using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrayScope.App.Services;
using GrayScope.Core.Services;
using GrayScope.Infrastructure.Export;

namespace GrayScope.App.ViewModels;

/// <summary>Grid row. <see cref="Display"/> is what the user sees; sorting uses the raw cells.</summary>
public sealed partial class ResultRowViewModel : ObservableObject
{
    public ResultRowViewModel(ResultRecord record, int timestampColumn)
    {
        Record = record;
        Display = (string[])record.Cells.Clone();
        if (timestampColumn >= 0 && record.Timestamp is { } ts)
        {
            Display[timestampColumn] = ts.ToLocalTime().ToString(ResultsViewModel.TimestampDisplayFormat, CultureInfo.InvariantCulture);
        }
        _hits = new bool[Display.Length];
    }

    public ResultRecord Record { get; }
    public string[] Display { get; }

    [ObservableProperty]
    private bool[] _hits;

    [ObservableProperty]
    private bool _isNew;
}

/// <summary>A grid filter shown as a removable chip.</summary>
public sealed record FilterChip(string Label, Func<ResultRowViewModel, bool> Predicate);

public sealed record FieldValueViewModel(string Name, string Value);

public sealed partial class ResultsViewModel : ObservableObject, IDisposable
{
    public const string TimestampDisplayFormat = "dd.MM.yyyy HH:mm:ss.fff";

    private static readonly JsonSerializerOptions RowJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly AppServices _app;
    private readonly Func<string, bool, Task> _addToQuery;
    private Regex? _pattern;
    private CancellationTokenSource? _detailCts;

    public ResultsViewModel(AppServices app, Func<string, bool, Task> addToQuery)
    {
        _app = app;
        _addToQuery = addToQuery;
        View = new DataGridCollectionView(Rows) { Filter = o => o is ResultRowViewModel r && Matches(r) };
        Filters.CollectionChanged += (_, _) => Refresh();
    }

    /// <summary>Raised when the column set changes so the view can rebuild grid columns.</summary>
    public event EventHandler? ColumnsChanged;

