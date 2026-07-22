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
