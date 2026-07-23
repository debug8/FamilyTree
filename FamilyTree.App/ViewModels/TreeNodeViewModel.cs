using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.Domain.Layout;

namespace FamilyTree.App.ViewModels;

/// <summary>Вузол дерева для рендерингу на полотні.</summary>
public partial class TreeNodeViewModel : ObservableObject
{
    public TreeNodeViewModel(Guid personId)
    {
        PersonId = personId;
    }

    public Guid PersonId { get; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Width => TreeLayoutEngine.NodeWidth;

    public double Height => TreeLayoutEngine.NodeHeight;

    public string FullName { get; init; } = string.Empty;

    public string Years { get; init; } = string.Empty;

    /// <summary>Родинний зв'язок відносно кореня (бейдж) — наповнюється в T-4.3.</summary>
    public string? RelationBadge { get; init; }

    public bool IsRoot { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}
