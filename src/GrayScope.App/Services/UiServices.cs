using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GrayScope.Core.Abstractions;

namespace GrayScope.App.Services;

/// <summary>Gives services access to the main window without view models referencing views.</summary>
public sealed class WindowHost
{
    public Window? MainWindow { get; set; }

    public Window Require() => MainWindow ?? throw new InvalidOperationException("Main window is not ready.");

    /// <summary>The window currently in front (a dialog when one is open) so child dialogs get the right owner.</summary>
    public Window Active()
    {
        var main = Require();
        var owned = main.OwnedWindows;
        return owned.LastOrDefault(w => w.IsActive) ?? (owned.Count > 0 ? owned[^1] : main);
    }
}

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed partial class ToastItem(string message, ToastKind kind) : ObservableObject
{
    public string Message { get; } = message;
    public ToastKind Kind { get; } = kind;
    public bool IsError => Kind == ToastKind.Error;
    public bool IsWarning => Kind == ToastKind.Warning;
    public bool IsSuccess => Kind == ToastKind.Success;
}

/// <summary>Bottom-right, auto-dismissing notifications (4 s; errors 7 s).</summary>
public sealed class ToastService
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(7);
    private const int MaxVisible = 4;

    public ObservableCollection<ToastItem> Items { get; } = [];

    public void Show(string message, ToastKind kind = ToastKind.Info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var item = new ToastItem(message, kind);
            Items.Add(item);
            while (Items.Count > MaxVisible)
            {
                Items.RemoveAt(0);
            }
            DispatcherTimer.RunOnce(() => Items.Remove(item), kind == ToastKind.Error ? ErrorDuration : DefaultDuration);
        });
    }

    public void Success(string message) => Show(message, ToastKind.Success);
    public void Warning(string message) => Show(message, ToastKind.Warning);
    public void Error(string message) => Show(message, ToastKind.Error);
    public void Dismiss(ToastItem item) => Items.Remove(item);
}

/// <summary>Light/dark switching, persisted in the settings store.</summary>
public sealed class ThemeService(ISettingsStore settings)
{
    private const string SettingKey = "ui.theme";

    public static bool IsDark => Application.Current?.ActualThemeVariant != ThemeVariant.Light;

    public async Task LoadAsync()
    {
        var stored = await settings.GetAsync(SettingKey);
        Apply(stored == "light" ? ThemeVariant.Light : ThemeVariant.Dark);
    }

    public async Task ToggleAsync()
    {
        var next = IsDark ? ThemeVariant.Light : ThemeVariant.Dark;
        Apply(next);
        await settings.SetAsync(SettingKey, next == ThemeVariant.Light ? "light" : "dark");
    }

    private static void Apply(ThemeVariant variant)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = variant;
        }
    }
}

public sealed class ClipboardService(WindowHost host)
{
    public async Task CopyAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(host.Active())?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}

public sealed record FileTypeFilter(string Name, string Pattern);

public sealed class FilePickerService(WindowHost host)
{
    public async Task<string?> SaveAsync(string title, string suggestedName, FileTypeFilter filter)
    {
        var storage = TopLevel.GetTopLevel(host.Active())?.StorageProvider;
        if (storage is null)
        {
            return null;
        }
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = Path.GetExtension(filter.Pattern).TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(filter.Name) { Patterns = [filter.Pattern] }],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> OpenAsync(string title, params FileTypeFilter[] filters)
    {
        var storage = TopLevel.GetTopLevel(host.Active())?.StorageProvider;
        if (storage is null)
        {
            return null;
        }
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [.. filters.Select(f => new FilePickerFileType(f.Name) { Patterns = [f.Pattern] })],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
