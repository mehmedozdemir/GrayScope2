using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using GrayScope.App.ViewModels;
using GrayScope.Core.Services;

namespace GrayScope.App.Views;

/// <summary>
/// Query tab. The grid's columns are dynamic (one per result field), so they are built here;
/// everything else is plain binding.
/// </summary>
public partial class QueryTabView : UserControl
{
    private const double DefaultColumnWidth = 170;
    private const double MessageColumnWidth = 520;
    private const double TimestampColumnWidth = 180;

    /// <summary>Grid cells are one step smaller than body text: dense, scannable data.</summary>
    private const double CellFontSize = 12;

    private ResultsViewModel? _results;

    public QueryTabView()
    {
        InitializeComponent();
        ResultGrid.DoubleTapped += OnGridDoubleTapped;
        ResultGrid.ContextRequested += OnGridContextRequested;
        ResultGrid.KeyDown += OnGridKeyDown;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private QueryTabViewModel? Tab => DataContext as QueryTabViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_results is not null)
        {
            _results.ColumnsChanged -= OnColumnsChanged;
        }
        _results = Tab?.Results;
        if (_results is not null)
        {
            _results.ColumnsChanged += OnColumnsChanged;
        }
        RebuildColumns();
    }

    private void OnColumnsChanged(object? sender, EventArgs e) => RebuildColumns();

    private void RebuildColumns()
    {
        ResultGrid.Columns.Clear();
        if (_results is null)
        {
            return;
        }
        for (var i = 0; i < _results.Columns.Count; i++)
        {
            ResultGrid.Columns.Add(CreateColumn(_results.Columns[i], i, _results.IsContextColumn(i)));
        }
    }

    private static DataGridTemplateColumn CreateColumn(string name, int index, bool isContext)
    {
        var width = name switch
        {
            "message" or "full_message" => MessageColumnWidth,
            ResultTableBuilder.TimestampField => TimestampColumnWidth,
            _ => DefaultColumnWidth,
        };
        return new DataGridTemplateColumn
        {
            Header = name,
            Width = new DataGridLength(width),
            MinWidth = 60,
            CanUserSort = true,
            // Sort by the raw value (ISO timestamps, numbers) rather than the formatted display.
            CustomSortComparer = new RowComparer(index),
            CellTemplate = new FuncDataTemplate<ResultRowViewModel>((_, _) => CreateCell(index, isContext), supportsRecycling: true),
        };
    }

    private static Border CreateCell(int index, bool isContext)
    {
        var text = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            FontSize = CellFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0),
            [!TextBlock.TextProperty] = new ReflectionBinding($"Display[{index}]"),
        };
        if (isContext)
        {
            text.FontWeight = FontWeight.Medium;
        }
        var border = new Border
        {
            Tag = index,
            Child = text,
            Background = Brushes.Transparent,
        };
        border.Bind(Border.BackgroundProperty, new MultiBinding
        {
            Converter = new CellBackgroundConverter(index),
            Bindings = { new ReflectionBinding(nameof(ResultRowViewModel.Hits)), new ReflectionBinding(nameof(ResultRowViewModel.IsNew)) },
        });
        return border;
    }

    /// <summary>Finds the row and column under an event source (cells carry their column index in <see cref="Control.Tag"/>).</summary>
    private static (ResultRowViewModel Row, int Column)? HitTest(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Border { Tag: int column, DataContext: ResultRowViewModel row })
            {
                return (row, column);
            }
            if (visual is DataGrid)
            {
                break;
            }
        }
        return null;
    }

    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (HitTest(e.Source) is { } hit && _results is not null)
        {
            Tab?.Results.ShowFieldValueCommand.Execute(new FieldValueViewModel(_results.Columns[hit.Column], hit.Row.Record.Cells[hit.Column]));
        }
    }

    private void OnGridContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_results is not { } results || HitTest(e.Source) is not { } hit)
        {
            return;
        }
        var (row, column) = hit;
        var value = row.Record.Cells[column];
        var field = results.Columns[column];
        results.SelectedRow = row;

        var menu = new ContextMenu();
        void Add(string header, Func<Task> action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }

        Add("Hücreyi kopyala", () => results.CopyAsync(row.Display[column]));
        Add("Satırı kopyala (JSON)", () => results.CopyRowAsJsonAsync(row));
        Add("Değeri büyük pencerede göster", () =>
        {
            results.ShowFieldValueCommand.Execute(new FieldValueViewModel(field, value));
            return Task.CompletedTask;
        });
        menu.Items.Add(new Separator());
        var isField = !results.IsContextColumn(column);
        Add($"Tabloyu filtrele: {field} = …", () =>
        {
            results.AddColumnFilter(column, value.Length == 0 ? Core.Services.ResultAnalyzer.EmptyLabel : value);
            return Task.CompletedTask;
        });
        Add("Sorguya ekle (AND)", () => results.AddCellToQueryAsync(column, value, exclude: false), isField);
        Add("Sorgudan hariç tut (AND NOT)", () => results.AddCellToQueryAsync(column, value, exclude: true), isField);
        menu.Open(ResultGrid);
        e.Handled = true;
    }

    private async void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _results is { SelectedRow: { } row } results)
        {
            var column = ResultGrid.CurrentColumn is { } current ? ResultGrid.Columns.IndexOf(current) : -1;
            e.Handled = true;
            await (column >= 0 ? results.CopyAsync(row.Display[column]) : results.CopyRowAsJsonAsync(row));
        }
    }

    /// <summary>Enter in a parameter box runs the query; Ctrl+F focuses the grid search.</summary>
    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && e.Source is TextBox { Classes: var classes } && classes.Contains("param"))
        {
            e.Handled = true;
            if (Tab is { } tab)
            {
                await tab.RunIfIdleAsync();
            }
        }
        else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            if (_results is not null)
            {
                _results.IsAnalysisView = false;
            }
            GridSearch.Focus();
            GridSearch.SelectAll();
        }
    }

    private sealed class RowComparer(int column) : IComparer
    {
        public int Compare(object? x, object? y) =>
            CellValueComparer.Instance.Compare(
                (x as ResultRowViewModel)?.Record.Cells[column],
                (y as ResultRowViewModel)?.Record.Cells[column]);
    }
}
