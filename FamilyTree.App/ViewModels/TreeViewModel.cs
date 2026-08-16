using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.App.Localization;
using FamilyTree.App.Services;
using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;
using FamilyTree.Domain.Layout;
using FamilyTree.Storage;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// ViewModel вкладки «Дерево»: будує граф із документа, розкладає його через
/// <see cref="TreeLayoutEngine"/> і віддає вузли/ребра для рендерингу.
/// Режим/глибина та бейджі родства розширюються в T-4.3.
/// </summary>
public partial class TreeViewModel : ObservableObject, IDisposable
{
    private static readonly IReadOnlyList<TreeModeOption> ModeOptions = new[]
    {
        new TreeModeOption(TreeMode.Ancestors, "Tree_Mode_Ancestors"),
        new TreeModeOption(TreeMode.Descendants, "Tree_Mode_Descendants"),
        new TreeModeOption(TreeMode.FullRelatives, "Tree_Mode_Full"),
    };

    private readonly IDocumentSession _session;
    private readonly TreeLayoutEngine _engine;
    private readonly ILocalizationService _localization;
    private readonly KinshipCalculator _kinship;

    // Складання картки-тултіпа спільне з вкладкою «Особа» (див. PersonCardBuilder).
    private readonly PersonCardBuilder _cards;

    [ObservableProperty]
    private TreeMode _mode = TreeMode.Descendants;

    [ObservableProperty]
    private TreeModeOption _selectedMode = ModeOptions[1];

    [ObservableProperty]
    private int _depth = 3;

    [ObservableProperty]
    private bool _showGenerationBands = true;

    /// <summary>Перевернути дерево по вертикалі (предки знизу).</summary>
    [ObservableProperty]
    private bool _flipVertical;

    [ObservableProperty]
    private double _canvasWidth;

    [ObservableProperty]
    private double _canvasHeight;

    private Guid? _rootId;

    // Стан останньої побудови. Дозволяє перемалювати сцену (переворот по вертикалі,
    // смуги поколінь) без перебудови графа й без повторного розрахунку родства.
    private FamilyDocument? _doc;
    private FamilyGraph? _graph;
    private TreeLayout? _layout;
    private Dictionary<Guid, Person>? _persons;

    // Кеш бейджів родства — найдорожча частина побудови: KinshipCalculator.Compute
    // робить ~5 обходів графа на вузол, і викликається для КОЖНОГО вузла розкладки.
    // Валідний лише для _badgeRootId та поточної мови/стилю назв; скидається через
    // InvalidateBadges() при зміні вмісту, мови, стилю або кореня.
    private readonly Dictionary<Guid, string> _badges = new();
    private Guid? _badgeRootId;

