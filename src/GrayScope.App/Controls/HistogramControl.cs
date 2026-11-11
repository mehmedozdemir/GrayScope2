using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GrayScope.Core.Services;

namespace GrayScope.App.Controls;

/// <summary>
/// Message-count-over-time bar chart. Hover shows the bucket; click invokes <see cref="BucketCommand"/>
/// (used to filter the grid to that time slice).
/// </summary>
public sealed class HistogramControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<HistogramBucket>?> BucketsProperty =
        AvaloniaProperty.Register<HistogramControl, IReadOnlyList<HistogramBucket>?>(nameof(Buckets));

    public static readonly StyledProperty<ICommand?> BucketCommandProperty =
        AvaloniaProperty.Register<HistogramControl, ICommand?>(nameof(BucketCommand));

    private const double AxisHeight = 18;
    private const double BarGap = 3;
    private const double LabelFontSize = 10.5;
    private const double BarOpacity = 0.85;
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private int _hoverIndex = -1;

    static HistogramControl()
    {
        AffectsRender<HistogramControl>(BucketsProperty);
    }

    public HistogramControl()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public IReadOnlyList<HistogramBucket>? Buckets
    {
        get => GetValue(BucketsProperty);
        set => SetValue(BucketsProperty, value);
    }

    public ICommand? BucketCommand
    {
        get => GetValue(BucketCommandProperty);
        set => SetValue(BucketCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BucketsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= OnBucketsChanged;
            }
            if (change.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += OnBucketsChanged;
            }
        }
    }

    private void OnBucketsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        var buckets = Buckets;
        var bounds = Bounds;
        var text = Brush("TextSecondaryBrush") ?? Brushes.Gray;
        if (buckets is null || buckets.Count == 0)
        {
            DrawText(context, "Zaman alanı (timestamp) içeren sonuç yok.", new Point(8, (bounds.Height / 2) - 8), text);
            return;
        }

        var chartHeight = Math.Max(0, bounds.Height - AxisHeight);
        var max = Math.Max(1, buckets.Max(b => b.Count));
        var barWidth = bounds.Width / buckets.Count;
        var accent = Brush("AccentBrush") ?? Brushes.SteelBlue;
        var hover = Brush("AccentHoverBrush") ?? Brushes.CornflowerBlue;
        var grid = new Pen(Brush("BorderBrush") ?? Brushes.Gray, 1);

        context.DrawLine(grid, new Point(0, chartHeight), new Point(bounds.Width, chartHeight));
        for (var i = 0; i < buckets.Count; i++)
        {
            var height = chartHeight * buckets[i].Count / max;
            if (height <= 0)
            {
                continue;
            }
            var rect = new Rect((i * barWidth) + (BarGap / 2), chartHeight - height, Math.Max(1, barWidth - BarGap), height);
            using (context.PushOpacity(i == _hoverIndex ? 1 : BarOpacity))
            {
                context.FillRectangle(i == _hoverIndex ? hover : accent, rect, 2);
            }
        }

        DrawText(context, Label(buckets[0].Start), new Point(0, chartHeight + 2), text);
        var end = Label(buckets[^1].End);
        var endText = Format(end, text);
        context.DrawText(endText, new Point(Math.Max(0, bounds.Width - endText.Width), chartHeight + 2));
        DrawText(context, $"en yüksek: {max:N0}", new Point(4, 0), text);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var index = IndexAt(e.GetPosition(this));
        if (index == _hoverIndex)
        {
            return;
        }
        _hoverIndex = index;
        if (index >= 0 && Buckets is { } buckets)
        {
            var b = buckets[index];
            ToolTip.SetTip(this, $"{b.Start.ToLocalTime().ToString("dd.MM HH:mm:ss", Tr)} – {b.End.ToLocalTime().ToString("HH:mm:ss", Tr)}\n{b.Count:N0} kayıt · tıklayınca filtrele");
            ToolTip.SetIsOpen(this, true);
        }
        else
        {
            ToolTip.SetIsOpen(this, false);
        }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverIndex = -1;
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var index = IndexAt(e.GetPosition(this));
        if (index >= 0 && Buckets is { } buckets && buckets[index].Count > 0 && BucketCommand?.CanExecute(buckets[index]) == true)
        {
            BucketCommand.Execute(buckets[index]);
        }
    }

    private int IndexAt(Point point)
    {
        var buckets = Buckets;
        if (buckets is null || buckets.Count == 0 || Bounds.Width <= 0)
        {
            return -1;
        }
        var index = (int)(point.X / (Bounds.Width / buckets.Count));
        return index >= 0 && index < buckets.Count ? index : -1;
    }

    private static string Label(DateTimeOffset value) => value.ToLocalTime().ToString("dd.MM HH:mm:ss", Tr);

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : null;

    private static FormattedText Format(string value, IBrush brush) =>
        new(value, Tr, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), LabelFontSize, brush);

    private static void DrawText(DrawingContext context, string value, Point origin, IBrush brush) =>
        context.DrawText(Format(value, brush), origin);
}
