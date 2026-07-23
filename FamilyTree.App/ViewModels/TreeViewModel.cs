using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
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
public partial class TreeViewModel : ObservableObject
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

    [ObservableProperty]
    private TreeMode _mode = TreeMode.Descendants;

    [ObservableProperty]
    private TreeModeOption _selectedMode = ModeOptions[1];

    [ObservableProperty]
    private int _depth = 3;

    [ObservableProperty]
    private double _canvasWidth;

    [ObservableProperty]
    private double _canvasHeight;

    private Guid? _rootId;

    public TreeViewModel(IDocumentSession session, TreeLayoutEngine engine, ILocalizationService localization, KinshipCalculator kinship)
    {
        _session = session;
        _engine = engine;
        _localization = localization;
        _kinship = kinship;
        _session.DocumentChanged += (_, _) => Rebuild();
        _session.ContentChanged += (_, _) => Rebuild();
        _localization.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AvailableModes));
            Rebuild(); // бейджі родства перекладаються
        };
    }

    /// <summary>Перебудувати дерево (напр. після зміни стилю назв родства).</summary>
    public void Refresh() => Rebuild();

    /// <summary>Доступні режими дерева (локалізовані назви оновлюються при зміні мови).</summary>
    public IReadOnlyList<TreeModeOption> AvailableModes => ModeOptions.ToList();

    /// <summary>Варіанти глибини: 0 — усі покоління.</summary>
    public IReadOnlyList<int> DepthOptions { get; } = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    public ObservableCollection<TreeNodeViewModel> Nodes { get; } = new();

    public ObservableCollection<TreeEdgeViewModel> Edges { get; } = new();

    /// <summary>Рамки навколо подружжя в чинному шлюбі (позаду карток).</summary>
    public ObservableCollection<CoupleBoxViewModel> Couples { get; } = new();

    /// <summary>Задає кореневу особу й перебудовує дерево.</summary>
    public void SetRoot(Guid? rootId)
    {
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

    partial void OnSelectedModeChanged(TreeModeOption value)
    {
        if (value is not null)
        {
            Mode = value.Value;
        }
    }

    partial void OnModeChanged(TreeMode value) => Rebuild();

    partial void OnDepthChanged(int value) => Rebuild();

    private void Rebuild()
    {
        Nodes.Clear();
        Edges.Clear();
        Couples.Clear();

        if (_rootId is not { } rootId)
        {
            CanvasWidth = CanvasHeight = 0;
            return;
        }

        var doc = _session.Current;
        var graph = new FamilyGraph(doc.Persons, doc.ParentChildLinks, doc.SpouseLinks);
        if (!graph.Contains(rootId))
        {
            CanvasWidth = CanvasHeight = 0;
            return;
        }

        var layout = _engine.Build(graph, rootId, Mode, Depth);
        var persons = doc.Persons.ToDictionary(p => p.Id);
        var positions = layout.Nodes.ToDictionary(n => n.PersonId);
        var rootPerson = persons[rootId];
        var youBadge = _localization.GetString("Tree_You");

        foreach (var node in layout.Nodes)
        {
            var person = persons[node.PersonId];
            var isRoot = node.PersonId == rootId;
            Nodes.Add(new TreeNodeViewModel(node.PersonId)
            {
                X = node.X,
                Y = node.Y,
                FullName = person.FullName,
                Years = FormatYears(person),
                RelationBadge = isRoot ? youBadge : _kinship.Compute(rootPerson, person, graph, includeAffinity: true).DisplayName,
                IsRoot = isRoot,
                PhotoPath = ResolvePhoto(person.PhotoPath),
                DetailMaiden = Line("Person_MaidenName", person.MaidenName),
                DetailGender = Line("Person_Gender", GenderText(person.Gender)),
                DetailBirth = Line("Person_BirthDate", FormatBirth(person)),
                DetailDeath = person.IsAlive ? null : Line("Person_DeathDate", FormatDate(person.DeathDate)),
                DetailMarriage = Line("Tree_Card_Marriage", FormatMarriages(person, doc, persons)),
                DetailChildren = Line("Tree_Card_Children", graph.GetChildren(person.Id).Count.ToString(CultureInfo.CurrentCulture)),
                DetailNotes = Line("Person_Notes", person.Notes),
            });
        }

        const double couplePad = 6;
        var halfW = TreeLayoutEngine.NodeWidth / 2;
        var halfH = TreeLayoutEngine.NodeHeight / 2;

        // Активні подружжя → рамка + якір знизу рамки; розлучені → пунктирне ребро.
        var coupleAnchors = new List<(Guid A, Guid B, double X, double Y)>();
        var childToParents = new Dictionary<Guid, List<Guid>>();

        foreach (var edge in layout.Edges)
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
                    Couples.Add(new CoupleBoxViewModel(left, top, width, height,
                        BuildCoupleTooltip(edge.FromId, edge.ToId, doc, persons)));
                    coupleAnchors.Add((edge.FromId, edge.ToId, left + width / 2, top + height));
                }
                else
                {
                    Edges.Add(new TreeEdgeViewModel(a.X + halfW, a.Y + halfH, b.X + halfW, b.Y + halfH, IsSpouse: true));
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
                    Edges.Add(new TreeEdgeViewModel(couple.X, couple.Y, childX, childY, IsSpouse: false));
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
                    parent.X + halfW, parent.Y + TreeLayoutEngine.NodeHeight, childX, childY, IsSpouse: false));
            }
        }

        CanvasWidth = layout.Width;
        CanvasHeight = layout.Height;
    }

    private static string FormatYears(Person person)
    {
        var birth = person.BirthDate?.Year.ToString(CultureInfo.InvariantCulture);
        var death = person.DeathDate?.Year.ToString(CultureInfo.InvariantCulture);
        return (birth, death) switch
        {
            (null, null) => string.Empty,
            (not null, null) => birth!,
            (null, not null) => $"–{death}",
            _ => $"{birth}–{death}",
        };
    }

    /// <summary>Рядок картки «Підпис: значення» або null, якщо значення порожнє (рядок ховається).</summary>
    private string? Line(string labelKey, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{_localization.GetString(labelKey)}: {value}";

    private string GenderText(Gender gender) => gender switch
    {
        Gender.Male => _localization.GetString("Gender_Male"),
        Gender.Female => _localization.GetString("Gender_Female"),
        _ => _localization.GetString("Gender_Unknown"),
    };

    private static string FormatDate(DateOnly? date) =>
        date?.ToString("d", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>Дата народження + місце (якщо є): «01.01.1980 · Київ».</summary>
    private static string FormatBirth(Person person)
    {
        var date = FormatDate(person.BirthDate);
        var place = person.BirthPlace;
        return (date, hasPlace: !string.IsNullOrWhiteSpace(place)) switch
        {
            ("", false) => string.Empty,
            ("", true) => place!,
            (_, false) => date,
            _ => $"{date} · {place}",
        };
    }

    /// <summary>Подружжя: «Ім'я (рік шлюбу — рік розлучення)», кілька — через «; ».</summary>
    private static string FormatMarriages(Person person, FamilyDocument doc, IReadOnlyDictionary<Guid, Person> persons)
    {
        var parts = new List<string>();
        foreach (var link in doc.SpouseLinks.Where(l => l.Involves(person.Id)))
        {
            var otherId = link.Person1Id == person.Id ? link.Person2Id : link.Person1Id;
            if (!persons.TryGetValue(otherId, out var other))
            {
                continue;
            }

            var period = FormatMarriagePeriod(link);
            parts.Add(period.Length > 0 ? $"{other.FullName} ({period})" : other.FullName);
        }

        return string.Join("; ", parts);
    }

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

    private static string FormatMarriagePeriod(SpouseLink link)
    {
        var from = link.MarriageDate?.Year.ToString(CultureInfo.InvariantCulture);
        var to = link.DivorceDate?.Year.ToString(CultureInfo.InvariantCulture);
        return (from, to) switch
        {
            (null, null) => string.Empty,
            (not null, null) => from!,
            (null, not null) => $"… – {to}",
            _ => $"{from} – {to}",
        };
    }

    /// <summary>Абсолютний шлях до фото у папці даних (поки лише резолвинг; місце під фото).</summary>
    private static string? ResolvePhoto(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        return Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FamilyTree", relativePath);
    }
}
