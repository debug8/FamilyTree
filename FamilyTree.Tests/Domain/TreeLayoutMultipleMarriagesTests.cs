using FamilyTree.Domain;
using FamilyTree.Domain.Layout;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Розкладка при кількох шлюбах. Раніше <c>BuildDescendantUnit</c> брав партнера через
/// <c>FirstOrDefault</c>, тож особа з двома шлюбами показувалася поруч із випадковим
/// (першим у файлі) партнером, а решта партнерів не потрапляла ні у <c>visited</c>,
/// ні в <c>positions</c> — вони зникали з полотна, і <c>Finalize</c> не малював до них
/// ребер, хоч діти під ними були саме від них.
/// </summary>
public class TreeLayoutMultipleMarriagesTests
{
    private const double Tolerance = 0.001;

    private readonly TreeLayoutEngine _engine = new();
    private readonly Dictionary<string, Person> _p = new();
    private readonly List<ParentChildLink> _pc = new();
    private readonly List<SpouseLink> _sp = new();

    private FamilyGraph Graph() => new(_p.Values, _pc, _sp);

    private Guid Id(string name) => _p[name].Id;

    private void Person(string name, Gender g) =>
        _p[name] = new Person { LastName = name, FirstName = name, Gender = g };

    private void Parents(string father, string mother, params string[] kids)
    {
        foreach (var kid in kids)
        {
            _pc.Add(new ParentChildLink { ParentId = Id(father), ChildId = Id(kid) });
            _pc.Add(new ParentChildLink { ParentId = Id(mother), ChildId = Id(kid) });
        }
    }

    private void Marry(string a, string b, bool divorced = false) =>
        _sp.Add(SpouseLink.Create(
            Id(a),
            Id(b),
            new DateOnly(2000, 1, 1),
            divorced ? new DateOnly(2010, 1, 1) : null));

    /// <summary>
    /// Батько двічі одружений; діти є від обох шлюбів.
    ///   Перша ═ Батько ═ Друга
    ///      │                │
    ///   СинПершої        СинДругої
    /// </summary>
    private void BuildTwoMarriages(bool firstDivorced = true)
    {
        Person("Батько", Gender.Male);
        Person("Перша", Gender.Female);
        Person("Друга", Gender.Female);
        Person("СинПершої", Gender.Male);
        Person("СинДругої", Gender.Male);

        Marry("Батько", "Перша", divorced: firstDivorced);
        Marry("Батько", "Друга");

        Parents("Батько", "Перша", "СинПершої");
        Parents("Батько", "Друга", "СинДругої");
    }

    private static NodeLayout Node(TreeLayout layout, Person person) =>
        layout.Nodes.Single(n => n.PersonId == person.Id);

    // ---- Ніхто не зникає з полотна ---------------------------------------

    [Fact]
    public void Both_partners_are_placed()
    {
        BuildTwoMarriages();

        var layout = _engine.Build(Graph(), Id("Батько"), TreeMode.Descendants);

        layout.Nodes.Count.ShouldBe(5);
        layout.Nodes.Select(n => n.PersonId).ShouldContain(Id("Перша"));
        layout.Nodes.Select(n => n.PersonId).ShouldContain(Id("Друга"));
    }

    [Fact]
    public void Edges_to_both_partners_are_drawn()
    {
        BuildTwoMarriages();

        var layout = _engine.Build(Graph(), Id("Батько"), TreeMode.Descendants);
        var spouseEdges = layout.Edges.Where(e => e.Kind == EdgeKind.Spouse).ToList();

        spouseEdges.Count.ShouldBe(2);
        spouseEdges.ShouldContain(e => Involves(e, Id("Батько"), Id("Перша")));
        spouseEdges.ShouldContain(e => Involves(e, Id("Батько"), Id("Друга")));
    }

    [Fact]
    public void Children_of_both_marriages_keep_edges_to_both_parents()
    {
        BuildTwoMarriages();

        var layout = _engine.Build(Graph(), Id("Батько"), TreeMode.Descendants);
        var parentEdges = layout.Edges.Where(e => e.Kind == EdgeKind.ParentChild).ToList();

        // По два ребра на дитину: від батька і від відповідної матері.
        parentEdges.Count.ShouldBe(4);
        parentEdges.ShouldContain(e => e.FromId == Id("Перша") && e.ToId == Id("СинПершої"));
        parentEdges.ShouldContain(e => e.FromId == Id("Друга") && e.ToId == Id("СинДругої"));
    }

    // ---- Геометрія --------------------------------------------------------

    [Fact]
    public void Person_sits_between_both_partners()
    {
        // S1 — Особа — S2: так кожна пара лишається сусідньою і рамка шлюбу
        // не розтягується через усе полотно.
        BuildTwoMarriages();

        var layout = _engine.Build(Graph(), Id("Батько"), TreeMode.Descendants);
        var father = Node(layout, _p["Батько"]);
        var first = Node(layout, _p["Перша"]);
        var second = Node(layout, _p["Друга"]);

        father.Y.ShouldBe(first.Y, Tolerance);
        father.Y.ShouldBe(second.Y, Tolerance);

        Math.Min(first.X, second.X).ShouldBeLessThan(father.X);
        Math.Max(first.X, second.X).ShouldBeGreaterThan(father.X);

        Math.Abs(father.X - first.X).ShouldBe(TreeLayoutEngine.ColumnStep, Tolerance);
        Math.Abs(father.X - second.X).ShouldBe(TreeLayoutEngine.ColumnStep, Tolerance);
    }

