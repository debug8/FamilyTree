namespace FamilyTree.Domain.Seeding;

/// <summary>
/// Параметри генерації демо-родини (T-5.5). Керують розміром і "складністю" зв'язків.
/// Значення за замовчуванням дають невелику, але багату на різні назви родства родину.
/// Усі числа нормалізуються методом <see cref="Normalized"/> (значення поза межами обрізаються).
/// </summary>
public sealed class DemoFamilyOptions
{
    public const int MinGenerations = 2;
    public const int MaxGenerations = 8;
    public const int MinPersons = 4;
    public const int MaxPersonsLimit = 2000;
    public const int MinChildrenPerCouple = 2;
    public const int MaxChildrenPerCoupleLimit = 8;

    /// <summary>Кількість поколінь (глибина дерева нащадків від засновників).</summary>
    public int Generations { get; set; } = 4;

    /// <summary>Верхня межа кількості осіб (генерація зупиняється, коли досягнуто).</summary>
    public int MaxPersons { get; set; } = 40;

    /// <summary>Максимум дітей на подружню пару (фактична кількість — випадкова в межах 2..це).</summary>
    public int MaxChildrenPerCouple { get; set; } = 4;

    /// <summary>
    /// Складні зв'язки: повторні шлюби й зведені сиблінги (спільний лише один із батьків),
    /// подекуди дитина з невідомою статтю. Дає більше різновидів назв родства й свояцтва.
    /// </summary>
    public bool ComplexRelations { get; set; } = true;

    /// <summary>Додавати частину розлучень (колишнє подружжя) для демонстрації відповідних назв.</summary>
    public bool IncludeDivorces { get; set; } = true;

    /// <summary>Насіння генератора випадкових чисел. null — недетерміноване (кожен раз інша родина).</summary>
    public int? Seed { get; set; }

    /// <summary>Повертає копію з обрізаними до допустимих меж числовими значеннями.</summary>
    public DemoFamilyOptions Normalized() => new()
    {
        Generations = Math.Clamp(Generations, MinGenerations, MaxGenerations),
        MaxPersons = Math.Clamp(MaxPersons, MinPersons, MaxPersonsLimit),
        MaxChildrenPerCouple = Math.Clamp(MaxChildrenPerCouple, MinChildrenPerCouple, MaxChildrenPerCoupleLimit),
        ComplexRelations = ComplexRelations,
        IncludeDivorces = IncludeDivorces,
        Seed = Seed,
    };
}
