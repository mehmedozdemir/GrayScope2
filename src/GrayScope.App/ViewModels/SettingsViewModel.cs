using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrayScope.App.Services;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;
using GrayScope.Core.Services;
using GrayScope.Infrastructure;
using GrayScope.Infrastructure.Legacy;

namespace GrayScope.App.ViewModels;

/// <summary>"Ayarlar ve Tanımlamalar": Graylog profiles + general/data operations.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _app;

    public SettingsViewModel(AppServices app)
    {
        _app = app;
        _ = LoadAsync();
    }

    /// <summary>Raised after anything that changes profiles or queries, so the main window can refresh.</summary>
    public event EventHandler? DataChanged;

    public ObservableCollection<GraylogProfile> Profiles { get; } = [];
    public string DataDirectory => _app.Paths.DataDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    private ProfileEditorViewModel? _editor;

    [ObservableProperty]
    private GraylogProfile? _selectedProfile;

    [ObservableProperty]
    private bool _includeTokensInBackup;

    [ObservableProperty]
    private bool _isDark = ThemeService.IsDark;

    [ObservableProperty]
    private string? _dataStatus;

    public bool HasEditor => Editor is not null;
    public bool HasNoProfiles => Profiles.Count == 0;

    private async Task LoadAsync(long? selectId = null)
    {
        Profiles.Clear();
        foreach (var profile in await _app.Profiles.GetAllAsync())
        {
            Profiles.Add(profile);
        }
        OnPropertyChanged(nameof(HasNoProfiles));
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectId) ?? Profiles.FirstOrDefault();
        if (SelectedProfile is null)
        {
            Editor = null;
        }
    }

    partial void OnSelectedProfileChanged(GraylogProfile? value)
    {
        if (value is not null)
        {
            Editor = new ProfileEditorViewModel(_app, value, OnProfileSavedAsync, OnProfileDeletedAsync);
        }
    }

    [RelayCommand]
    private void NewProfile()
    {
        SelectedProfile = null;
        Editor = new ProfileEditorViewModel(_app, new GraylogProfile { IsActive = true, IsDefault = Profiles.Count == 0 }, OnProfileSavedAsync, OnProfileDeletedAsync);
    }

    private async Task OnProfileSavedAsync(GraylogProfile saved)
    {
        await LoadAsync(saved.Id);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task OnProfileDeletedAsync()
    {
        await LoadAsync();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ToggleTheme()
    {
        await _app.Theme.ToggleAsync();
        IsDark = ThemeService.IsDark;
    }

    [RelayCommand]
    private async Task Backup()
    {
        var path = await _app.Files.SaveAsync("Yedeği kaydet", $"grayscope_yedek_{DateTime.Now:yyyyMMdd}.json", new FileTypeFilter("JSON yedek", "*.json"));
        if (path is null)
        {
            return;
        }
        var json = await _app.Backup.ExportAsync(IncludeTokensInBackup);
        await File.WriteAllTextAsync(path, json);
        DataStatus = IncludeTokensInBackup
            ? "Yedek alındı. Token'lar yalnızca bu Windows kullanıcısıyla geri yüklenebilir."
            : "Yedek alındı (token'lar hariç).";
        _app.Toasts.Success("Yedek kaydedildi.");
    }

    [RelayCommand]
    private async Task Restore()
    {
        var path = await _app.Files.OpenAsync("Yedek dosyası seçin", new FileTypeFilter("JSON yedek", "*.json"));
        if (path is null)
        {
            return;
        }
        await RunImportAsync(async () => await _app.Backup.ImportAsync(await File.ReadAllTextAsync(path)));
    }

    [RelayCommand]
    private async Task ImportLegacy()
    {
        var path = await _app.Files.OpenAsync("GrayScope 1 veritabanını seçin (grayscope.db)", new FileTypeFilter("SQLite veritabanı", "*.db"));
        if (path is null)
        {
            return;
        }
        await RunImportAsync(async () => await _app.Backup.ImportAsync(await LegacyDatabaseReader.ReadAsync(path)));
    }

    private async Task RunImportAsync(Func<Task<ImportSummary>> import)
    {
        try
        {
            var summary = await import();
            DataStatus = $"{summary.ProfilesAdded} profil, {summary.FoldersAdded} klasör, {summary.QueriesAdded} sorgu eklendi; {summary.QueriesSkipped} sorgu atlandı."
                + (summary.Warnings.Count > 0 ? "\n• " + string.Join("\n• ", summary.Warnings) : "");
            _app.Toasts.Success("İçe aktarma tamamlandı.");
            await LoadAsync();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (ValidationException ex)
        {
            DataStatus = ex.Message;
            _app.Toasts.Error(ex.Message);
        }
        catch (IOException ex)
        {
            DataStatus = $"Dosya okunamadı: {ex.Message}";
        }
    }

    public static string LegacyKeyHint => File.Exists(AppPaths.LegacyKeyFilePath)
        ? "GrayScope 1 anahtar dosyası bulundu: token'lar otomatik taşınacak."
        : "GrayScope 1 anahtar dosyası (~/.grayscope/secret.key) bulunamadı: profiller token'sız aktarılır, token'ları yeniden girmeniz gerekir.";
}

public sealed partial class ProfileEditorViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _app;
    private readonly GraylogProfile _profile;
    private readonly Func<GraylogProfile, Task> _onSaved;
    private readonly Func<Task> _onDeleted;
    private CancellationTokenSource? _testCts;

    public ProfileEditorViewModel(AppServices app, GraylogProfile profile, Func<GraylogProfile, Task> onSaved, Func<Task> onDeleted)
    {
        _app = app;
        _profile = profile;
        _onSaved = onSaved;
        _onDeleted = onDeleted;
        _name = profile.Name;
        _baseUrl = profile.BaseUrl;
        _isDefault = profile.IsDefault;
        _isActive = profile.IsActive;
    }

    public bool IsNew => _profile.Id == 0;
    public string Heading => IsNew ? "Yeni Graylog Profili" : _profile.Name;

    public string TokenWatermark => IsNew
        ? "Graylog API token"
        : _profile.NeedsToken ? "Token tanımlı değil — girin" : "Kayıtlı token korunuyor (değiştirmek için yazın)";

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _baseUrl;
    [ObservableProperty] private string _token = "";
    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _revealToken;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private bool _testSucceeded;
    [ObservableProperty] private string? _error;

    public char TokenMask => RevealToken ? '\0' : '•';
    partial void OnRevealTokenChanged(bool value) => OnPropertyChanged(nameof(TokenMask));

    public void Dispose()
    {
        _testCts?.Cancel();
        _testCts?.Dispose();
    }

    [RelayCommand]
    private async Task Test()
    {
        if (IsTesting)
        {
            _testCts?.Cancel();
            return;
        }
        _testCts = new CancellationTokenSource();
        IsTesting = true;
        TestResult = null;
        try
        {
            var info = await _app.Profiles.TestConnectionAsync(BaseUrl, Token, IsNew ? null : _profile, _testCts.Token);
            TestSucceeded = true;
            TestResult = $"Bağlantı başarılı · Graylog {info.Version ?? "?"}{(info.Hostname is null ? "" : $" · {info.Hostname}")}";
        }
        catch (GrayScopeException ex)
        {
            TestSucceeded = false;
            TestResult = ex.Message;
        }
        catch (OperationCanceledException)
        {
            TestResult = null;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        Error = null;
        try
        {
            _profile.Name = Name;
            _profile.BaseUrl = BaseUrl;
            _profile.IsDefault = IsDefault;
            _profile.IsActive = IsActive;
            var saved = await _app.Profiles.SaveAsync(_profile, Token);
            Token = "";
            _app.Toasts.Success($"'{saved.Name}' kaydedildi.");
            await _onSaved(saved);
        }
        catch (ValidationException ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (IsNew)
        {
            await _onDeleted();
            return;
        }
        var confirmed = await _app.Dialogs.ConfirmAsync("Profili sil", $"'{_profile.Name}' profili silinsin mi? Bu işlem geri alınamaz.", "Sil");
        if (!confirmed)
        {
            return;
        }
        try
        {
            await _app.Profiles.DeleteAsync(_profile);
            _app.Toasts.Success("Profil silindi.");
            await _onDeleted();
        }
        catch (ValidationException ex)
        {
            Error = ex.Message;
            _app.Toasts.Warning(ex.Message);
        }
    }
}
