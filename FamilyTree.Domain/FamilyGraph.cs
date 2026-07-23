namespace FamilyTree.Domain;

/// <summary>
/// Граф родини в пам'яті (розд. 2.3). Будується зі списків осіб і зв'язків та дає
/// швидку навігацію: батьки, діти, подружжя, сиблінги, зв'язний компонент.
/// Прямі суміжності (батьки/діти/подружжя) — O(1) через попередньо побудовані списки.
/// Граф незмінний після побудови; на зміну даних створюється новий граф.
/// </summary>
public sealed class FamilyGraph
{
    private static readonly IReadOnlyList<Person> Empty = Array.Empty<Person>();

    private readonly Dictionary<Guid, Person> _persons;
    private readonly Dictionary<Guid, List<Guid>> _parents = new();   // дитина -> батьки
    private readonly Dictionary<Guid, List<Guid>> _children = new();  // батько/мати -> діти
    private readonly Dictionary<Guid, List<Guid>> _spouses = new();   // особа -> подружжя (симетрично)
    private readonly Dictionary<(Guid, Guid), bool> _spouseActive = new(); // пара -> чинний шлюб

    public FamilyGraph(
        IEnumerable<Person> persons,
        IEnumerable<ParentChildLink> parentChildLinks,
        IEnumerable<SpouseLink> spouseLinks)
    {
        ArgumentNullException.ThrowIfNull(persons);
        ArgumentNullException.ThrowIfNull(parentChildLinks);
        ArgumentNullException.ThrowIfNull(spouseLinks);

        _persons = persons.ToDictionary(p => p.Id);

        foreach (var link in parentChildLinks)
        {
            // Ігноруємо ребра, що посилаються на невідомі особи (захист від «висячих» посилань).
            if (!_persons.ContainsKey(link.ParentId) || !_persons.ContainsKey(link.ChildId))
            {
                continue;
            }

            AddEdge(_children, link.ParentId, link.ChildId);
            AddEdge(_parents, link.ChildId, link.ParentId);
        }

        foreach (var link in spouseLinks)
        {
            if (!_persons.ContainsKey(link.Person1Id) || !_persons.ContainsKey(link.Person2Id))
            {
                continue;
            }

            AddEdge(_spouses, link.Person1Id, link.Person2Id);
            AddEdge(_spouses, link.Person2Id, link.Person1Id);
            _spouseActive[OrderPair(link.Person1Id, link.Person2Id)] = link.IsActive;
        }
    }

    /// <summary>Усі особи графа.</summary>
    public IReadOnlyCollection<Person> Persons => _persons.Values;

    /// <summary>Кількість осіб.</summary>
    public int PersonCount => _persons.Count;

    /// <summary>Чи є особа в графі.</summary>
    public bool Contains(Guid personId) => _persons.ContainsKey(personId);

    /// <summary>Повертає особу за Id або null.</summary>
    public Person? Find(Guid personId) => _persons.GetValueOrDefault(personId);

    /// <summary>Повертає особу за Id; кидає виняток, якщо особи немає.</summary>
    public Person GetPerson(Guid personId) =>
        _persons.TryGetValue(personId, out var person)
            ? person
            : throw new KeyNotFoundException($"Особу з Id {personId} не знайдено у графі.");

    /// <summary>Батьки особи (0–2 і більше, якщо є нерідні).</summary>
    public IReadOnlyList<Person> GetParents(Guid personId) => Neighbors(_parents, personId);

    /// <summary>Діти особи.</summary>
    public IReadOnlyList<Person> GetChildren(Guid personId) => Neighbors(_children, personId);

    /// <summary>Подружжя особи (симетричний зв'язок).</summary>
    public IReadOnlyList<Person> GetSpouses(Guid personId) => Neighbors(_spouses, personId);

    /// <summary>Чи чинний шлюб між двома особами (false — розлучені або не подружжя).</summary>
    public bool IsSpouseActive(Guid a, Guid b) =>
        _spouseActive.TryGetValue(OrderPair(a, b), out var active) && active;

    private static (Guid, Guid) OrderPair(Guid a, Guid b) =>
        a.CompareTo(b) <= 0 ? (a, b) : (b, a);

    /// <summary>
    /// Сиблінги — особи, що мають щонайменше одного спільного батька/матір (без самої особи).
    /// Включає як рідних, так і зведених; розрізнення рідні/зведені — на рівні розрахунку родства.
    /// </summary>
    public IReadOnlyList<Person> GetSiblings(Guid personId)
    {
        if (!_persons.ContainsKey(personId) || !_parents.TryGetValue(personId, out var parents))
        {
            return Empty;
        }

        var seen = new HashSet<Guid> { personId };
        var result = new List<Person>();

        foreach (var parentId in parents)
        {
            foreach (var childId in _children[parentId])
            {
                if (seen.Add(childId))
                {
                    result.Add(_persons[childId]);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Зв'язний компонент графа, що містить особу: усі досяжні через ребра
    /// «батько-дитина» та «подружжя». Для ізольованої особи — лише вона сама.
    /// </summary>
    public IReadOnlyList<Person> GetConnectedComponent(Guid personId)
    {
        if (!_persons.ContainsKey(personId))
        {
            return Empty;
        }

        var visited = new HashSet<Guid> { personId };
        var queue = new Queue<Guid>();
        queue.Enqueue(personId);

        var result = new List<Person> { _persons[personId] };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in Adjacent(current))
            {
                if (visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                    result.Add(_persons[neighbor]);
                }
            }
        }

        return result;
    }

    private IEnumerable<Guid> Adjacent(Guid personId)
    {
        if (_parents.TryGetValue(personId, out var parents))
        {
            foreach (var id in parents)
            {
                yield return id;
            }
        }

        if (_children.TryGetValue(personId, out var children))
        {
            foreach (var id in children)
            {
                yield return id;
            }
        }

        if (_spouses.TryGetValue(personId, out var spouses))
        {
            foreach (var id in spouses)
            {
                yield return id;
            }
        }
    }

    private IReadOnlyList<Person> Neighbors(Dictionary<Guid, List<Guid>> map, Guid personId) =>
        map.TryGetValue(personId, out var ids)
            ? ids.Select(id => _persons[id]).ToList()
            : Empty;

    private static void AddEdge(Dictionary<Guid, List<Guid>> map, Guid from, Guid to)
    {
        if (!map.TryGetValue(from, out var list))
        {
            list = new List<Guid>();
            map[from] = list;
        }

        if (!list.Contains(to))
        {
            list.Add(to);
        }
    }
}
