using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using GrayScope.App.Controls;
using GrayScope.App.ViewModels;
using GrayScope.App.Views;
using GrayScope.Core.Formatting;

namespace GrayScope.App.Services;

/// <summary>Opens dialogs. View models call this; they never construct windows themselves.</summary>
public sealed class DialogService(WindowHost host, ClipboardService clipboard, ToastService toasts)
{
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = true)
    {
        var confirm = new Button { Content = confirmText, IsDefault = true };
        confirm.Classes.Add(isDestructive ? "danger" : "primary");
        var window = BuildWindow(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 400, LineHeight = 19 }, confirm, out var cancel);
        confirm.Click += (_, _) => window.Close(true);
        cancel.Click += (_, _) => window.Close(false);
        return await window.ShowDialog<bool>(host.Active());
    }

    public async Task<string?> PromptAsync(string title, string label, string initial)
    {
        var input = new TextBox { Text = initial, Width = 360 };
        var body = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = label, Classes = { "label" } }, input } };
        var ok = new Button { Content = "Tamam", IsDefault = true, Classes = { "primary" } };
        var window = BuildWindow(title, body, ok, out var cancel);
        ok.Click += (_, _) => window.Close(input.Text?.Trim());
        cancel.Click += (_, _) => window.Close(null);
        window.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        return await window.ShowDialog<string?>(host.Active());
    }

    public async Task<T?> ChooseAsync<T>(string title, string label, IReadOnlyList<T> options, T? selected)
        where T : class
    {
        var combo = new ComboBox { ItemsSource = options, SelectedItem = selected ?? (options.Count > 0 ? options[0] : null), Width = 360 };
        var body = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = label, Classes = { "label" } }, combo } };
        var ok = new Button { Content = "Tamam", IsDefault = true, Classes = { "primary" } };
        var window = BuildWindow(title, body, ok, out var cancel);
        ok.Click += (_, _) => window.Close(combo.SelectedItem as T);
        cancel.Click += (_, _) => window.Close(null);
        return await window.ShowDialog<T?>(host.Active());
    }

    public async Task<bool> ShowQueryEditorAsync(QueryEditorViewModel editor)
    {
        var window = new QueryEditorWindow { DataContext = editor };
        return await window.ShowDialog<bool>(host.Active());
    }

    public Task ShowSettingsAsync(SettingsViewModel settings) =>
        new SettingsWindow { DataContext = settings }.ShowDialog(host.Active());

    /// <summary>Non-modal viewer for a long cell value with JSON/XML pretty printing.</summary>
    public void ShowValue(string fieldName, string value)
    {
        var editor = new TextEditor
        {
            IsReadOnly = true,
            WordWrap = true,
            ShowLineNumbers = true,
            FontFamily = (FontFamily)Application.Current!.FindResource("MonoFont")!,
            FontSize = 12.5,
            Background = Brushes.Transparent,
        };
        var formats = new ComboBox
        {
            ItemsSource = new[] { "Otomatik", "JSON", "XML", "Düz metin" },
            SelectedIndex = 0,
            Width = 140,
        };
        var detected = new TextBlock { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };

        void Apply()
        {
            var requested = formats.SelectedIndex switch { 1 => ValueFormat.Json, 2 => ValueFormat.Xml, 3 => ValueFormat.Plain, _ => ValueFormat.Auto };
            var (applied, text) = ValueFormatter.Format(value, requested);
            editor.Text = text;
            detected.Text = requested != ValueFormat.Plain && applied == ValueFormat.Plain && requested != ValueFormat.Auto
                ? "Değer bu biçimde ayrıştırılamadı."
                : $"Biçim: {applied switch { ValueFormat.Json => "JSON", ValueFormat.Xml => "XML", _ => "düz metin" }} · {value.Length:N0} karakter";
        }

        formats.SelectionChanged += (_, _) => Apply();
        var copy = new Button { Content = "Kopyala" };
        copy.Click += async (_, _) =>
        {
            await clipboard.CopyAsync(editor.Text);
            toasts.Success("Panoya kopyalandı.");
        };

        var toolbar = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 8) };
        toolbar.Children.Add(formats);
        detected.Margin = new Thickness(12, 0, 0, 0);
        toolbar.Children.Add(detected);
        DockPanel.SetDock(copy, Dock.Right);
        toolbar.Children.Add(copy);

        var body = new DockPanel { Margin = new Thickness(16, 12, 16, 16) };
        DockPanel.SetDock(toolbar, Dock.Top);
        body.Children.Add(toolbar);
        body.Children.Add(new Border { Classes = { "editor" }, Padding = new Thickness(4), Child = editor });
        var root = WithTitleBar(body);

        Apply();
        var window = new Window
        {
            Title = string.IsNullOrEmpty(fieldName) ? "Değer" : fieldName,
            Width = 760,
            Height = 560,
            MinWidth = 420,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root,
        };
        WindowTitleBar.Extend(window);
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };
        window.Show(host.Require());
    }

    private static Window BuildWindow(string title, Control body, Button primary, out Button cancel)
    {
        cancel = new Button { Content = "Vazgeç", IsCancel = true };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, primary },
        };
        var layout = new StackPanel
        {
            Margin = new Thickness(20, 16, 20, 18),
            Spacing = 18,
            MinWidth = 360,
            Children = { body, buttons },
        };
        var window = new Window
        {
            Title = title,
            Content = WithTitleBar(layout),
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        WindowTitleBar.Extend(window);
        return window;
    }

    /// <summary>The platform decorations draw the window title; the bar only provides the surface.</summary>
    private static DockPanel WithTitleBar(Control body)
    {
        var bar = new WindowTitleBar();
        DockPanel.SetDock(bar, Dock.Top);
        return new DockPanel { Children = { bar, body } };
    }
}
