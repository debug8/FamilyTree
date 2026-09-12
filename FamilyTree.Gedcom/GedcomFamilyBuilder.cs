using System.Globalization;
using FamilyTree.Domain;
using FamilyTree.Storage;

namespace FamilyTree.Gedcom;

/// <summary>
/// Виведення записів <c>FAM</c> з ребер документа (T-5.2, Частина 2) — ядро експорту.
///
/// <para>Алгоритм:</para>
/// <list type="number">
/// <item>для кожної дитини зібрати її батьків, окремо по кожній ролі
///   (<see cref="ParentRole.Biological"/> / <see cref="ParentRole.Adoptive"/> /
///   <see cref="ParentRole.Step"/>) — так родина лишається однорідною за роллю,
///   і <c>PEDI</c> у <c>FAMC</c> має одне значення на всю родину;</item>
/// <item>згрупувати дітей за ключем «пара батьків + роль» — це і є <c>FAM</c>;</item>
/// <item>подружжя: якщо пара вже має родину — домалювати їй <c>MARR</c>/<c>DIV</c>;
///   якщо ні (бездітні) — створити окрему;</item>
/// <item>особи без ребер не потрапляють у жодну родину;</item>
/// <item>пронумерувати все детерміновано, щоб два експорти збігалися побайтово.</item>
/// </list>
///
/// <para>
/// Коректність ключа спирається на доменні інваріанти: <c>RelationshipValidator</c>
/// не пускає третього біологічного батька й другого тієї самої статі, а
/// <c>DocumentIntegrity.RemoveExtraBiologicalParents</c> дочищає це при завантаженні.
/// </para>
/// </summary>
public static class GedcomFamilyBuilder
{
    /// <summary>Порядок ролей у виводі — щоб біологічна родина йшла першою.</summary>
    private static readonly ParentRole[] RoleOrder =
        { ParentRole.Biological, ParentRole.Adoptive, ParentRole.Step };

