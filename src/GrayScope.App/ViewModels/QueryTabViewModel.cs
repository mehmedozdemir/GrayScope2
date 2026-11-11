using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;
using GrayScope.Core.Querying;
using GrayScope.Core.Services;

namespace GrayScope.App.ViewModels;

public sealed record LiveIntervalOption(string Label, int Seconds)
{
    public override string ToString() => Label;
}

public sealed record OutcomeMessage(string Text, bool IsError);

/// <summary>One open query (document tab). Edits here are ad-hoc until "Kaydet" is used.</summary>
public sealed partial class QueryTabViewModel : ObservableObject, IDisposable
{
    public static IReadOnlyList<LiveIntervalOption> LiveIntervals { get; } =
        [new("5 sn", 5), new("10 sn", 10), new("30 sn", 30), new("1 dk", 60)];

    private readonly AppServices _app;
    private readonly LiveTailBuffer _tailBuffer = new();
    private CancellationTokenSource? _runCts;
    private DispatcherTimer? _liveTimer;
    private SavedQuery _query;

    public QueryTabViewModel(AppServices app, SavedQuery query, IReadOnlyList<GraylogProfile> profiles)
    {
        _app = app;
        _query = query;
        Results = new ResultsViewModel(app, AddTermToQueryAsync) { ExportBaseName = SafeFileName(query.Name) };
        TimeRange.Load(query.TimeRange);
        TimeRange.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TimeRangeEditorViewModel.Kind) && TimeRange.IsAbsolute && IsLive)
            {
                IsLive = false;
            }
        };
        _templateText = query.Template;
        _size = query.ResultSize;
        SetProfiles(profiles);
        RebuildParameters();
        _ = LoadFieldsAsync();
    }

    public SavedQuery Query => _query;
    public long QueryId => _query.Id;
    public ResultsViewModel Results { get; }
    public TimeRangeEditorViewModel TimeRange { get; } = new();
    public ObservableCollection<ParameterInputViewModel> Parameters { get; } = [];
    public ObservableCollection<ProfileChoiceViewModel> Targets { get; } = [];
    public ObservableCollection<OutcomeMessage> Outcomes { get; } = [];
    public ObservableCollection<QueryRun> History { get; } = [];

    /// <summary>Field names for editor completion (loaded once per profile).</summary>
    public IReadOnlyList<string> FieldNames { get; private set; } = [];

    public string Title => _query.Name;
    public string ProfileName => Targets.FirstOrDefault(t => t.Profile.Id == _query.ProfileId)?.Name ?? "?";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplateDirty))]
    private string _templateText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplateDirty))]
    private decimal? _size;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun), nameof(RunLabel))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParameterHint))]
    private bool _isBatch;

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private LiveIntervalOption _liveInterval = LiveIntervals[1];

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string? _validationText;

    [ObservableProperty]
    private DateTime? _lastRunAt;

    public bool IsTemplateDirty => TemplateText != _query.Template || (int)(Size ?? 0) != _query.ResultSize;
    public bool HasParameters => Parameters.Count > 0;
    public bool CanRun => !IsRunning;
    public string RunLabel => IsRunning ? "Durdur" : "Çalıştır";
    public string ParameterHint => IsBatch ? "Toplu mod: değerleri virgülle ayırın (örn. 34, 35, 6)" : "";

    public string TargetsLabel
    {
        get
        {
            var selected = Targets.Where(t => t.IsSelected).ToList();
            return selected.Count switch
            {
                0 => "Sunucu seçin",
                1 => selected[0].Name,
                _ => $"{selected.Count} sunucu",
            };
        }
    }

    // ── Setup ───────────────────────────────────────────────────────────────────────

    public void SetProfiles(IReadOnlyList<GraylogProfile> profiles)
    {
        var previouslySelected = Targets.Where(t => t.IsSelected).Select(t => t.Profile.Id).ToHashSet();
        Targets.Clear();
        foreach (var profile in profiles.Where(p => p.IsActive || p.Id == _query.ProfileId))
        {
            var selected = previouslySelected.Count > 0 ? previouslySelected.Contains(profile.Id) : profile.Id == _query.ProfileId;
            var choice = new ProfileChoiceViewModel(profile, selected);
            choice.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TargetsLabel));
            Targets.Add(choice);
        }
        OnPropertyChanged(nameof(TargetsLabel));
        OnPropertyChanged(nameof(ProfileName));
    }

    /// <summary>Replaces the underlying query after it was edited in the form.</summary>
    public void Reload(SavedQuery query, IReadOnlyList<GraylogProfile> profiles)
    {
        _query = query;
        TemplateText = query.Template;
        Size = query.ResultSize;
        TimeRange.Load(query.TimeRange);
        Targets.Clear();
        SetProfiles(profiles);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsTemplateDirty));
        _ = LoadFieldsAsync();
    }

    partial void OnTemplateTextChanged(string value) => RebuildParameters();

    private void RebuildParameters()
    {
        var previous = Parameters.ToDictionary(p => p.Name, p => p.Value);
        var parsed = QueryTemplate.ExtractParameters(TemplateText);
        if (parsed.Select(p => p.Name).SequenceEqual(Parameters.Select(p => p.Name)))
        {
            return;
        }
        Parameters.Clear();
        foreach (var parameter in parsed)
        {
            var input = new ParameterInputViewModel(parameter);
            if (previous.TryGetValue(parameter.Name, out var value))
            {
                input.Value = value;
            }
            Parameters.Add(input);
        }
        OnPropertyChanged(nameof(HasParameters));
    }

    private async Task LoadFieldsAsync()
    {
        var profile = Targets.FirstOrDefault(t => t.Profile.Id == _query.ProfileId)?.Profile;
        if (profile is null || profile.NeedsToken)
        {
            return;
        }
        try
        {
            FieldNames = await _app.Catalog.GetFieldsAsync(profile);
            OnPropertyChanged(nameof(FieldNames));
        }
        catch (GrayScopeException)
        {
            // Completion is a convenience; a failing field list must not disturb the tab.
        }
    }

    // ── Run ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Run()
    {
        if (IsRunning)
        {
            _runCts?.Cancel();
            return;
        }
        if (IsLive)
        {
            _tailBuffer.Clear();
        }
        await ExecuteAsync(isLiveTick: false);
    }

    /// <summary>Starts a run unless one is in progress (keyboard shortcut path).</summary>
    public Task RunIfIdleAsync() => IsRunning ? Task.CompletedTask : Run();

    private async Task ExecuteAsync(bool isLiveTick)
    {
        ValidationText = null;
        ExecutionRequest request;
        try
        {
            request = BuildRequest(isLiveTick);
            QueryExecutionService.BuildPlan(request);
        }
        catch (ValidationException ex)
        {
            ValidationText = ex.Message;
            IsLive = false;
            return;
        }

        _runCts = new CancellationTokenSource();
        IsRunning = true;
        ErrorText = null;
        if (!isLiveTick)
        {
            StatusText = "Çalıştırılıyor…";
        }
        try
        {
            var table = await _app.Execution.ExecuteAsync(request, _runCts.Token);
            ShowOutcomes(table);
            if (table.IsTotalFailure)
            {
                ErrorText = table.Outcomes[0].Error;
                if (!isLiveTick)
                {
                    Results.Clear();
                }
                IsLive = false;
                return;
            }

            if (IsLive)
            {
                if (!isLiveTick)
                {
                    Results.Clear();
                }
                var fresh = _tailBuffer.Merge(table.Rows);
                Results.ApplyLive(table.Columns, fresh, LiveTailBuffer.DefaultCapacity);
                StatusText = $"Canlı · {DateTime.Now:HH:mm:ss} · {fresh.Count} yeni";
            }
            else
            {
                Results.SetTable(table);
                StatusText = $"{table.Duration.TotalSeconds:0.00} sn · {table.Outcomes.Count} arama";
            }
            LastRunAt = DateTime.Now;
            if (!isLiveTick)
            {
                await LoadHistoryAsync();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "İptal edildi.";
            IsLive = false;
        }
        catch (GrayScopeException ex)
        {
            ErrorText = ex.Message;
            IsLive = false;
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private ExecutionRequest BuildRequest(bool isLiveTick)
    {
        var profiles = Targets.Where(t => t.IsSelected).Select(t => t.Profile).ToList();
        var size = (int)(Size ?? 0);
        if (size is < QueryService.MinResultSize or > QueryService.MaxResultSize)
        {
            throw new ValidationException($"Kayıt sayısı {QueryService.MinResultSize}–{QueryService.MaxResultSize} arasında olmalı (0 = tümü).");
        }
        return new ExecutionRequest
        {
            Query = _query,
            Template = TemplateText,
            Parameters = Parameters.ToDictionary(p => p.Name, p => p.Value ?? ""),
            IsBatch = IsBatch,
            Profiles = profiles,
            TimeRange = TimeRange.ToSpec(),
            Size = size,
            RecordHistory = !isLiveTick,
        };
    }

    private void ShowOutcomes(ResultTable table)
    {
        Outcomes.Clear();
        var showLabels = table.Outcomes.Count > 1;
        foreach (var outcome in table.Outcomes)
        {
            if (outcome.Error is not null && showLabels)
            {
                Outcomes.Add(new OutcomeMessage($"{outcome.Label}: {outcome.Error}", true));
            }
            foreach (var warning in outcome.Warnings)
            {
                Outcomes.Add(new OutcomeMessage($"{outcome.Profile.Name}: {warning}", false));
            }
        }
        if (table.IsPartialFailure)
        {
            _app.Toasts.Warning($"{table.Outcomes.Count} aramadan {table.FailedCount} tanesi başarısız oldu.");
        }
    }

    // ── Live tail ───────────────────────────────────────────────────────────────────

    partial void OnIsLiveChanged(bool value)
    {
        _liveTimer?.Stop();
        _liveTimer = null;
        if (!value)
        {
            Results.ClearNewFlags();
            if (StatusText?.StartsWith("Canlı", StringComparison.Ordinal) == true)
            {
                StatusText = "Canlı takip durduruldu.";
            }
            return;
        }
        if (TimeRange.IsAbsolute)
        {
            ValidationText = "Canlı takip mutlak zaman aralığıyla çalışmaz; göreli bir aralık seçin.";
            IsLive = false;
            return;
        }
        _tailBuffer.Clear();
        StartLiveTimer();
        _ = RunIfIdleAsync();
    }

    partial void OnLiveIntervalChanged(LiveIntervalOption value)
    {
        if (IsLive)
        {
            StartLiveTimer();
        }
    }

    private void StartLiveTimer()
    {
        _liveTimer?.Stop();
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(LiveInterval.Seconds) };
        _liveTimer.Tick += async (_, _) =>
        {
            if (!IsRunning && IsLive)
            {
                await ExecuteAsync(isLiveTick: true);
            }
        };
        _liveTimer.Start();
    }

    // ── History ─────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        if (_query.Id <= 0)
        {
            return;
        }
        History.Clear();
        foreach (var run in await _app.Queries.GetHistoryAsync(_query.Id))
        {
            History.Add(run);
        }
    }

    [RelayCommand]
    private void ApplyHistory(QueryRun run)
    {
        foreach (var parameter in Parameters)
        {
            if (run.Parameters.TryGetValue(parameter.Name, out var value))
            {
                parameter.Value = value;
            }
        }
        IsBatch = run.Parameters.Values.Any(v => v.Contains(',', StringComparison.Ordinal));
    }

    // ── Editing helpers ─────────────────────────────────────────────────────────────

    /// <summary>"Add to query" from grid/detail: appends a term and re-runs.</summary>
    private async Task AddTermToQueryAsync(string term, bool exclude)
    {
        TemplateText = LuceneTerms.Append(TemplateText, term, exclude);
        await RunIfIdleAsync();
    }

    [RelayCommand]
    private void RevertTemplate()
    {
        TemplateText = _query.Template;
        Size = _query.ResultSize;
    }

    /// <summary>Persists ad-hoc template / time range / size edits into the saved query.</summary>
    [RelayCommand]
    private async Task SaveChanges()
    {
        try
        {
            var updated = _query.Clone();
            updated.Template = TemplateText;
            updated.ResultSize = (int)(Size ?? 0);
            updated.TimeRange = TimeRange.ToSpec();
            _query = await _app.Queries.SaveAsync(updated);
            OnPropertyChanged(nameof(IsTemplateDirty));
            _app.Toasts.Success("Sorgu kaydedildi.");
        }
        catch (ValidationException ex)
        {
            ValidationText = ex.Message;
        }
    }

    [RelayCommand]
    private void SelectAllTargets(bool select)
    {
        foreach (var target in Targets)
        {
            target.IsSelected = select || target.Profile.Id == _query.ProfileId;
        }
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));

    /// <summary>Tab items expose this to screen readers / UI automation.</summary>
    public override string ToString() => Title;

    public void Dispose()
    {
        _liveTimer?.Stop();
        _runCts?.Cancel();
        _runCts?.Dispose();
        Results.Dispose();
    }
}
