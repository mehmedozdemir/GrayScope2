using GrayScope.App.Services;
using GrayScope.Core.Abstractions;
using GrayScope.Core.Services;
using GrayScope.Infrastructure;

namespace GrayScope.App.ViewModels;

/// <summary>Bundle of application services handed to view models (keeps constructors short).</summary>
public sealed record AppServices(
    ProfileService Profiles,
    QueryService Queries,
    QueryExecutionService Execution,
    CatalogService Catalog,
    MessageDetailService MessageDetail,
    BackupService Backup,
    ISettingsStore Settings,
    ToastService Toasts,
    ThemeService Theme,
    ClipboardService Clipboard,
    FilePickerService Files,
    DialogService Dialogs,
    AppPaths Paths);
