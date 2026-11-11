using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrayScope.App.Services;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;
using GrayScope.Core.Services;

namespace GrayScope.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const string OpenTabsSetting = "ui.openTabs";

    private readonly AppServices _app;
    private IReadOnlyList<SavedQuery> _queries = [];
    private IReadOnlyList<QueryFolder> _folders = [];
    private IReadOnlyList<GraylogProfile> _profiles = [];
    private IReadOnlyDictionary<long, QueryRun> _latestRuns = new Dictionary<long, QueryRun>();
    private bool _restoringTabs;

    public MainViewModel(AppServices app)
    {
        _app = app;
        Tabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTabs));
            if (!_restoringTabs)
            {
                _ = PersistOpenTabsAsync();
            }
        };
    }

    public ToastService Toasts => _app.Toasts;
    public ObservableCollection<TreeNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<QueryTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private QueryTabViewModel? _selectedTab;

    [ObservableProperty]
    private TreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOnboarding))]
    private bool _hasProfiles = true;

    [ObservableProperty]
    private bool _isTreeEmpty;

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    public bool HasTabs => Tabs.Count > 0;
    public bool ShowOnboarding => !HasProfiles;

    // ── Load ────────────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await _app.Theme.LoadAsync();
        await ReloadAsync();
        await RestoreOpenTabsAsync();
    }

    public async Task ReloadAsync()
    {
        _profiles = await _app.Profiles.GetAllAsync();
        _queries = await _app.Queries.GetAllAsync();
        _folders = await _app.Queries.GetFoldersAsync();
        _latestRuns = await _app.Queries.GetLatestRunsAsync();
        HasProfiles = _profiles.Count > 0;
        RenderTree();
        foreach (var tab in Tabs)
        {
            tab.SetProfiles(_profiles);
        }
    }

    partial void OnSearchTextChanged(string value) => RenderTree();

    private void RenderTree()
    {
        Nodes.Clear();
        var term = SearchText.Trim();
        // Badge only queries bound to a non-default server; the default one would be noise on every row.
        var profileNames = _profiles.Where(p => !p.IsDefault).ToDictionary(p => p.Id, p => p.Name);
        var needsToken = _profiles.Where(p => p.NeedsToken).Select(p => p.Id).ToHashSet();
        var matching = _queries
            .Where(q => term.Length == 0
                || q.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || q.Template.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        TreeNodeViewModel QueryNode(SavedQuery q) => new()
        {
            Kind = TreeNodeKind.Query,
            Id = q.Id,
            Name = q.Name,
            ParentFolderId = q.FolderId,
            Badge = profileNames.GetValueOrDefault(q.ProfileId),
            NeedsAttention = needsToken.Contains(q.ProfileId),
            Tooltip = BuildTooltip(q),
        };

        void AddChildren(long? parentId, ObservableCollection<TreeNodeViewModel> target)
        {
            foreach (var folder in _folders.Where(f => f.ParentId == parentId).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var node = new TreeNodeViewModel { Kind = TreeNodeKind.Folder, Id = folder.Id, Name = folder.Name, ParentFolderId = folder.ParentId };
                AddChildren(folder.Id, node.Children);
                // While searching hide folders without hits.
                if (term.Length == 0 || node.Children.Count > 0)
                {
                    target.Add(node);
                }
            }
            foreach (var query in matching.Where(q => q.FolderId == parentId).OrderBy(q => q.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                target.Add(QueryNode(query));
            }
        }

        AddChildren(null, Nodes);
        IsTreeEmpty = Nodes.Count == 0;
    }

    private string BuildTooltip(SavedQuery q)
    {
        var lines = new List<string> { q.Template };
        if (!string.IsNullOrWhiteSpace(q.Description))
        {
            lines.Insert(0, q.Description);
        }
        if (_latestRuns.TryGetValue(q.Id, out var run))
        {
            lines.Add($"Son çalıştırma: {run.RunAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("tr-TR"))} · {run.ResultCount:N0} kayıt");
        }
        return string.Join('\n', lines);
    }

    // ── Tabs ────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenNode(TreeNodeViewModel? node)
    {
        if (node is { IsQuery: true })
        {
            await OpenQueryAsync(node.Id);
        }
    }

    public async Task OpenQueryAsync(long queryId)
    {
        var existing = Tabs.FirstOrDefault(t => t.QueryId == queryId);
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }
        var query = await _app.Queries.GetAsync(queryId);
        if (query is null)
        {
            return;
        }
        var tab = new QueryTabViewModel(_app, query, _profiles);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void CloseTab(QueryTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null)
        {
            return;
        }
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();
        if (Tabs.Count > 0)
        {
            SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }
    }

    [RelayCommand]
    private Task RunSelectedTab() => SelectedTab?.RunIfIdleAsync() ?? Task.CompletedTask;

    private async Task PersistOpenTabsAsync() =>
        await _app.Settings.SetAsync(OpenTabsSetting, string.Join(',', Tabs.Select(t => t.QueryId)));

    private async Task RestoreOpenTabsAsync()
    {
        var stored = await _app.Settings.GetAsync(OpenTabsSetting);
        if (string.IsNullOrEmpty(stored))
        {
            return;
        }
        _restoringTabs = true;
        try
        {
            foreach (var id in stored.Split(',').Select(s => long.TryParse(s, out var v) ? v : 0).Where(v => v > 0))
            {
                await OpenQueryAsync(id);
            }
        }
        finally
        {
            _restoringTabs = false;
        }
    }

    // ── Query CRUD ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task NewQuery(TreeNodeViewModel? context)
    {
        if (!HasProfiles)
        {
            await OpenSettings();
            return;
        }
        var folderId = context switch
        {
            { IsFolder: true } f => f.Id,
            { IsQuery: true } q => q.ParentFolderId,
            _ => null,
        };
        var defaultProfile = _profiles.FirstOrDefault(p => p.IsDefault && p.IsActive) ?? _profiles.FirstOrDefault(p => p.IsActive) ?? _profiles[0];
        var query = new SavedQuery { ProfileId = defaultProfile.Id, FolderId = folderId, Template = "" };
        var saved = await ShowEditorAsync(query);
        if (saved is not null)
        {
            await ReloadAsync();
            await OpenQueryAsync(saved.Id);
        }
    }

    [RelayCommand]
    private async Task EditQuery(TreeNodeViewModel? node)
    {
        var id = node is { IsQuery: true } ? node.Id : SelectedTab?.QueryId;
        if (id is null || await _app.Queries.GetAsync(id.Value) is not { } query)
        {
            return;
        }
        var saved = await ShowEditorAsync(query);
        if (saved is not null)
        {
            await ReloadAsync();
            Tabs.FirstOrDefault(t => t.QueryId == saved.Id)?.Reload(saved, _profiles);
        }
    }

    private async Task<SavedQuery?> ShowEditorAsync(SavedQuery query)
    {
        var editor = new QueryEditorViewModel(_app, query, _profiles, FolderOptions());
        return await _app.Dialogs.ShowQueryEditorAsync(editor) ? editor.Saved : null;
    }

    [RelayCommand]
    private async Task DuplicateQuery(TreeNodeViewModel? node)
    {
        var id = node is { IsQuery: true } ? node.Id : SelectedTab?.QueryId;
        if (id is null)
        {
            return;
        }
        var copy = await _app.Queries.DuplicateAsync(id.Value);
        await ReloadAsync();
        _app.Toasts.Success($"'{copy.Name}' oluşturuldu.");
        await OpenQueryAsync(copy.Id);
    }

    [RelayCommand]
    private async Task DeleteQuery(TreeNodeViewModel? node)
    {
        var id = node is { IsQuery: true } ? node.Id : SelectedTab?.QueryId;
        var query = _queries.FirstOrDefault(q => q.Id == id);
        if (query is null || !await _app.Dialogs.ConfirmAsync("Sorguyu sil", $"'{query.Name}' sorgusu ve çalıştırma geçmişi silinsin mi?", "Sil"))
        {
            return;
        }
        await _app.Queries.DeleteAsync(query.Id);
        var tab = Tabs.FirstOrDefault(t => t.QueryId == query.Id);
        if (tab is not null)
        {
            CloseTab(tab);
        }
        await ReloadAsync();
        _app.Toasts.Success("Sorgu silindi.");
    }

    [RelayCommand]
    private async Task MoveQuery(TreeNodeViewModel? node)
    {
        if (node is not { IsQuery: true })
        {
            return;
        }
        var options = FolderOptions();
        var target = await _app.Dialogs.ChooseAsync("Sorguyu taşı", "Hedef klasör", options, options.FirstOrDefault(o => o.Id == node.ParentFolderId));
        if (target is null)
        {
            return;
        }
        await _app.Queries.MoveAsync(node.Id, target.Id, 0);
        await ReloadAsync();
    }

    // ── Folders ─────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task NewFolder(TreeNodeViewModel? parent)
    {
        var name = await _app.Dialogs.PromptAsync(parent is { IsFolder: true } ? $"'{parent.Name}' altında yeni klasör" : "Yeni klasör", "Klasör adı", "");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        await RunGuardedAsync(async () =>
        {
            await _app.Queries.SaveFolderAsync(new QueryFolder { Name = name, ParentId = parent is { IsFolder: true } ? parent.Id : null });
            await ReloadAsync();
        });
    }

    [RelayCommand]
    private async Task RenameFolder(TreeNodeViewModel? node)
    {
        var folder = _folders.FirstOrDefault(f => f.Id == node?.Id && node.IsFolder);
        if (folder is null)
        {
            return;
        }
        var name = await _app.Dialogs.PromptAsync("Klasörü yeniden adlandır", "Klasör adı", folder.Name);
        if (string.IsNullOrWhiteSpace(name) || name == folder.Name)
        {
            return;
        }
        await RunGuardedAsync(async () =>
        {
            await _app.Queries.SaveFolderAsync(new QueryFolder { Id = folder.Id, Name = name, ParentId = folder.ParentId, Position = folder.Position });
            await ReloadAsync();
        });
    }

    [RelayCommand]
    private async Task DeleteFolder(TreeNodeViewModel? node)
    {
        if (node is not { IsFolder: true })
        {
            return;
        }
        var confirmed = await _app.Dialogs.ConfirmAsync("Klasörü sil",
            $"'{node.Name}' klasörü ve alt klasörleri silinsin mi? İçindeki sorgular silinmez, en üst seviyeye taşınır.", "Sil");
        if (!confirmed)
        {
            return;
        }
        await _app.Queries.DeleteFolderAsync(node.Id);
        await ReloadAsync();
    }

    private IReadOnlyList<FolderOption> FolderOptions()
    {
        var paths = BackupService.FolderPaths(_folders);
        return [new FolderOption(null, "(Kök — klasörsüz)"), .. paths.OrderBy(p => p.Value, StringComparer.CurrentCultureIgnoreCase).Select(p => new FolderOption(p.Key, p.Value))];
    }

    // ── Shell actions ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenSettings()
    {
        var settings = new SettingsViewModel(_app);
        var changed = false;
        settings.DataChanged += (_, _) => changed = true;
        await _app.Dialogs.ShowSettingsAsync(settings);
        if (changed)
        {
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleTheme()
    {
        await _app.Theme.ToggleAsync();
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand]
    private void DismissToast(ToastItem item) => _app.Toasts.Dismiss(item);

    private async Task RunGuardedAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ValidationException ex)
        {
            _app.Toasts.Warning(ex.Message);
        }
    }
}
