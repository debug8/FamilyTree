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

    // --- Дані для великої картки-тултіпа (кожен рядок null → рядок ховається) ---

    /// <summary>Абсолютний шлях до фото (поки не реалізовано — місце під фото).</summary>
    public string? PhotoPath { get; init; }

    public string? DetailMaiden { get; init; }

    public string? DetailGender { get; init; }

    public string? DetailBirth { get; init; }

    public string? DetailDeath { get; init; }

    public string? DetailMarriage { get; init; }

    public string? DetailChildren { get; init; }

    public string? DetailNotes { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}
