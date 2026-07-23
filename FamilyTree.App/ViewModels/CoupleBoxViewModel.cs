namespace FamilyTree.App.ViewModels;

/// <summary>Рамка навколо подружжя в чинному шлюбі (малюється позаду карток осіб).</summary>
/// <param name="Tooltip">Короткий опис шлюбу (подружжя + дата одруження) для підказки.</param>
/// <param name="MemberA">Ідентифікатор першого з подружжя (для підсвітки ребер на дітей).</param>
/// <param name="MemberB">Ідентифікатор другого з подружжя.</param>
public sealed record CoupleBoxViewModel(
    double X, double Y, double Width, double Height,
    string? Tooltip = null, Guid MemberA = default, Guid MemberB = default);
