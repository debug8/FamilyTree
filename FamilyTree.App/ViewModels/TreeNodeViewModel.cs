using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.Domain.Layout;

namespace FamilyTree.App.ViewModels;

/// <summary>Вузол дерева для рендерингу на полотні.</summary>
public partial class TreeNodeViewModel : ObservableObject
{
    /// <summary>
    /// Ширина декодування мініатюри у вузлі: показується 40×50, запас — на 200% DPI.
    /// Навмисно мала: таких мініатюр на полотні стільки ж, скільки людей у дереві,
    /// і декодувати їх у розмірі картки-тултіпа означало б тримати в пам'яті
    /// в тридцять разів більше пікселів, ніж видно.
    /// </summary>
    internal const int NodePhotoWidth = 96;

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

    /// <summary>Дівоче прізвище окремим рядком (null → рядок ховається).</summary>
    public string? MaidenName { get; init; }

    /// <summary>
    /// Роки життя. Порожній рядок, а не null, історично: <c>NullToCollapsedConverter</c>
    /// ховає і те, і те, а тип лишається незмінним для наявних прив'язок.
    /// </summary>
    public string Years { get; init; } = string.Empty;

    /// <summary>Мініатюра фото ліворуч від тексту (null — фото немає або вимкнене).</summary>
    public ImageSource? Photo { get; init; }

    /// <summary>
    /// Чи виділяти місце під мініатюру. Як і в <see cref="PersonCard.ShowPhoto"/>, окремо
    /// від самого зображення: інакше сусідні вузли однакового розміру мали б різні
    /// внутрішні поля залежно від того, у кого є фото.
    /// </summary>
    public bool ShowPhoto { get; init; }

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
