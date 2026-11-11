using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GrayScope.App.ViewModels;

namespace GrayScope.App.Views;

public partial class QueryEditorWindow : Window
{
    public QueryEditorWindow()
    {
        InitializeComponent();
        SaveButton.Click += async (_, _) => await SaveAsync();
        CancelButton.Click += (_, _) => Close(false);
        FieldInput.AddHandler(KeyDownEvent, OnFieldInputKeyDown, RoutingStrategies.Tunnel);
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                await SaveAsync();
            }
        };
        Opened += (_, _) => NameBox.Focus();
    }

    private QueryEditorViewModel? ViewModel => DataContext as QueryEditorViewModel;

    private async Task SaveAsync()
    {
        if (ViewModel is { } vm && await vm.SaveAsync())
        {
            Close(true);
        }
    }

    /// <summary>Enter adds the typed field as a chip instead of submitting the form.</summary>
    private void OnFieldInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel is { } vm && !string.IsNullOrWhiteSpace(vm.NewField))
        {
            vm.AddFieldCommand.Execute(null);
            e.Handled = true;
        }
    }
}
