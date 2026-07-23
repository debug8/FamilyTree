using FamilyTree.Domain;
using FamilyTree.Domain.Layout;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

public class TreeLayoutEngineTests
{
    private const double Tolerance = 0.001;

    // 4 покоління:
    //  G1: Gp1 ═ Gp2
    //  G2: Par ═ ParSp
    //  G3: Ch ═ ChSp,  Ch2
    //  G4: Gch (дитина Ch+ChSp)
    private static readonly Person Gp1 = Make("Дід", Gender.Male);
    private static readonly Person Gp2 = Make("Баба", Gender.Female);
    private static readonly Person Par = Make("Батько", Gender.Male);
    private static readonly Person ParSp = Make("Мати", Gender.Female);
    private static readonly Person Ch = Make("Син", Gender.Male);
    private static readonly Person ChSp = Make("Невістка", Gender.Female);
    private static readonly Person Ch2 = Make("Дочка", Gender.Female);
    private static readonly Person Gch = Make("Онук", Gender.Male);

    private static Person Make(string name, Gender g) => new() { LastName = name, FirstName = name, Gender = g };

    private static FamilyGraph Graph() => new(
        new[] { Gp1, Gp2, Par, ParSp, Ch, ChSp, Ch2, Gch },
        new[]
        {
            new ParentChildLink { ParentId = Gp1.Id, ChildId = Par.Id },
            new ParentChildLink { ParentId = Gp2.Id, ChildId = Par.Id },
            new ParentChildLink { ParentId = Par.Id, ChildId = Ch.Id },
            new ParentChildLink { ParentId = ParSp.Id, ChildId = Ch.Id },
            new ParentChildLink { ParentId = Par.Id, ChildId = Ch2.Id },
            new ParentChildLink { ParentId = ParSp.Id, ChildId = Ch2.Id },
            new ParentChildLink { ParentId = Ch.Id, ChildId = Gch.Id },
            new ParentChildLink { ParentId = ChSp.Id, ChildId = Gch.Id },
        },
        new[]
        {
            SpouseLink.Create(Gp1.Id, Gp2.Id),
            SpouseLink.Create(Par.Id, ParSp.Id),
            SpouseLink.Create(Ch.Id, ChSp.Id),
        });

    private readonly TreeLayoutEngine _engine = new();

    private static void AssertNoOverlapPerLevel(TreeLayout layout)
    {
        foreach (var level in layout.Nodes.GroupBy(n => Math.Round(n.Y, 3)))
        {
            var xs = level.Select(n => n.X).OrderBy(x => x).ToList();
            for (var i = 1; i < xs.Count; i++)
            {
                (xs[i] - xs[i - 1]).ShouldBeGreaterThanOrEqualTo(TreeLayoutEngine.NodeWidth - Tolerance);
            }
        }
    }

    private static void AssertCoupleAdjacent(TreeLayout layout, Person a, Person b)
    {
        var na = layout.Nodes.Single(n => n.PersonId == a.Id);
        var nb = layout.Nodes.Single(n => n.PersonId == b.Id);

        na.Y.ShouldBe(nb.Y, Tolerance);
        Math.Abs(na.X - nb.X).ShouldBe(TreeLayoutEngine.ColumnStep, Tolerance);
    }

    [Fact]
    public void Descendants_places_all_persons()
    {
        var layout = _engine.Build(Graph(), Gp1.Id, TreeMode.Descendants);

        layout.Nodes.Count.ShouldBe(8);
    }

    [Fact]
    public void Descendants_have_no_overlap_within_a_level()
    {
        AssertNoOverlapPerLevel(_engine.Build(Graph(), Gp1.Id, TreeMode.Descendants));
    }

    [Fact]
    public void Descendants_place_spouses_side_by_side()
    {
        var layout = _engine.Build(Graph(), Gp1.Id, TreeMode.Descendants);

        AssertCoupleAdjacent(layout, Gp1, Gp2);
        AssertCoupleAdjacent(layout, Par, ParSp);
        AssertCoupleAdjacent(layout, Ch, ChSp);
    }

    [Fact]
    public void Descendants_span_four_levels()
    {
        var layout = _engine.Build(Graph(), Gp1.Id, TreeMode.Descendants);

        layout.Nodes.Select(n => n.Level).Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public void Ancestors_from_grandchild_reach_great_grandparents()
    {
        var layout = _engine.Build(Graph(), Gch.Id, TreeMode.Ancestors);

        layout.Nodes.Select(n => n.PersonId).ShouldContain(Gp1.Id);
        AssertNoOverlapPerLevel(layout);

        // Онук — коренева особа — має бути на найнижчому рівні (найбільший Y).
        var gch = layout.Nodes.Single(n => n.PersonId == Gch.Id);
        gch.Y.ShouldBe(layout.Nodes.Max(n => n.Y), Tolerance);
    }

    [Fact]
    public void Full_mode_covers_whole_component_without_overlap()
    {
        var layout = _engine.Build(Graph(), Par.Id, TreeMode.FullRelatives);

        layout.Nodes.Count.ShouldBe(8);
        AssertNoOverlapPerLevel(layout);
    }

    [Fact]
    public void Unknown_root_yields_empty_layout()
    {
        _engine.Build(Graph(), Guid.NewGuid(), TreeMode.Descendants).Nodes.ShouldBeEmpty();
    }
}
