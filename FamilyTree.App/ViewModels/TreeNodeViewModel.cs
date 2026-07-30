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

    /// <summary>Перший рядок картки — «Прізвище Ім'я».</summary>
    public string NamePrimary { get; init; } = string.Empty;

    /// <summary>По батькові — окремий рядок картки (null → рядок ховається).</summary>
    public string? Patronymic { get; init; }

    public string Years { get; init; } = string.Empty;

    /// <summary>Родинний зв'язок відносно кореня (бейдж) — наповнюється в T-4.3.</summary>
    public string? RelationBadge { get; init; }

    public bool IsRoot { get; init; }

    /// <summary>
    /// Дані великої картки-тултіпа. Той самий тип, що й у рядках родичів на
    /// вкладці «Особа», тож обидва місця рендерять один шаблон PersonCardTemplate.
    /// </summary>
    public PersonCard? Card { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Підсвічений вузол (наведення на суміжне ребро).</summary>
    [ObservableProperty]
    private bool _isHighlighted;
}
