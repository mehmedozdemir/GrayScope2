using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GrayScope.App.Views;

public static class Converters
{
    /// <summary>Stored UTC timestamps → local time for display.</summary>
    public static readonly IValueConverter UtcToLocal =
        new FuncValueConverter<DateTime, DateTime>(value => value.Kind == DateTimeKind.Local ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime());

    /// <summary>Run-history parameters as "A=1 · B=2".</summary>
    public static readonly IValueConverter ParametersText =
        new FuncValueConverter<IReadOnlyDictionary<string, string>?, string>(value =>
            value is null || value.Count == 0 ? "parametresiz" : string.Join(" · ", value.Select(p => $"{p.Key}={p.Value}")));
}

/// <summary>
/// Grid cell background: search hit → amber, new live-tail row → green tint, otherwise transparent.
/// Values: [0] bool[] hits, [1] bool isNew. Parameter: column index.
/// </summary>
public sealed class CellBackgroundConverter(int column) : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var hit = values.Count > 0 && values[0] is bool[] hits && column < hits.Length && hits[column];
        var isNew = values.Count > 1 && values[1] is true;
        var key = hit ? "HitBrush" : isNew ? "NewRowBrush" : null;
        if (key is null || Application.Current is not { } app)
        {
            return Brushes.Transparent;
        }
        return app.TryGetResource(key, app.ActualThemeVariant, out var brush) ? brush : Brushes.Transparent;
    }
}
