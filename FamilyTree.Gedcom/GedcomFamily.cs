using FamilyTree.Domain;

namespace FamilyTree.Gedcom;

/// <summary>
/// Родина у сенсі GEDCOM (<c>FAM</c>): пара батьків + діти + події шлюбу.
/// <b>Не доменна сутність</b> — виводиться з ребер документа лише на час обміну
/// й ніде не зберігається (див. T-5.2: додавати «Family» в домен було б зайвим
/// рефакторингом графа, розкладки, валідатора й схеми файлу).
/// </summary>
public sealed class GedcomFamily
{
    /// <summary>Ідентифікатор запису без «собачок» (<c>F1</c>, <c>F2</c>, …).</summary>
    public required string Xref { get; init; }

    /// <summary>Особа у слоті <c>HUSB</c>, або null.</summary>
    public Guid? HusbandId { get; init; }

    /// <summary>Особа у слоті <c>WIFE</c>, або null.</summary>
    public Guid? WifeId { get; init; }

    /// <summary>
    /// Роль батьків щодо дітей цієї родини. Родина завжди однорідна за роллю:
    /// біологічні батьки й усиновлювачі дають РІЗНІ <c>FAM</c>, і саме тому
    /// <c>PEDI</c> у дитячому <c>FAMC</c> — одне значення на всю родину.
    /// </summary>
    public required ParentRole Role { get; init; }

    /// <summary>Діти, у порядку експорту осіб.</summary>
    public required IReadOnlyList<Guid> ChildIds { get; init; }

    /// <summary>
    /// Шлюби цієї пари. Кілька — коли пара одружувалась повторно; тоді запис
    /// отримує кілька подій <c>MARR</c>/<c>DIV</c> (відома втрата: більшість
    /// сторонніх програм покаже лише першу).
    /// </summary>
    public required IReadOnlyList<SpouseLink> Marriages { get; init; }

    /// <summary>Чи стоїть особа в одному зі слотів батьків.</summary>
    public bool HasSpouse(Guid personId) => HusbandId == personId || WifeId == personId;
}

/// <summary>
/// Готова до запису модель документа: порядок осіб, їхні xref і виведені родини.
/// Проміжний результат <see cref="GedcomFamilyBuilder"/>, вхід для <see cref="GedcomExporter"/>.
/// </summary>
public sealed class GedcomModel
{
    private readonly Dictionary<Guid, string> _personXrefs;
    private readonly Dictionary<Guid, List<GedcomFamily>> _asSpouse;
    private readonly Dictionary<Guid, List<GedcomFamily>> _asChild;

    internal GedcomModel(
        IReadOnlyList<Person> persons,
        Dictionary<Guid, string> personXrefs,
        IReadOnlyList<GedcomFamily> families)
    {
        Persons = persons;
        Families = families;
        _personXrefs = personXrefs;

        _asSpouse = new Dictionary<Guid, List<GedcomFamily>>();
        _asChild = new Dictionary<Guid, List<GedcomFamily>>();

        foreach (var family in families)
        {
            foreach (var parentId in new[] { family.HusbandId, family.WifeId })
            {
                if (parentId is { } id)
                {
                    Append(_asSpouse, id, family);
                }
            }

            foreach (var childId in family.ChildIds)
            {
                Append(_asChild, childId, family);
            }
        }
    }

    /// <summary>Особи в порядку експорту (він же — порядок нумерації xref).</summary>
    public IReadOnlyList<Person> Persons { get; }

    /// <summary>Виведені родини в порядку експорту.</summary>
    public IReadOnlyList<GedcomFamily> Families { get; }

    /// <summary>Xref особи без «собачок» (<c>I1</c>, <c>I2</c>, …).</summary>
    public string XrefOf(Guid personId) => _personXrefs[personId];

    /// <summary>Родини, де особа — один із батьків (теги <c>FAMS</c>).</summary>
    public IReadOnlyList<GedcomFamily> SpouseFamilies(Guid personId) =>
        _asSpouse.TryGetValue(personId, out var list) ? list : Array.Empty<GedcomFamily>();

    /// <summary>Родини, де особа — дитина (теги <c>FAMC</c>).</summary>
    public IReadOnlyList<GedcomFamily> ChildFamilies(Guid personId) =>
        _asChild.TryGetValue(personId, out var list) ? list : Array.Empty<GedcomFamily>();

    private static void Append(Dictionary<Guid, List<GedcomFamily>> map, Guid key, GedcomFamily family)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = new List<GedcomFamily>();
        }

        list.Add(family);
    }
}
