using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GrayScope.App.ViewModels;

namespace GrayScope.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        QueryTree.ContextRequested += OnTreeContextRequested;
        QueryTree.SelectionChanged += OnTreeSelectionChanged;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (ViewModel is { } vm)
        {
            await vm.InitializeAsync();
        }
    }

    /// <summary>Selecting a query opens it (or focuses its tab), like a file explorer.</summary>
    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (QueryTree.SelectedItem is TreeNodeViewModel { IsQuery: true } node)
        {
            ViewModel?.OpenNodeCommand.Execute(node);
        }
    }

    /// <summary>
    /// The context menu is built in code: menus live in a popup outside the tree's visual tree,
    /// so binding back to the window's view model from a template is unreliable.
    /// </summary>
    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }
        var node = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as TreeNodeViewModel;
        var menu = new ContextMenu();

        void Add(string header, System.Windows.Input.ICommand command, object? parameter = null)
        {
            var item = new MenuItem { Header = header, Command = command, CommandParameter = parameter };
            menu.Items.Add(item);
        }

        switch (node)
        {
            case { IsQuery: true }:
                Add("Aç", vm.OpenNodeCommand, node);
                Add("Düzenle…", vm.EditQueryCommand, node);
                Add("Kopyala", vm.DuplicateQueryCommand, node);
                Add("Taşı…", vm.MoveQueryCommand, node);
                menu.Items.Add(new Separator());
                Add("Sil", vm.DeleteQueryCommand, node);
                break;
            case { IsFolder: true }:
                Add("Bu klasörde yeni sorgu…", vm.NewQueryCommand, node);
                Add("Yeni alt klasör…", vm.NewFolderCommand, node);
                Add("Yeniden adlandır…", vm.RenameFolderCommand, node);
                menu.Items.Add(new Separator());
                Add("Sil", vm.DeleteFolderCommand, node);
                break;
            default:
                Add("Yeni sorgu…", vm.NewQueryCommand);
                Add("Yeni klasör…", vm.NewFolderCommand);
                break;
        }

        menu.Open(QueryTree);
        e.Handled = true;
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty && ViewModel is { } vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.IsSidebarOpen))
                {
                    ShellGrid.ColumnDefinitions[0].Width = vm.IsSidebarOpen ? new GridLength(264) : new GridLength(0);
                }
            };
        }
    }
}