    public IReadOnlyList<string> Columns { get; private set; } = [];
    public ObservableCollection<ResultRowViewModel> Rows { get; } = [];
    public DataGridCollectionView View { get; }
    public ObservableCollection<FilterChip> Filters { get; } = [];
    public ObservableCollection<FieldValueViewModel> DetailFields { get; } = [];
    public ObservableCollection<HistogramBucket> Histogram { get; } = [];
    public ObservableCollection<ValueCount> TopValues { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows), nameof(IsEmptyResult), nameof(CountText))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _visibleCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyResult))]
    private bool _hasResult;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isFilterMode;

    [ObservableProperty]
    private ResultRowViewModel? _selectedRow;

    [ObservableProperty]
    private bool _isDetailOpen = true;

    [ObservableProperty]
    private bool _isDetailLoading;

    [ObservableProperty]
    private string? _detailNotice;

    [ObservableProperty]
    private string _detailFilter = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTableView))]
    private bool _isAnalysisView;

    /// <summary>Counterpart of <see cref="IsAnalysisView"/> for the segmented control (only "true" is applied).</summary>
    public bool IsTableView
    {
        get => !IsAnalysisView;
        set
        {
            if (value)
            {
                IsAnalysisView = false;
            }
        }
    }

    [ObservableProperty]
    private string? _analysisField;

    [ObservableProperty]
    private int _analysisCardinality;

    public bool HasRows => TotalCount > 0;
    public bool IsEmptyResult => HasResult && TotalCount == 0;
    public bool HasFilters => Filters.Count > 0 || (IsFilterMode && _pattern is not null);

    public string CountText => VisibleCount == TotalCount
        ? $"{TotalCount:N0} kayıt"
        : $"{VisibleCount:N0} / {TotalCount:N0} kayıt";

    private int TimestampColumn => Columns.ToList().IndexOf(ResultTableBuilder.TimestampField);

    // ── Loading results ─────────────────────────────────────────────────────────────

    public void SetTable(ResultTable table)
    {
        var columnsChanged = !Columns.SequenceEqual(table.Columns);
        Columns = table.Columns;
        // A new run never mixes with the previous result set or its filters (FR-Query-Run-02).
        Filters.Clear();
        Rows.Clear();
        var tsColumn = TimestampColumn;
        foreach (var record in table.Rows)
        {
            Rows.Add(new ResultRowViewModel(record, tsColumn));
        }
        AfterRowsChanged(columnsChanged);
    }

    /// <summary>Live tail: prepends new rows (flagged) and trims rows beyond the buffer.</summary>
    public void ApplyLive(IReadOnlyList<string> columns, IReadOnlyList<ResultRecord> fresh, int capacity)
    {
        if (!Columns.SequenceEqual(columns))
        {
            Columns = columns;
            Rows.Clear();
            AfterRowsChanged(columnsChanged: true);
        }
        ClearNewFlags();
        var tsColumn = TimestampColumn;
        foreach (var record in fresh.OrderBy(r => r.Timestamp))
        {
            Rows.Insert(0, new ResultRowViewModel(record, tsColumn) { IsNew = true });
        }
        while (Rows.Count > capacity)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
        AfterRowsChanged(columnsChanged: false);
    }

    /// <summary>Removes the "new row" highlight (live tail stopped).</summary>
    public void ClearNewFlags()
    {
        foreach (var row in Rows.Where(r => r.IsNew))
        {
            row.IsNew = false;
        }
    }

    public void Clear()
    {
        Rows.Clear();
        Filters.Clear();
        HasResult = false;
        AfterRowsChanged(columnsChanged: false);
    }

    private void AfterRowsChanged(bool columnsChanged)
    {
        HasResult = true;
        if (columnsChanged)
        {
            Filters.Clear();
            OnPropertyChanged(nameof(Columns));
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
            AnalysisField = PickAnalysisField();
        }
        TotalCount = Rows.Count;
        ApplySearch();
        RecomputeAnalysis();
    }

    // ── Search & filters ────────────────────────────────────────────────────────────

    partial void OnSearchTextChanged(string value) => ApplySearch();
    partial void OnIsFilterModeChanged(bool value) => ApplySearch();

    /// <summary>Contains-match with <c>*</c> and <c>?</c> wildcards, case-insensitive.</summary>
    internal static Regex? CompileSearch(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return null;
        }
        var pattern = Regex.Escape(term.Trim()).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal);
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    }

    private void ApplySearch()
    {
        _pattern = CompileSearch(SearchText);
        foreach (var row in Rows)
        {
            row.Hits = _pattern is null || IsFilterMode
                ? new bool[row.Display.Length]
                : row.Display.Select(SafeMatch).ToArray();
        }
        Refresh();
    }

    private bool SafeMatch(string text)
    {
        try
        {
            return _pattern!.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private bool Matches(ResultRowViewModel row)
    {
        if (Filters.Any(f => !f.Predicate(row)))
        {
            return false;
        }
        return !IsFilterMode || _pattern is null || row.Display.Any(SafeMatch);
    }

    private void Refresh()
    {
        View.Refresh();
        VisibleCount = View.Count;
        OnPropertyChanged(nameof(HasFilters));
    }

    [RelayCommand]
    private void RemoveFilter(FilterChip chip) => Filters.Remove(chip);

    [RelayCommand]
    private void ClearFilters()
    {
        Filters.Clear();
        SearchText = "";
    }

    public void AddColumnFilter(int column, string value)
    {
        var target = value == ResultAnalyzer.EmptyLabel ? "" : value;
        Filters.Add(new FilterChip($"{Columns[column]} = {Shorten(value)}", r => r.Record.Cells[column] == target));
    }

    [RelayCommand]
    private void FilterByBucket(HistogramBucket bucket)
    {
        var label = $"{bucket.Start.ToLocalTime():HH:mm:ss} – {bucket.End.ToLocalTime():HH:mm:ss}";
        Filters.Add(new FilterChip($"Zaman: {label}", r => r.Record.Timestamp >= bucket.Start && r.Record.Timestamp < bucket.End));
    }

    [RelayCommand]
    private void FilterByValue(ValueCount value)
    {
        var column = Columns.ToList().IndexOf(AnalysisField ?? "");
        if (column >= 0)
        {
            AddColumnFilter(column, value.Value);
        }
    }

    // ── Analysis ────────────────────────────────────────────────────────────────────

    partial void OnAnalysisFieldChanged(string? value) => RecomputeTopValues();

    private string? PickAnalysisField()
    {
        // Prefer a categorical column (few distinct values) over ids and free text.
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i] == ResultTableBuilder.TimestampField)
            {
                continue;
            }
            var cardinality = ResultAnalyzer.Cardinality(Rows.Select(r => r.Record.Cells[i]));
            if (cardinality is > 1 and <= 30)
            {
                return Columns[i];
            }
        }
        return Columns.FirstOrDefault(c => c != ResultTableBuilder.TimestampField);
    }

    private void RecomputeAnalysis()
    {
        Histogram.Clear();
        foreach (var bucket in ResultAnalyzer.Histogram(Rows.Select(r => r.Record.Timestamp)))
        {
            Histogram.Add(bucket);
        }
        RecomputeTopValues();
    }

    private void RecomputeTopValues()
    {
        TopValues.Clear();
        var column = Columns.ToList().IndexOf(AnalysisField ?? "");
        if (column < 0)
        {
            AnalysisCardinality = 0;
            return;
        }
        var values = Rows.Select(r => r.Record.Cells[column]).ToList();
        AnalysisCardinality = ResultAnalyzer.Cardinality(values);
        foreach (var value in ResultAnalyzer.TopValues(values))
        {
            TopValues.Add(value);
        }
    }

    // ── Row detail ──────────────────────────────────────────────────────────────────

    partial void OnSelectedRowChanged(ResultRowViewModel? value) => _ = LoadDetailAsync(value);
    partial void OnDetailFilterChanged(string value) => _ = LoadDetailAsync(SelectedRow, refetch: false);

    private IReadOnlyDictionary<string, string>? _detailCache;

    private async Task LoadDetailAsync(ResultRowViewModel? row, bool refetch = true)
    {
        if (row is null)
        {
            DetailFields.Clear();
            DetailNotice = null;
            return;
        }
        if (refetch || _detailCache is null)
        {
            _detailCts?.Cancel();
            _detailCts = new CancellationTokenSource();
            var ct = _detailCts.Token;
            IsDetailLoading = true;
            try
            {
                var detail = await _app.MessageDetail.GetAsync(row.Record, ct);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _detailCache = detail.Fields;
                DetailNotice = detail.Notice;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                IsDetailLoading = false;
            }
        }

        DetailFields.Clear();
        var filter = DetailFilter.Trim();
        foreach (var (name, value) in _detailCache.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase) || value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                DetailFields.Add(new FieldValueViewModel(name, value));
            }
        }
    }

    [RelayCommand]
    private Task CopyFieldValue(FieldValueViewModel field) => CopyAsync(field.Value);

    [RelayCommand]
    private Task IncludeField(FieldValueViewModel field) => _addToQuery(Core.Querying.LuceneTerms.FieldEquals(field.Name, field.Value), false);

    [RelayCommand]
    private Task ExcludeField(FieldValueViewModel field) => _addToQuery(Core.Querying.LuceneTerms.FieldEquals(field.Name, field.Value), true);

    [RelayCommand]
    private void ShowFieldValue(FieldValueViewModel field) => _app.Dialogs.ShowValue(field.Name, field.Value);

    // ── Grid actions (called from the view's context menu) ──────────────────────────

    public Task CopyAsync(string text)
    {
        _app.Toasts.Success("Panoya kopyalandı.");
        return _app.Clipboard.CopyAsync(text);
    }

    public Task CopyRowAsJsonAsync(ResultRowViewModel row)
    {
        var map = Columns.Select((c, i) => (c, row.Record.Cells[i])).ToDictionary(x => x.c, x => x.Item2);
        return CopyAsync(JsonSerializer.Serialize(map, RowJsonOptions));
    }

    public Task AddCellToQueryAsync(int column, string value, bool exclude) =>
        _addToQuery(Core.Querying.LuceneTerms.FieldEquals(Columns[column], value), exclude);

    /// <summary>Synthetic batch columns (server / parameter) are not real Graylog fields.</summary>
    public bool IsContextColumn(int column) =>
        column < Columns.Count && (Columns[column].StartsWith('{') || Columns[column] == ResultTableBuilder.ServerColumn);

    // ── Export ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task ExportCsv() => ExportAsync("CSV", new FileTypeFilter("CSV dosyası", "*.csv"), ResultExporter.WriteCsvAsync);

    [RelayCommand]
    private Task ExportExcel() => ExportAsync("Excel", new FileTypeFilter("Excel çalışma kitabı", "*.xlsx"), ResultExporter.WriteExcelAsync);

    public string ExportBaseName { get; set; } = "grayscope";

    private async Task ExportAsync(string kind, FileTypeFilter filter, Func<string, IReadOnlyList<string>, IEnumerable<IReadOnlyList<string>>, CancellationToken, Task> write)
    {
        var name = $"{ExportBaseName}_{DateTime.Now:yyyyMMdd_HHmm}{Path.GetExtension(filter.Pattern)}";
        var path = await _app.Files.SaveAsync($"{kind} olarak dışa aktar", name, filter);
        if (path is null)
        {
            return;
        }
        try
        {
            // Visible rows in the current sort order — what you see is what you export.
            var rows = View.Cast<ResultRowViewModel>().Select(r => (IReadOnlyList<string>)r.Display).ToList();
            await write(path, Columns, rows, CancellationToken.None);
            _app.Toasts.Success($"{rows.Count:N0} kayıt dışa aktarıldı: {Path.GetFileName(path)}");
        }
        catch (IOException ex)
        {
            _app.Toasts.Error($"Dosya yazılamadı: {ex.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            _app.Toasts.Error("Bu konuma yazma izniniz yok.");
        }
    }

    public void Dispose()
    {
        _detailCts?.Cancel();
        _detailCts?.Dispose();
    }

    private static string Shorten(string value) => value.Length > 40 ? value[..39] + "…" : value;
}
