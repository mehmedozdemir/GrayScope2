using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GrayScope.App.Services;
using GrayScope.App.ViewModels;
using GrayScope.App.Views;
using GrayScope.Core.Abstractions;
using GrayScope.Core.Services;
using GrayScope.Infrastructure;
using GrayScope.Infrastructure.Graylog;
using GrayScope.Infrastructure.Persistence;
using GrayScope.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace GrayScope.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _services = ConfigureServices();
        _services.GetRequiredService<Database>().Migrate();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var host = _services.GetRequiredService<WindowHost>();
            var main = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
            host.MainWindow = main;
            desktop.MainWindow = main;
            desktop.ShutdownRequested += (_, _) => _services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Composition root: the only place that knows concrete infrastructure types.</summary>
    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        var paths = new AppPaths();

        services.AddSingleton(paths);
        services.AddSingleton(new Database(paths.DatabasePath));
        services.AddSingleton<ISecretProtector>(new LocalSecretProtector(paths.KeyFilePath));
        services.AddSingleton<ILegacyTokenDecryptor>(FernetTokenDecryptor.FromDefaultKeyFile(AppPaths.LegacyKeyFilePath));
        services.AddSingleton<IGraylogClientFactory>(sp => new GraylogClientFactory(sp.GetRequiredService<ISecretProtector>()));

        services.AddSingleton<IProfileRepository, ProfileRepository>();
        services.AddSingleton<IFolderRepository, FolderRepository>();
        services.AddSingleton<IQueryRepository, QueryRepository>();
        services.AddSingleton<IRunHistoryRepository, RunHistoryRepository>();
        services.AddSingleton<ISettingsStore, SettingsStore>();

        services.AddSingleton<CatalogService>();
        services.AddSingleton<ProfileService>();
        services.AddSingleton<QueryService>();
        services.AddSingleton<QueryExecutionService>();
        services.AddSingleton<MessageDetailService>();
        services.AddSingleton<BackupService>();

        services.AddSingleton<WindowHost>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<FilePickerService>();
        services.AddSingleton<DialogService>();

        services.AddSingleton<AppServices>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider();
    }
}
