using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.App.Localization;
using FamilyTree.App.Services;
using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;
using FamilyTree.Domain.Layout;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// ViewModel вкладки «Хто кому» (T-4.4): вибір двох осіб → назва зв'язку особи 2
/// відносно особи 1 та пояснення шляху (через <see cref="KinshipPathExplainer"/>).
/// </summary>
public partial class WhoIsWhoViewModel : ObservableObject
{
    private readonly IDocumentSession _session;
    private readonly KinshipPathExplainer _explainer;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private Person? _person1;

    [ObservableProperty]
    private Person? _person2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(NoResult))]
    private string? _resultSummary;

    public WhoIsWhoViewModel(IDocumentSession session, KinshipPathExplainer explainer, ILocalizationService localization)
    {
        _session = session;
        _explainer = explainer;
        _localization = localization;
        _session.DocumentChanged += (_, _) => Reset();
        _session.ContentChanged += (_, _) => RefreshPersons();
        localization.LanguageChanged += (_, _) => Recompute(); // назви зв'язку перекладаються
        RefreshPersons();
    }

    /// <summary>Вузли міні-графа шляху (лише особи з ланцюжка) — вигляд як у дереві.</summary>
    public ObservableCollection<TreeNodeViewModel> PathNodes { get; } = new();

    /// <summary>Ребра міні-графа шляху.</summary>
    public ObservableCollection<TreeEdgeViewModel> PathEdges { get; } = new();

    [ObservableProperty]
    private double _pathCanvasWidth;

    [ObservableProperty]
    private double _pathCanvasHeight;

    /// <summary>Чи є що показувати в міні-графі (у шляху ≥ 2 осіб).</summary>
    public bool HasPathGraph => PathNodes.Count > 0;

    /// <summary>Усі особи документа (відсортовані) для вибору.</summary>
    public ObservableCollection<Person> Persons { get; } = new();

    /// <summary>Кроки шляху родства.</summary>
    public ObservableCollection<string> Steps { get; } = new();

    public bool HasResult => !string.IsNullOrEmpty(ResultSummary);

    public bool NoResult => !HasResult;

    partial void OnPerson1Changed(Person? value) => Recompute();

    partial void OnPerson2Changed(Person? value) => Recompute();

    private void Recompute()
    {
        Steps.Clear();
        ClearPathGraph();

        if (Person1 is not { } root || Person2 is not { } relative)
        {
            ResultSummary = null;
            return;
        }

        var doc = _session.Current;
        var graph = new FamilyGraph(doc.Persons, doc.ParentChildLinks, doc.SpouseLinks);
        var path = _explainer.Explain(root, relative, graph);

        ResultSummary = path.Summary;
        foreach (var step in path.Steps)
        {
            Steps.Add(step);
        }

        BuildPathGraph(path.PersonChain, graph, doc.Persons.ToDictionary(p => p.Id));
    }

    /// <summary>
    /// Будує маленький граф лише з осіб ланцюжка. Рівень (Y) визначається напрямком
    /// кожного кроку (батько — вище, дитина — нижче, подружжя — той самий рівень),
    /// X зростає вздовж ланцюжка → природна «галочка» через спільного предка.
    /// </summary>
    private void BuildPathGraph(IReadOnlyList<Guid> chain, FamilyGraph graph, IReadOnlyDictionary<Guid, Person> persons)
    {
        if (chain.Count < 2)
        {
            OnPropertyChanged(nameof(HasPathGraph));
            return;
        }

        const double colStep = TreeLayoutEngine.NodeWidth + 40;
        const double rowStep = TreeLayoutEngine.NodeHeight + 46;
        const double halfW = TreeLayoutEngine.NodeWidth / 2;
        const double halfH = TreeLayoutEngine.NodeHeight / 2;

        // Рівні відносно кроків.
        var levels = new int[chain.Count];
        var spouseStep = new bool[chain.Count]; // spouseStep[i] — крок (i-1)->i є подружнім
        for (var i = 1; i < chain.Count; i++)
        {
            var prev = chain[i - 1];
            var cur = chain[i];
            if (graph.GetParents(prev).Any(p => p.Id == cur))
            {
                levels[i] = levels[i - 1] - 1; // предок — вище
            }
            else if (graph.GetChildren(prev).Any(c => c.Id == cur))
            {
                levels[i] = levels[i - 1] + 1; // нащадок — нижче
            }
            else
            {
                levels[i] = levels[i - 1]; // подружжя — той самий рівень
                spouseStep[i] = true;
            }
        }

        var minLevel = levels.Min();
        var maxLevel = levels.Max();

        var xs = new double[chain.Count];
        var ys = new double[chain.Count];
        for (var i = 0; i < chain.Count; i++)
        {
            xs[i] = i * colStep;
            ys[i] = (levels[i] - minLevel) * rowStep;
        }

        // Ребра між сусідніми особами.
        for (var i = 1; i < chain.Count; i++)
        {
            PathEdges.Add(new TreeEdgeViewModel(
                xs[i - 1] + halfW, ys[i - 1] + halfH,
                xs[i] + halfW, ys[i] + halfH,
                isSpouse: spouseStep[i]));
        }

        // Вузли.
        var youBadge = _localization.GetString("Tree_You");
        for (var i = 0; i < chain.Count; i++)
        {
            if (!persons.TryGetValue(chain[i], out var person))
            {
                continue;
            }

            PathNodes.Add(new TreeNodeViewModel(chain[i])
            {
                X = xs[i],
                Y = ys[i],
                FullName = person.FullName,
                Years = FormatYears(person),
                RelationBadge = i == 0 ? youBadge : null,
                IsRoot = i == 0,
            });
        }

        PathCanvasWidth = (chain.Count - 1) * colStep + TreeLayoutEngine.NodeWidth;
        PathCanvasHeight = (maxLevel - minLevel) * rowStep + TreeLayoutEngine.NodeHeight;
        OnPropertyChanged(nameof(HasPathGraph));
    }

    private void ClearPathGraph()
    {
        PathNodes.Clear();
        PathEdges.Clear();
        PathCanvasWidth = 0;
        PathCanvasHeight = 0;
        OnPropertyChanged(nameof(HasPathGraph));
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

    private void RefreshPersons()
    {
        var previous1 = Person1?.Id;
        var previous2 = Person2?.Id;

        Persons.Clear();
        foreach (var person in _session.Current.Persons
                     .OrderBy(p => p.LastName, StringComparer.CurrentCulture)
                     .ThenBy(p => p.FirstName, StringComparer.CurrentCulture))
        {
            Persons.Add(person);
        }

        Person1 = Persons.FirstOrDefault(p => p.Id == previous1);
        Person2 = Persons.FirstOrDefault(p => p.Id == previous2);
    }

    private void Reset()
    {
        Person1 = null;
        Person2 = null;
        ResultSummary = null;
        Steps.Clear();
        ClearPathGraph();
        RefreshPersons();
    }
}
