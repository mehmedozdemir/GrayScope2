using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GrayScope.Core.Domain;
using GrayScope.Core.Querying;

namespace GrayScope.App.ViewModels;

/// <summary>One input in the parameter strip, generated from the template's <c>{name}</c> tokens.</summary>
public sealed partial class ParameterInputViewModel(QueryParameter parameter) : ObservableObject
{
    public string Name { get; } = parameter.Name;
    public bool IsOptional { get; } = parameter.IsOptional;
    public string DefaultValue { get; } = parameter.DefaultValue;

    [ObservableProperty]
    private string _value = parameter.DefaultValue;

    public string Label => IsOptional ? Name + " (ops.)" : Name;

    public string Watermark => IsOptional
        ? DefaultValue.Length > 0 ? $"boşsa {DefaultValue}" : "boşsa *"
        : "değer girin";
}

/// <summary>A selectable server in the run bar's target list.</summary>
public sealed partial class ProfileChoiceViewModel(GraylogProfile profile, bool isSelected) : ObservableObject
{
    public GraylogProfile Profile { get; } = profile;
    public string Name => Profile.Name;

    [ObservableProperty]
    private bool _isSelected = isSelected;
}

public enum TreeNodeKind
{
    Folder,
    Query,
}

public sealed partial class TreeNodeViewModel : ObservableObject
{
    public required TreeNodeKind Kind { get; init; }
    public required long Id { get; init; }
    public required string Name { get; init; }
    public long? ParentFolderId { get; init; }
    public string? Badge { get; init; }
    public string? Tooltip { get; init; }
    public bool NeedsAttention { get; init; }
    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public bool IsFolder => Kind == TreeNodeKind.Folder;
    public bool IsQuery => Kind == TreeNodeKind.Query;

    [ObservableProperty]
    private bool _isExpanded = true;
}

/// <summary>Selectable stream in the query form.</summary>
public sealed partial class StreamChoiceViewModel(string id, string title, bool isSelected) : ObservableObject
{
    public string Id { get; } = id;
    public string Title { get; } = title;

    [ObservableProperty]
    private bool _isSelected = isSelected;

    [ObservableProperty]
    private bool _isVisible = true;
}

public sealed record FolderOption(long? Id, string Path)
{
    public override string ToString() => Path;
}