    public static GedcomModel Build(FamilyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var persons = OrderPersons(document.Persons);
        var order = new Dictionary<Guid, int>(persons.Count);
        var xrefs = new Dictionary<Guid, string>(persons.Count);

        for (var i = 0; i < persons.Count; i++)
        {
            order[persons[i].Id] = i;
            xrefs[persons[i].Id] = "I" + (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        var byId = persons.ToDictionary(p => p.Id);
        var draft = new Dictionary<FamilyKey, FamilyDraft>();

        BuildFromChildren(document, byId, draft);
        AttachMarriages(document, byId, draft);

        var families = Materialize(draft, order);
        return new GedcomModel(persons, xrefs, families);
    }

    /// <summary>
    /// Порядок експорту — навмисно за ПІБ, а не за <c>Id</c>: він стабільний між
    /// запусками й читабельний у diff. Порівняння ординарне, щоб результат не залежав
    /// від культури машини (інакше «детермінований експорт» ламався б на іншій локалі).
    /// </summary>
    private static List<Person> OrderPersons(IEnumerable<Person> persons) => persons
        .OrderBy(p => p.LastName, StringComparer.Ordinal)
        .ThenBy(p => p.FirstName, StringComparer.Ordinal)
        .ThenBy(p => p.MiddleName ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(p => p.Id)
        .ToList();

    /// <summary>Кроки 1–2: родини з батьківських ребер.</summary>
    private static void BuildFromChildren(
        FamilyDocument document,
        Dictionary<Guid, Person> byId,
        Dictionary<FamilyKey, FamilyDraft> draft)
    {
        var linksByChild = document.ParentChildLinks
            .Where(l => byId.ContainsKey(l.ChildId) && byId.ContainsKey(l.ParentId))
            .GroupBy(l => l.ChildId);

        foreach (var childGroup in linksByChild)
        {
            foreach (var role in RoleOrder)
            {
                var parents = childGroup
                    .Where(l => l.ParentRole == role)
                    .Select(l => byId[l.ParentId])
                    .DistinctBy(p => p.Id)
                    .ToList();

                if (parents.Count == 0)
                {
                    continue;
                }

                foreach (var slots in AssignSlots(parents))
                {
                    var key = new FamilyKey(slots.HusbandId, slots.WifeId, role);
                    Get(draft, key).Children.Add(childGroup.Key);
                }
            }
        }
    }

    /// <summary>
    /// Крок 3: подружжя. Шлюб чіпляється до вже наявної родини тієї самої пари
    /// (перевага — біологічній), інакше народжується бездітна родина. Без цього
    /// подружжя, яке лише усиновило дитину, отримало б два <c>FAM</c>: один із
    /// дитиною, другий зі шлюбом.
    /// </summary>
    private static void AttachMarriages(
        FamilyDocument document,
        Dictionary<Guid, Person> byId,
        Dictionary<FamilyKey, FamilyDraft> draft)
    {
        foreach (var link in document.SpouseLinks)
        {
            if (!byId.TryGetValue(link.Person1Id, out var first) || !byId.TryGetValue(link.Person2Id, out var second))
            {
                continue;
            }

            var slots = AssignSlots(new List<Person> { first, second }).Single();

            var existing = draft.Keys
                .Where(k => k.SamePair(slots.HusbandId, slots.WifeId))
                .OrderBy(k => Array.IndexOf(RoleOrder, k.Role))
                .Select(k => (FamilyKey?)k)
                .FirstOrDefault();

            var key = existing ?? new FamilyKey(slots.HusbandId, slots.WifeId, ParentRole.Biological);
            Get(draft, key).Marriages.Add(link);
        }
    }

    /// <summary>Крок 5: детермінований порядок родин і нумерація <c>@F…@</c>.</summary>
    private static List<GedcomFamily> Materialize(
        Dictionary<FamilyKey, FamilyDraft> draft,
        Dictionary<Guid, int> order)
    {
        int Index(Guid? id) => id is { } value && order.TryGetValue(value, out var i) ? i : int.MaxValue;

        var ordered = draft
            .OrderBy(pair => Index(pair.Key.HusbandId))
            .ThenBy(pair => Index(pair.Key.WifeId))
            .ThenBy(pair => Array.IndexOf(RoleOrder, pair.Key.Role))
            .ToList();

        var families = new List<GedcomFamily>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var (key, value) = (ordered[i].Key, ordered[i].Value);

            families.Add(new GedcomFamily
            {
                Xref = "F" + (i + 1).ToString(CultureInfo.InvariantCulture),
                HusbandId = key.HusbandId,
                WifeId = key.WifeId,
                Role = key.Role,
                ChildIds = value.Children.OrderBy(id => Index(id)).ToList(),
                Marriages = value.Marriages
                    .OrderBy(m => m.MarriageDate?.EffectiveYear ?? int.MaxValue)
                    .ThenBy(m => m.Id)
                    .ToList(),
            });
        }

        return families;
    }

    /// <summary>
    /// Розкладає батьків по слотах <c>HUSB</c>/<c>WIFE</c>.
    /// <para>
    /// Стать вирішує: чоловік — <c>HUSB</c>, жінка — <c>WIFE</c>. Коли стать не
    /// розрізняє (обидва невідомі або обидва однакові — валідатор дозволяє перше
    /// й таке трапляється у чужих файлах), слоти дає менший <c>Id</c>: важлива не
    /// «правильність», а відтворюваність експорту.
    /// </para>
    /// <para>
    /// Понад двох батьків однієї ролі (можливо для Adoptive/Step) не влазить в один
    /// <c>FAM</c>: перші двоє утворюють пару, кожен наступний — власну родину з одним
    /// батьком. Так нічого не губиться.
    /// </para>
    /// </summary>
    private static IEnumerable<Slots> AssignSlots(List<Person> parents)
    {
        // Сортування за Id тут потрібне заради ДЕТЕРМІНОВАНОСТІ — щоб той самий документ
        // завжди давав той самий розподіл HUSB/WIFE. Хронології воно не дає й не має:
        // у межах мілісекунди UUIDv7 упорядкований випадково (див. Entity.Id), а при
        // імпорті осіб створюється саме пачками. Не «покращувати» на «хто раніше
        // створений, той HUSB» — цього тут не буде.
        var sorted = parents.OrderBy(p => p.Id).ToList();

        if (sorted.Count == 1)
        {
            yield return Single(sorted[0]);
            yield break;
        }

        var first = sorted[0];
        var second = sorted[1];

        if (first.Gender == Gender.Female && second.Gender != Gender.Female)
        {
            yield return new Slots(second.Id, first.Id);
        }
        else if (second.Gender == Gender.Female && first.Gender != Gender.Female)
        {
            yield return new Slots(first.Id, second.Id);
        }
        else
        {
            // Обидві статі однакові чи невідомі — вирішує порядок Id.
            yield return new Slots(first.Id, second.Id);
        }

        for (var i = 2; i < sorted.Count; i++)
        {
            yield return Single(sorted[i]);
        }
    }

    private static Slots Single(Person parent) =>
        parent.Gender == Gender.Female ? new Slots(null, parent.Id) : new Slots(parent.Id, null);

    private static FamilyDraft Get(Dictionary<FamilyKey, FamilyDraft> draft, FamilyKey key)
    {
        if (!draft.TryGetValue(key, out var value))
        {
            draft[key] = value = new FamilyDraft();
        }

        return value;
    }

    private readonly record struct Slots(Guid? HusbandId, Guid? WifeId);

    private readonly record struct FamilyKey(Guid? HusbandId, Guid? WifeId, ParentRole Role)
    {
        /// <summary>Та сама пара незалежно від розподілу по слотах.</summary>
        public bool SamePair(Guid? husbandId, Guid? wifeId) =>
            (HusbandId == husbandId && WifeId == wifeId)
            || (HusbandId == wifeId && WifeId == husbandId);
    }

    private sealed class FamilyDraft
    {
        public List<Guid> Children { get; } = new();

        public List<SpouseLink> Marriages { get; } = new();
    }
}