    [Fact]
    public void Children_are_grouped_under_their_own_couple()
    {
        // Дитина від лівого партнера має бути ліворуч від дитини від правого —
        // інакше ребра від рамок шлюбу перетинаються.
        BuildTwoMarriages();

        var layout = _engine.Build(Graph(), Id("Батько"), TreeMode.Descendants);

        Node(layout, _p["СинПершої"]).X.ShouldBeLessThan(Node(layout, _p["СинДругої"]).X);
    }

    [Fact]
    public void No_overlap_within_a_level()
    {
        BuildTwoMarriages();

        var layout = _engine.Build(Graph(), Id("Батько"), TreeMode.Descendants);

        foreach (var level in layout.Nodes.GroupBy(n => Math.Round(n.Y, 3)))
        {
            var xs = level.Select(n => n.X).OrderBy(x => x).ToList();
            for (var i = 1; i < xs.Count; i++)
            {
                (xs[i] - xs[i - 1]).ShouldBeGreaterThanOrEqualTo(TreeLayoutEngine.NodeWidth - Tolerance);
            }
        }
    }

    [Fact]
    public void Two_active_marriages_are_also_handled()
    {
        // Розлучення не є умовою: у даних трапляються два чинних шлюби
        // (напр. власний DemoFamilyGenerator їх створює).
        BuildTwoMarriages(firstDivorced: false);

        var layout = _engine.Build(Graph(), Id("Батько"), TreeMode.Descendants);

        layout.Nodes.Count.ShouldBe(5);
        layout.Edges.Count(e => e.Kind == EdgeKind.Spouse).ShouldBe(2);
    }

    // ---- Три шлюби --------------------------------------------------------

    [Fact]
    public void Three_marriages_place_everyone_exactly_once()
    {
        Person("Він", Gender.Male);
        Person("Ж1", Gender.Female);
        Person("Ж2", Gender.Female);
        Person("Ж3", Gender.Female);
        Person("Д1", Gender.Male);
        Person("Д3", Gender.Female);

        Marry("Він", "Ж1", divorced: true);
        Marry("Він", "Ж2", divorced: true);
        Marry("Він", "Ж3");
        Parents("Він", "Ж1", "Д1");
        Parents("Він", "Ж3", "Д3");

        var layout = _engine.Build(Graph(), Id("Він"), TreeMode.Descendants);

        layout.Nodes.Count.ShouldBe(6);
        layout.Nodes.Select(n => n.PersonId).Distinct().Count().ShouldBe(6);
        layout.Edges.Count(e => e.Kind == EdgeKind.Spouse).ShouldBe(3);
    }

    // ---- Глибина й детермінізм -------------------------------------------

    [Fact]
    public void Depth_limit_stops_recursion_but_keeps_partners_of_the_last_level()
    {
        BuildTwoMarriages();

        // Онук під сином від другого шлюбу — має відпасти за межею глибини.
        Person("Онука", Gender.Female);
        Person("Невістка", Gender.Female);
        Marry("СинДругої", "Невістка");
        Parents("СинДругої", "Невістка", "Онука");

        var layout = _engine.Build(Graph(), Id("Батько"), TreeMode.Descendants, maxDepth: 1);
        var placed = layout.Nodes.Select(n => n.PersonId).ToList();

        placed.ShouldContain(Id("Друга"));      // обидва партнери кореня на місці
        placed.ShouldContain(Id("Невістка"));   // партнер особи на останньому рівні — теж
        placed.ShouldNotContain(Id("Онука"));   // а її діти вже за межею глибини
    }

    [Fact]
    public void Layout_is_deterministic_for_the_same_input()
    {
        // Раніше порядок вузлів і ребер брався з Dictionary/HashSet, тож snapshot-тести
        // розкладки були неможливі.
        BuildTwoMarriages();
        var graph = Graph();

        var first = _engine.Build(graph, Id("Батько"), TreeMode.Descendants);
        var second = _engine.Build(graph, Id("Батько"), TreeMode.Descendants);

        first.Nodes.ShouldBe(second.Nodes);
        first.Edges.ShouldBe(second.Edges);
        first.Width.ShouldBe(second.Width);
        first.Height.ShouldBe(second.Height);
    }

    [Fact]
    public void Full_mode_is_deterministic_too()
    {
        BuildTwoMarriages();
        var graph = Graph();

        var first = _engine.Build(graph, Id("Батько"), TreeMode.FullRelatives);
        var second = _engine.Build(graph, Id("Батько"), TreeMode.FullRelatives);

        first.Nodes.ShouldBe(second.Nodes);
        first.Edges.ShouldBe(second.Edges);
    }

    private static bool Involves(EdgeLayout edge, Guid a, Guid b) =>
        (edge.FromId == a && edge.ToId == b) || (edge.FromId == b && edge.ToId == a);
}
