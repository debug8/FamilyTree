using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private readonly IDocumentSession _session;
    private readonly TreeLayoutEngine _engine;

    [ObservableProperty]
    private TreeMode _mode = TreeMode.Descendants;

    [ObservableProperty]
    private int _depth;

    [ObservableProperty]
    private double _canvasWidth;

    [ObservableProperty]
    private double _canvasHeight;

    private Guid? _rootId;

    public TreeViewModel(IDocumentSession session, TreeLayoutEngine engine)
    {
        _session = session;
        _engine = engine;
        _session.DocumentChanged += (_, _) => Rebuild();
        _session.ContentChanged += (_, _) => Rebuild();
    }

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
