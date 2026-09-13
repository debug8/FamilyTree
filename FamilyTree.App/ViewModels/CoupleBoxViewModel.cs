namespace FamilyTree.App.ViewModels;

/// <summary>Рамка навколо подружжя в чинному шлюбі (малюється позаду карток осіб).</summary>
/// <param name="Tooltip">Короткий опис шлюбу (подружжя + дата одруження) для підказки.</param>
/// <param name="MemberA">Ідентифікатор першого з подружжя (для підсвітки ребер на дітей).</param>
/// <param name="MemberB">Ідентифікатор другого з подружжя.</param>
/// <param name="LinkId">
/// Ідентифікатор ЧИННОГО <c>SpouseLink</c>, за яким намальовано рамку. У пари може бути
/// кілька зв'язків (повторний шлюб — B-16), тож пари ідентифікаторів для пошуку замало:
/// без цього поля меню рамки могло відкрити давній, розлучений шлюб. Носимо саме Id, а не
/// сам зв'язок: сцену малюють раз, а документ тим часом можуть змінити.
/// </param>
public sealed record CoupleBoxViewModel(
    double X, double Y, double Width, double Height,
    string? Tooltip = null, Guid MemberA = default, Guid MemberB = default, Guid LinkId = default);
