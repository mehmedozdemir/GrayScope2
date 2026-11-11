using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;
using GrayScope.Core.Querying;
using GrayScope.Core.Services;

namespace GrayScope.App.ViewModels;

/// <summary>New / edit query form. <see cref="Saved"/> is set when the user saves successfully.</summary>
public sealed partial class QueryEditorViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _app;
    private readonly SavedQuery _original;
    private readonly HashSet<string> _selectedStreamIds;
    private CancellationTokenSource? _streamsCts;

    public QueryEditorViewModel(AppServices app, SavedQuery query, IReadOnlyList<GraylogProfile> profiles, IReadOnlyList<FolderOption> folders)
    {
        _app = app;
        _original = query;
        Profiles = profiles.Where(p => p.IsActive || p.Id == query.ProfileId).ToList();
        Folders = folders;
        _name = query.Name;
        _description = query.Description ?? "";
        _template = query.Template;
        _resultSize = query.ResultSize;
        _sortField = query.DefaultSortField ?? "";
        _sortDescending = query.DefaultSortDirection == SortDirection.Descending;
        _selectedFolder = folders.FirstOrDefault(f => f.Id == query.FolderId) ?? folders[0];
        _selectedProfile = Profiles.FirstOrDefault(p => p.Id == query.ProfileId) ?? Profiles.FirstOrDefault(p => p.IsDefault) ?? (Profiles.Count > 0 ? Profiles[0] : null);
        _selectedStreamIds = query.Streams.Select(s => s.StreamId).ToHashSet(StringComparer.Ordinal);
        foreach (var field in query.Fields)
        {
            Fields.Add(field);
        }
        TimeRange.Load(query.TimeRange);
        _ = LoadStreamsAsync(forceRefresh: false);
        _ = LoadFieldNamesAsync();
    }

    public bool IsNew => _original.Id == 0;
    public string WindowTitle => IsNew ? "Yeni Sorgu" : $"Sorguyu Düzenle — {_original.Name}";
    public IReadOnlyList<GraylogProfile> Profiles { get; }
    public IReadOnlyList<FolderOption> Folders { get; }
    public TimeRangeEditorViewModel TimeRange { get; } = new();
    public ObservableCollection<StreamChoiceViewModel> Streams { get; } = [];
    public ObservableCollection<string> Fields { get; } = [];
    public ObservableCollection<string> FieldSuggestions { get; } = [];
    public SavedQuery? Saved { get; private set; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _template;
    [ObservableProperty] private decimal? _resultSize;
    [ObservableProperty] private string _sortField;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortAscending))]
    private bool _sortDescending;

    /// <summary>Counterpart of <see cref="SortDescending"/> for the segmented control.</summary>
    public bool SortAscending
    {
        get => !SortDescending;
        set
        {
            if (value)
            {
                SortDescending = false;
            }
        }
    }
    [ObservableProperty] private FolderOption _selectedFolder;
    [ObservableProperty] private GraylogProfile? _selectedProfile;
    [ObservableProperty] private string _streamFilter = "";
    [ObservableProperty] private bool _isLoadingStreams;
    [ObservableProperty] private string? _streamError;
    [ObservableProperty] private string _newField = "";
    [ObservableProperty] private string? _nameError;
    [ObservableProperty] private string? _templateError;
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private bool _isSaving;

    public string ParameterSummary
    {
        get
        {
            var parameters = QueryTemplate.ExtractParameters(Template);
            return parameters.Count == 0
                ? "Parametre yok. Çalıştırma anında değer istemek için {Ad} yazın; {Ad?} opsiyonel, {Ad:34} varsayılan değerli."
                : "Parametreler: " + string.Join(", ", parameters.Select(p => p.Name + (p.IsOptional ? "?" : "") + (p.DefaultValue.Length > 0 ? $" = {p.DefaultValue}" : "")));
        }
    }

    public int SelectedStreamCount => Streams.Count(s => s.IsSelected);

    partial void OnTemplateChanged(string value)
    {
        TemplateError = null;
        OnPropertyChanged(nameof(ParameterSummary));
    }

    partial void OnNameChanged(string value) => NameError = null;

    partial void OnSelectedProfileChanged(GraylogProfile? value)
    {
        // Stream ids belong to a server; switching server invalidates the selection.
        if (value is not null && value.Id != _original.ProfileId)
        {
            _selectedStreamIds.Clear();
        }
        _ = LoadStreamsAsync(forceRefresh: false);
        _ = LoadFieldNamesAsync();
    }

    partial void OnStreamFilterChanged(string value)
    {
        foreach (var stream in Streams)
        {
            stream.IsVisible = value.Length == 0 || stream.Title.Contains(value, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    [RelayCommand]
    private Task RefreshStreams() => LoadStreamsAsync(forceRefresh: true);

    private async Task LoadStreamsAsync(bool forceRefresh)
    {
        _streamsCts?.Cancel();
        _streamsCts = new CancellationTokenSource();
        var ct = _streamsCts.Token;
        foreach (var s in Streams.Where(s => s.IsSelected))
        {
            _selectedStreamIds.Add(s.Id);
        }
        foreach (var s in Streams.Where(s => !s.IsSelected))
        {
            _selectedStreamIds.Remove(s.Id);
        }
        Streams.Clear();
        StreamError = null;
        if (SelectedProfile is not { } profile)
        {
            return;
        }
        if (profile.NeedsToken)
        {
            StreamError = "Bu profilin token'ı yok; stream listesi alınamıyor.";
            return;
        }

        IsLoadingStreams = true;
        try
        {
            var streams = await _app.Catalog.GetStreamsAsync(profile, forceRefresh, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }
            foreach (var stream in streams)
            {
                var choice = new StreamChoiceViewModel(stream.Id, stream.Title, _selectedStreamIds.Contains(stream.Id));
                choice.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedStreamCount));
                Streams.Add(choice);
            }
            // Keep saved streams that no longer exist visible, so saving does not silently drop them.
            foreach (var missing in _original.Streams.Where(s => _selectedStreamIds.Contains(s.StreamId) && streams.All(x => x.Id != s.StreamId)))
            {
                Streams.Add(new StreamChoiceViewModel(missing.StreamId, missing.StreamName + " (sunucuda yok)", true));
            }
            OnStreamFilterChanged(StreamFilter);
        }
        catch (OperationCanceledException)
        {
        }
        catch (GrayScopeException ex)
        {
            StreamError = $"Stream listesi alınamadı: {ex.Message}";
        }
        finally
        {
            IsLoadingStreams = false;
            OnPropertyChanged(nameof(SelectedStreamCount));
        }
    }

    private async Task LoadFieldNamesAsync()
    {
        FieldSuggestions.Clear();
        if (SelectedProfile is not { NeedsToken: false } profile)
        {
            return;
        }
        try
        {
            foreach (var field in await _app.Catalog.GetFieldsAsync(profile))
            {
                FieldSuggestions.Add(field);
            }
            OnPropertyChanged(nameof(FieldSuggestions));
        }
        catch (GrayScopeException)
        {
            // Suggestions are optional; free text entry still works.
        }
    }

    [RelayCommand]
    private void AddField()
    {
        foreach (var field in NewField.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Fields.Contains(field))
            {
                Fields.Add(field);
            }
        }
        NewField = "";
    }

    [RelayCommand]
    private void RemoveField(string field) => Fields.Remove(field);

    [RelayCommand]
    private void MoveFieldLeft(string field)
    {
        var index = Fields.IndexOf(field);
        if (index > 0)
        {
            Fields.Move(index, index - 1);
        }
    }

    public void Dispose()
    {
        _streamsCts?.Cancel();
        _streamsCts?.Dispose();
    }

    /// <summary>Validates and saves. Returns true when the dialog may close.</summary>
    public async Task<bool> SaveAsync()
    {
        FormError = null;
        if (NewField.Trim().Length > 0)
        {
            AddField();
        }
        NameError = string.IsNullOrWhiteSpace(Name) ? "Sorgu adı zorunludur." : null;
        TemplateError = string.IsNullOrWhiteSpace(Template) ? "Sorgu metni zorunludur." : null;
        if (NameError is not null || TemplateError is not null)
        {
            return false;
        }

        IsSaving = true;
        try
        {
            var query = _original.Clone();
            query.Name = Name;
            query.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
            query.Template = Template;
            query.ProfileId = SelectedProfile?.Id ?? 0;
            query.FolderId = SelectedFolder.Id;
            query.TimeRange = TimeRange.ToSpec();
            query.Fields = [.. Fields];
            query.DefaultSortField = string.IsNullOrWhiteSpace(SortField) ? null : SortField.Trim();
            query.DefaultSortDirection = SortDescending ? SortDirection.Descending : SortDirection.Ascending;
            query.ResultSize = (int)(ResultSize ?? 0);
            query.Streams = Streams.Count > 0 || StreamError is null
                ? Streams.Where(s => s.IsSelected).Select(s => new StreamRef(s.Id, s.Title.Replace(" (sunucuda yok)", "", StringComparison.Ordinal))).ToList()
                : _original.Streams; // stream list failed to load: keep what was saved (FR-Stream-01)
            Saved = await _app.Queries.SaveAsync(query);
            return true;
        }
        catch (ValidationException ex)
        {
            FormError = ex.Message;
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }
}
