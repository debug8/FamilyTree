namespace FamilyTree.Domain;

/// <summary>
/// Ненапрямлене ребро «подружжя» (розд. 3.2). Для унікальності пари ідентифікатори
/// зберігаються впорядкованими: <see cref="Person1Id"/> ≤ <see cref="Person2Id"/>.
/// Створювати через <see cref="Create"/>, щоб гарантувати цей інваріант.
/// </summary>
public sealed class SpouseLink : Entity
{
    /// <summary>Перший із подружжя (менший Id).</summary>
    public required Guid Person1Id { get; init; }

    /// <summary>Другий із подружжя (більший Id).</summary>
    public required Guid Person2Id { get; init; }

    /// <summary>Дата шлюбу.</summary>
    public DateOnly? MarriageDate { get; set; }

    /// <summary>Дата розлучення (null — шлюб чинний).</summary>
    public DateOnly? DivorceDate { get; set; }

    /// <summary>Чи чинний шлюб (немає дати розлучення).</summary>
    public bool IsActive => DivorceDate is null;

    /// <summary>
    /// Створює зв'язок подружжя, нормалізуючи порядок ідентифікаторів
    /// (Person1Id ≤ Person2Id), щоб та сама пара завжди мала однакове представлення.
    /// </summary>
    public static SpouseLink Create(Guid personA, Guid personB, DateOnly? marriageDate = null, DateOnly? divorceDate = null)
    {
        var (first, second) = personA.CompareTo(personB) <= 0 ? (personA, personB) : (personB, personA);
        return new SpouseLink
        {
            Person1Id = first,
            Person2Id = second,
            MarriageDate = marriageDate,
            DivorceDate = divorceDate,
        };
    }

    /// <summary>
    /// Чи перетинаються періоди шлюбу цього та іншого зв'язку. Період — [MarriageDate, DivorceDate];
    /// відсутня межа вважається відкритою (−∞ для дати шлюбу, +∞ для дати розлучення).
    /// <para>
    /// Призначення (B-16): відрізнити справжній дубль (та сама пара, шлюби перетинаються в часі —
    /// одночасним шлюб бути не може) від повторного шлюбу тієї самої пари з роздільними періодами
    /// (одружились → розлучились → одружились знову), який легітимний. Перевірку «та сама пара»
    /// робить викликач — тут порівнюються лише періоди.
    /// </para>
    /// </summary>
    public bool PeriodOverlaps(SpouseLink other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var start1 = MarriageDate ?? DateOnly.MinValue;
        var end1 = DivorceDate ?? DateOnly.MaxValue;
        var start2 = other.MarriageDate ?? DateOnly.MinValue;
        var end2 = other.DivorceDate ?? DateOnly.MaxValue;

        return start1 <= end2 && start2 <= end1;
    }

    /// <summary>Чи стосується цей зв'язок вказаної особи.</summary>
    public bool Involves(Guid personId) => Person1Id == personId || Person2Id == personId;

    /// <summary>Повертає Id другого з подружжя відносно вказаного, або null, якщо особа не в парі.</summary>
    public Guid? SpouseOf(Guid personId)
    {
        if (Person1Id == personId)
        {
            return Person2Id;
        }

        if (Person2Id == personId)
        {
            return Person1Id;
        }

        return null;
    }
}