    public TreeViewModel(IDocumentSession session, TreeLayoutEngine engine, ILocalizationService localization, KinshipCalculator kinship)
    {
        _session = session;
        _engine = engine;
        _localization = localization;
        _kinship = kinship;
        _cards = new PersonCardBuilder(localization);

        // Іменовані обробники (а не лямбди) — щоб від них можна було відписатися в Dispose.
        _session.DocumentChanged += OnDocumentOrContentChanged;
        _session.ContentChanged += OnDocumentOrContentChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Перебудувати дерево (напр. після зміни стилю назв родства).</summary>
    public void Refresh()
    {
        InvalidateBadges();
        Rebuild();
    }

    private void OnDocumentOrContentChanged(object? sender, EventArgs e)
    {
        InvalidateBadges(); // зв'язки могли змінитися → назви родства теж
        Rebuild();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AvailableModes));
        InvalidateBadges(); // бейджі родства перекладаються
        Rebuild();
    }

    private void InvalidateBadges()
    {
        _badges.Clear();
        _badgeRootId = null;
    }

    /// <summary>Доступні режими дерева (локалізовані назви оновлюються при зміні мови).</summary>
    public IReadOnlyList<TreeModeOption> AvailableModes => ModeOptions.ToList();

    /// <summary>Варіанти глибини: 0 — усі покоління.</summary>
    public IReadOnlyList<int> DepthOptions { get; } = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    public ObservableCollection<TreeNodeViewModel> Nodes { get; } = new();

    public ObservableCollection<TreeEdgeViewModel> Edges { get; } = new();

    /// <summary>Рамки навколо подружжя в чинному шлюбі (позаду карток).</summary>
    public ObservableCollection<CoupleBoxViewModel> Couples { get; } = new();

    /// <summary>Напівпрозорі смуги-фони поколінь (позаду всього).</summary>
    public ObservableCollection<GenerationBandViewModel> Bands { get; } = new();

    /// <summary>Задає кореневу особу й перебудовує дерево. Повторний вибір тієї самої
    /// особи нічого не робить — інакше сортування/пошук у списку осіб коштували б
    /// повну перебудову дерева без жодної зміни на екрані.</summary>
    public void SetRoot(Guid? rootId)
    {
        if (_rootId == rootId)
        {
            return;
        }

        _rootId = rootId;
        Rebuild();
    }

    /// <summary>Вибирає вузол (для підсвітки).</summary>
    public void SelectNode(Guid personId)
    {
        foreach (var node in Nodes)
        {
            node.IsSelected = node.PersonId == personId;
        }
    }

    /// <summary>Підсвітити ребра, що йдуть до дітей вказаної особи.</summary>
    public void HighlightChildrenOf(Guid parentId)
    {
        foreach (var edge in Edges)
        {
            edge.IsHighlighted = edge.ParentIds.Contains(parentId);
        }
    }

    /// <summary>Підсвітити ребра до спільних дітей подружжя (від рамки шлюбу).</summary>
    public void HighlightChildrenOfCouple(Guid a, Guid b)
    {
        foreach (var edge in Edges)
        {
            edge.IsHighlighted = edge.ParentIds.Contains(a) && edge.ParentIds.Contains(b);
        }
    }

    /// <summary>Підсвітити ребро та обидві особи, які воно з'єднує.</summary>
    public void HighlightEdge(TreeEdgeViewModel edge)
    {
        foreach (var e in Edges)
        {
            e.IsHighlighted = ReferenceEquals(e, edge);
        }

        foreach (var node in Nodes)
        {
            node.IsHighlighted = edge.EndpointIds.Contains(node.PersonId);
        }
    }

    /// <summary>Зняти підсвітку з усіх ребер і вузлів.</summary>
    public void ClearHighlight()
    {
        foreach (var edge in Edges)
        {
            edge.IsHighlighted = false;
        }

        foreach (var node in Nodes)
        {
            node.IsHighlighted = false;
        }
    }

    partial void OnSelectedModeChanged(TreeModeOption value)
    {
        if (value is not null)
        {
            Mode = value.Value;
        }
    }

    partial void OnModeChanged(TreeMode value) => Rebuild();

    partial void OnDepthChanged(int value) => Rebuild();

    // Смуги поколінь і переворот по вертикалі не змінюють ні складу вузлів, ні назв
    // родства — лише координати та фон. Тому достатньо перемалювати сцену з кешованої
    // розкладки, без перебудови графа й без N розрахунків родства.
    partial void OnShowGenerationBandsChanged(bool value) => Render();

    partial void OnFlipVerticalChanged(bool value) => Render();

    /// <summary>
    /// Повна перебудова: граф → розкладка → рендер. Викликається лише коли змінилося
    /// щось, що впливає на СКЛАД сцени (корінь, режим, глибина, вміст документа).
    /// </summary>
    private void Rebuild()
    {
        if (_rootId is not { } rootId)
        {
            ClearScene();
            return;
        }

        var doc = _session.Current;
        var graph = new FamilyGraph(doc.Persons, doc.ParentChildLinks, doc.SpouseLinks);
        if (!graph.Contains(rootId))
        {
            ClearScene();
            return;
        }

        if (_badgeRootId != rootId)
        {
            // Інший корінь — усі назви родства інші, кеш не переносимо.
            _badges.Clear();
            _badgeRootId = rootId;
        }

        _doc = doc;
        _graph = graph;
        _persons = doc.Persons.DistinctBy(p => p.Id).ToDictionary(p => p.Id);
        _layout = _engine.Build(graph, rootId, Mode, Depth);

        Render();
    }

    private void ClearScene()
    {
        Nodes.Clear();
        Edges.Clear();
        Couples.Clear();
        Bands.Clear();
        _doc = null;
        _graph = null;
        _layout = null;
        _persons = null;
        CanvasWidth = CanvasHeight = 0;
    }

    /// <summary>
    /// Перетворює кешовану розкладку у ViewModel-и сцени. Дешево: без обходів графа
    /// й без <see cref="KinshipCalculator"/> (бейджі беруться з кешу).
    /// </summary>
    private void Render()
    {
        if (_rootId is not { } rootId
            || _doc is not { } doc
            || _graph is not { } graph
            || _layout is not { } layout
            || _persons is not { } persons)
        {
            return;
        }

        Nodes.Clear();
        Edges.Clear();
        Couples.Clear();
        Bands.Clear();

        var positions = layout.Nodes.ToDictionary(n => n.PersonId);
        var rootPerson = persons[rootId];
        var youBadge = _localization.GetString("Tree_You");

        // Вертикальний переворот (предки знизу): дзеркалимо Y відносно висоти полотна.
        var flip = FlipVertical;
        var flipH = layout.Height;
        double BoxY(double y, double h) => flip ? flipH - y - h : y; // верхній лівий кут рамки
        double PointY(double y) => flip ? flipH - y : y;             // окрема точка (кінець ребра)

        foreach (var node in layout.Nodes)
        {
            var person = persons[node.PersonId];
            var isRoot = node.PersonId == rootId;
            var badge = isRoot ? youBadge : Badge(rootPerson, person, graph);

            // Кількість дітей беремо з графа (O(1)), а не перебором зв'язків.
            var card = _cards.Build(person, doc, persons, graph.GetChildren(person.Id).Count, badge);

            Nodes.Add(new TreeNodeViewModel(node.PersonId)
            {
                X = node.X,
                Y = BoxY(node.Y, TreeLayoutEngine.NodeHeight),
                FullName = person.FullName,
                NamePrimary = PersonCardBuilder.FormatNamePrimary(person),
                Patronymic = PersonCardBuilder.FormatPatronymic(person),
                Years = card.Years,
                RelationBadge = badge,
                IsRoot = isRoot,
                Card = card,
            });
        }

        const double couplePad = 6;
        var halfW = TreeLayoutEngine.NodeWidth / 2;
        var halfH = TreeLayoutEngine.NodeHeight / 2;

        // Активні подружжя → рамка + якір знизу рамки; розлучені → пунктирне ребро.
        var coupleAnchors = new List<(Guid A, Guid B, double X, double Y)>();
        var childToParents = new Dictionary<Guid, List<Guid>>();

        foreach (var edge in layout.Edges.Reverse())
        {
            if (edge.Kind == EdgeKind.Spouse)
            {
                var a = positions[edge.FromId];
                var b = positions[edge.ToId];
                if (graph.IsSpouseActive(edge.FromId, edge.ToId))
                {
                    var left = Math.Min(a.X, b.X) - couplePad;
                    var top = Math.Min(a.Y, b.Y) - couplePad;
                    var width = Math.Abs(a.X - b.X) + TreeLayoutEngine.NodeWidth + 2 * couplePad;
                    var height = TreeLayoutEngine.NodeHeight + 2 * couplePad;
                    Couples.Add(new CoupleBoxViewModel(left, BoxY(top, height), width, height,
                        BuildCoupleTooltip(edge.FromId, edge.ToId, doc, persons),
                        edge.FromId, edge.ToId));
                    coupleAnchors.Add((edge.FromId, edge.ToId, left + width / 2, top + height));
                }
                else
                {
                    Edges.Add(new TreeEdgeViewModel(
                        a.X + halfW, PointY(a.Y + halfH), b.X + halfW, PointY(b.Y + halfH), isSpouse: true,
                        endpointIds: new HashSet<Guid> { edge.FromId, edge.ToId },
                        tooltip: SpouseTooltip(edge.FromId, edge.ToId, persons)));
                }

                continue;
            }

            // ParentChild: From — батько/мати, To — дитина.
            if (!childToParents.TryGetValue(edge.ToId, out var parents))
            {
                parents = new List<Guid>();
                childToParents[edge.ToId] = parents;
            }

            parents.Add(edge.FromId);
        }

        foreach (var (childId, parentIds) in childToParents)
        {
            var child = positions[childId];
            var childX = child.X + halfW;
            var childY = child.Y;
            var handled = new HashSet<Guid>();

            // Спільна дитина активної пари — одне ребро від рамки шлюбу.
            foreach (var couple in coupleAnchors)
            {
                if (parentIds.Contains(couple.A) && parentIds.Contains(couple.B))
                {
                    Edges.Add(new TreeEdgeViewModel(couple.X, PointY(couple.Y), childX, PointY(childY), isSpouse: false,
                        parentIds: new HashSet<Guid> { couple.A, couple.B },
                        endpointIds: new HashSet<Guid> { couple.A, couple.B, childId },
                        tooltip: EdgeTooltip(new[] { couple.A, couple.B }, childId, persons)));
                    handled.Add(couple.A);
                    handled.Add(couple.B);
                }
            }

            // Решта батьків (одинокі чи розлучені) — окреме ребро від низу картки.
            foreach (var parentId in parentIds)
            {
                if (handled.Contains(parentId))
                {
                    continue;
                }

                var parent = positions[parentId];
                Edges.Add(new TreeEdgeViewModel(
                    parent.X + halfW, PointY(parent.Y + TreeLayoutEngine.NodeHeight), childX, PointY(childY),
                    isSpouse: false,
                    parentIds: new HashSet<Guid> { parentId },
                    endpointIds: new HashSet<Guid> { parentId, childId },
                    tooltip: EdgeTooltip(new[] { parentId }, childId, persons)));
            }
        }

        if (ShowGenerationBands)
        {
            BuildBands(layout.Nodes.Select(n => n.Y), layout.Width, flip, flipH);
        }

        CanvasWidth = layout.Width;
        CanvasHeight = layout.Height;
    }

    /// <summary>
    /// Назва родства для вузла, з кешем. Без кешу зміна глибини чи режиму дерева
    /// перераховувала родство для всіх спільних вузлів заново, хоч корінь той самий.
    /// </summary>
    private string Badge(Person root, Person relative, FamilyGraph graph)
    {
        if (_badges.TryGetValue(relative.Id, out var cached))
        {
            return cached;
        }

        var name = _kinship.Compute(root, relative, graph, includeAffinity: true).DisplayName;
        _badges[relative.Id] = name;
        return name;
    }

    /// <summary>Дві напівпрозорі смуги поколінь, що чергуються: світліша та темніша
    /// (один відтінок, трохи насичений — працює в обох темах).</summary>
    private static readonly string[] BandPalette =
    {
        "#1A3A8FD6", // світліша
        "#3A2F72B0", // темніша
    };

    /// <summary>Будує смугу-фон для кожного покоління (унікального Y-рядка).</summary>
    private void BuildBands(IEnumerable<double> nodeYs, double width, bool flip, double canvasHeight)
    {
        const double pad = TreeLayoutEngine.VerticalGap / 2;
        const double height = TreeLayoutEngine.NodeHeight + 2 * pad;
        var rows = nodeYs.Distinct().OrderBy(y => y).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var top = rows[i] - pad;
            Bands.Add(new GenerationBandViewModel(
                X: 0,
                Y: flip ? canvasHeight - top - height : top,
                Width: width,
                Height: height,
                Fill: BandPalette[i % BandPalette.Length]));
        }
    }

    /// <summary>Підказка ребра «батько–дитина»: «Батьки: X, Y \n Дитина: Z».</summary>
    private string? EdgeTooltip(IEnumerable<Guid> parentIds, Guid childId, IReadOnlyDictionary<Guid, Person> persons)
    {
        var parents = string.Join(", ", parentIds
            .Where(persons.ContainsKey)
            .Select(id => persons[id].FullName));
        if (!persons.TryGetValue(childId, out var child) || parents.Length == 0)
        {
            return null;
        }

        return $"{_localization.GetString("Tree_Edge_Parents")}: {parents}\n" +
               $"{_localization.GetString("Tree_Edge_Child")}: {child.FullName}";
    }

    /// <summary>Підказка пунктирного ребра колишнього подружжя: «Ім'я — Ім'я».</summary>
    private static string? SpouseTooltip(Guid aId, Guid bId, IReadOnlyDictionary<Guid, Person> persons) =>
        persons.TryGetValue(aId, out var a) && persons.TryGetValue(bId, out var b)
            ? $"{a.FullName} — {b.FullName}"
            : null;

    /// <summary>Короткий опис шлюбу для тултіпа рамки: «Ім'я ♥ Ім'я · у шлюбі з 2005».</summary>
    private string? BuildCoupleTooltip(Guid aId, Guid bId, FamilyDocument doc, IReadOnlyDictionary<Guid, Person> persons)
    {
        if (!persons.TryGetValue(aId, out var a) || !persons.TryGetValue(bId, out var b))
        {
            return null;
        }

        var couple = $"{a.FullName}  ♥  {b.FullName}";
        var link = doc.SpouseLinks.FirstOrDefault(l => l.Involves(aId) && l.Involves(bId));
        if (link?.MarriageDate is not { } date)
        {
            return couple;
        }

        var since = string.Format(
            _localization.GetString("Tree_Card_MarriedSince"),
            date.ToString("d", CultureInfo.CurrentCulture));
        return $"{couple}\n{since}";
    }

    public void Dispose()
    {
        _session.DocumentChanged -= OnDocumentOrContentChanged;
        _session.ContentChanged -= OnDocumentOrContentChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
