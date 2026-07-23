using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.App.Localization;
using FamilyTree.App.Services;
using FamilyTree.Domain;
using FamilyTree.Domain.Layout;

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

    public TreeViewModel(IDocumentSession session, TreeLayoutEngine engine, ILocalizationService localization)
    {
        _session = session;
        _engine = engine;
        _localization = localization;
        _session.DocumentChanged += (_, _) => Rebuild();
        _session.ContentChanged += (_, _) => Rebuild();
        _localization.LanguageChanged += (_, _) => OnPropertyChanged(nameof(AvailableModes));
    }

    /// <summary>Доступні режими дерева (локалізовані назви оновлюються при зміні мови).</summary>
    public IReadOnlyList<TreeModeOption> AvailableModes => ModeOptions.ToList();

    /// <summary>Варіанти глибини: 0 — усі покоління.</summary>
    public IReadOnlyList<int> DepthOptions { get; } = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    public ObservableCollection<TreeNodeViewModel> Nodes { get; } = new();

    public ObservableCollection<TreeEdgeViewModel> Edges { get; } = new();

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

        foreach (var node in layout.Nodes)
        {
            var person = persons[node.PersonId];
            Nodes.Add(new TreeNodeViewModel(node.PersonId)
            {
                X = node.X,
                Y = node.Y,
                FullName = person.FullName,
                Years = FormatYears(person),
                IsRoot = node.PersonId == rootId,
            });
        }

        var halfW = TreeLayoutEngine.NodeWidth / 2;
        var halfH = TreeLayoutEngine.NodeHeight / 2;
        foreach (var edge in layout.Edges)
        {
            var a = positions[edge.FromId];
            var b = positions[edge.ToId];
            Edges.Add(new TreeEdgeViewModel(
                a.X + halfW, a.Y + halfH,
                b.X + halfW, b.Y + halfH,
                edge.Kind == EdgeKind.Spouse));
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
}
