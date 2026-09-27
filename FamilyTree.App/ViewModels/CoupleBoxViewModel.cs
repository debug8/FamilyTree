namespace FamilyTree.App.ViewModels;

/// <summary>Рамка навколо подружжя в чинному шлюбі (малюється позаду карток осіб).</summary>
/// <param name="Card">
/// Картка підказки про шлюб. Раніше тут був готовий рядок; відколи зміст підказки
/// налаштовується, рамка носить дані, а не текст — рішення «що показати» приймає
/// <see cref="CoupleCardBuilder"/>, а «як показати» — шаблон CoupleCardTemplate.
/// </param>
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
    CoupleCard? Card = null, Guid MemberA = default, Guid MemberB = default, Guid LinkId = default);
