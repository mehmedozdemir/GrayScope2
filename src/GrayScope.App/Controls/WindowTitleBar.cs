using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GrayScope.App.Controls;

/// <summary>
/// In-app title bar for windows whose client area is extended into the decorations, so the
/// app's own surface replaces the OS-coloured caption. Drag moves the window; double-click
/// toggles maximise (resizable windows only). The OS caption buttons stay on the right, so
/// content must leave <see cref="CaptionButtonsWidth"/> free there.
/// </summary>
public sealed class WindowTitleBar : Border
{
    /// <summary>Space reserved for minimise / maximise / close.</summary>
    public const double CaptionButtonsWidth = 140;

    public WindowTitleBar()
    {
        Classes.Add("titlebar");
        PointerPressed += OnPointerPressed;
        DoubleTapped += OnDoubleTapped;
    }

    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>Applies the extended-client-area settings to <paramref name="window"/>.</summary>
    public static void Extend(Window window)
    {
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = 34;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only empty title-bar space drags; buttons and inputs placed in the bar keep working.
        if (e.Source is not (Button or TextBox) && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && TopLevel.GetTopLevel(this) is Window window)
        {
            window.BeginMoveDrag(e);
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window { CanResize: true } window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            Extend(window);
        }
    }
}
