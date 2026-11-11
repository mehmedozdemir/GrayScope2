using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;
using GrayScope.Core.Services;

namespace GrayScope.App.ViewModels;

public sealed record TimeUnitOption(string Label, int Seconds)
{
    public override string ToString() => Label;
}

public sealed record TimeRangePreset(string Label, int Seconds);

/// <summary>Edits a <see cref="TimeRangeSpec"/>; used both in the query form and in the run bar (ad-hoc override).</summary>
public sealed partial class TimeRangeEditorViewModel : ObservableObject
{
    public const string DateFormat = "dd.MM.yyyy HH:mm";
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static IReadOnlyList<TimeUnitOption> Units { get; } =
    [
        new("saniye", 1), new("dakika", 60), new("saat", 3600), new("gün", 86_400), new("hafta", 604_800), new("ay", 2_592_000), new("yıl", 31_536_000),
    ];

    public static IReadOnlyList<TimeRangePreset> Presets { get; } =
    [
        new("5 dk", 300), new("15 dk", 900), new("1 sa", 3600), new("4 sa", 14_400), new("24 sa", 86_400), new("7 gün", 604_800),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRelative), nameof(IsKeyword), nameof(IsAbsolute), nameof(Summary))]
    private TimeRangeKind _kind = TimeRangeKind.Relative;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private decimal? _relativeAmount = 5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private TimeUnitOption _relativeUnit = Units[1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _keyword = "last 1 hour";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _fromText = DateTime.Now.AddHours(-1).ToString(DateFormat, Tr);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _toText = DateTime.Now.ToString(DateFormat, Tr);

    [ObservableProperty]
    private string? _error;

    public bool IsRelative
    {
        get => Kind == TimeRangeKind.Relative;
        set { if (value) { Kind = TimeRangeKind.Relative; } }
    }

    public bool IsKeyword
    {
        get => Kind == TimeRangeKind.Keyword;
        set { if (value) { Kind = TimeRangeKind.Keyword; } }
    }

    public bool IsAbsolute
    {
        get => Kind == TimeRangeKind.Absolute;
        set { if (value) { Kind = TimeRangeKind.Absolute; } }
    }

    /// <summary>Short label for the run-bar button, e.g. "Son 15 dakika".</summary>
    public string Summary => Kind switch
    {
        TimeRangeKind.Keyword => $"“{Keyword}”",
        TimeRangeKind.Absolute => $"{FromText} – {ToText}",
        _ when RelativeAmount is null or 0 => "Tüm zamanlar",
        _ => $"Son {RelativeAmount:0} {RelativeUnit.Label}",
    };

    public void Load(TimeRangeSpec spec)
    {
        Kind = spec.Kind;
        var unit = Units.Reverse().FirstOrDefault(u => spec.RangeSeconds > 0 && spec.RangeSeconds % u.Seconds == 0) ?? Units[0];
        RelativeUnit = unit;
        RelativeAmount = spec.RangeSeconds / unit.Seconds;
        if (spec.Keyword is { Length: > 0 } keyword)
        {
            Keyword = keyword;
        }
        if (spec.From is { } from)
        {
            FromText = from.ToLocalTime().ToString(DateFormat, Tr);
        }
        if (spec.To is { } to)
        {
            ToText = to.ToLocalTime().ToString(DateFormat, Tr);
        }
        Error = null;
    }

    /// <summary>Builds and validates the spec; sets <see cref="Error"/> and rethrows on invalid input.</summary>
    public TimeRangeSpec ToSpec()
    {
        try
        {
            var spec = Kind switch
            {
                TimeRangeKind.Keyword => new TimeRangeSpec(TimeRangeKind.Keyword, Keyword: Keyword.Trim()),
                TimeRangeKind.Absolute => new TimeRangeSpec(TimeRangeKind.Absolute, From: ParseDate(FromText, "Başlangıç"), To: ParseDate(ToText, "Bitiş")),
                _ => new TimeRangeSpec(TimeRangeKind.Relative, (int)Math.Max(0, (RelativeAmount ?? 0) * RelativeUnit.Seconds)),
            };
            TimeRangeValidator.Validate(spec);
            Error = null;
            return spec;
        }
        catch (ValidationException ex)
        {
            Error = ex.Message;
            throw;
        }
    }

    [RelayCommand]
    private void ApplyPreset(TimeRangePreset preset)
    {
        Load(new TimeRangeSpec(TimeRangeKind.Relative, preset.Seconds));
    }

    private static DateTimeOffset ParseDate(string text, string label) =>
        DateTime.TryParseExact(text.Trim(), [DateFormat, "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy"], Tr, DateTimeStyles.AssumeLocal, out var value)
            ? new DateTimeOffset(value)
            : throw new ValidationException($"{label} zamanı geçersiz. Biçim: {DateFormat} (örn. 11.11.2026 09:30).");
}
