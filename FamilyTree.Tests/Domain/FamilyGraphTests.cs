using FamilyTree.Domain;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

public class FamilyGraphTests
{
    // Тестова родина з 3 поколінь:
    //   Покоління 1: g1 (ч) + g2 (ж) — подружжя
    //   Покоління 2: parent (дитина g1,g2); spouse1 (подружжя parent); partner2 (без шлюбу)
    //   Покоління 3: c1, c2 (діти parent + spouse1 — рідні); c3 (дитина parent + partner2 — зведений до c1/c2)
    //   Окремо: loner — ізольована особа без зв'язків
    private static readonly Person G1 = Make("Дід");
    private static readonly Person G2 = Make("Баба");
    private static readonly Person Parent = Make("Батько");
    private static readonly Person Spouse1 = Make("Мати");
    private static readonly Person Partner2 = Make("Партнер");
    private static readonly Person C1 = Make("Син1");
    private static readonly Person C2 = Make("Син2");
    private static readonly Person C3 = Make("Зведений");
    private static readonly Person Loner = Make("Самотній");

    private static Person Make(string name) => new()
    {
        LastName = name,
        FirstName = name,
        Gender = Gender.Unknown,
    };

    private static FamilyGraph BuildGraph()
    {
        var persons = new[] { G1, G2, Parent, Spouse1, Partner2, C1, C2, C3, Loner };

        var pcLinks = new[]
        {
            new ParentChildLink { ParentId = G1.Id, ChildId = Parent.Id },
            new ParentChildLink { ParentId = G2.Id, ChildId = Parent.Id },
            new ParentChildLink { ParentId = Parent.Id, ChildId = C1.Id },
            new ParentChildLink { ParentId = Spouse1.Id, ChildId = C1.Id },
            new ParentChildLink { ParentId = Parent.Id, ChildId = C2.Id },
            new ParentChildLink { ParentId = Spouse1.Id, ChildId = C2.Id },
            new ParentChildLink { ParentId = Parent.Id, ChildId = C3.Id },
            new ParentChildLink { ParentId = Partner2.Id, ChildId = C3.Id },
        };

        var spouseLinks = new[]
        {
            SpouseLink.Create(G1.Id, G2.Id),
            SpouseLink.Create(Parent.Id, Spouse1.Id),
        };

        return new FamilyGraph(persons, pcLinks, spouseLinks);
    }

    [Fact]
    public void GetParents_returns_both_parents()
    {
        var graph = BuildGraph();

        graph.GetParents(Parent.Id).ShouldBe(new[] { G1, G2 }, ignoreOrder: true);
    }

    [Fact]
    public void GetChildren_returns_all_children_including_from_different_partners()
    {
        var graph = BuildGraph();

        graph.GetChildren(Parent.Id).ShouldBe(new[] { C1, C2, C3 }, ignoreOrder: true);
    }

    [Fact]
    public void GetSpouses_returns_only_married_partners()
    {
        var graph = BuildGraph();

        graph.GetSpouses(Parent.Id).ShouldBe(new[] { Spouse1 }, ignoreOrder: true);
    }

    [Fact]
    public void GetSiblings_includes_full_and_half_siblings()
    {
        var graph = BuildGraph();

        // C1 має спільних із C2 обох батьків (рідні), а з C3 — лише parent (зведений).
        graph.GetSiblings(C1.Id).ShouldBe(new[] { C2, C3 }, ignoreOrder: true);
        graph.GetSiblings(C3.Id).ShouldBe(new[] { C1, C2 }, ignoreOrder: true);
    }

    [Fact]
    public void Isolated_person_has_no_relatives_and_own_component()
    {
        var graph = BuildGraph();

        graph.GetParents(Loner.Id).ShouldBeEmpty();
        graph.GetChildren(Loner.Id).ShouldBeEmpty();
        graph.GetSpouses(Loner.Id).ShouldBeEmpty();
        graph.GetSiblings(Loner.Id).ShouldBeEmpty();
        graph.GetConnectedComponent(Loner.Id).ShouldBe(new[] { Loner });
    }

    [Fact]
    public void ConnectedComponent_covers_whole_family_but_not_isolated()
    {
        var graph = BuildGraph();

        var component = graph.GetConnectedComponent(C1.Id);

        component.ShouldBe(new[] { G1, G2, Parent, Spouse1, Partner2, C1, C2, C3 }, ignoreOrder: true);
        component.ShouldNotContain(Loner);
    }

    [Fact]
    public void Unknown_person_is_handled_gracefully()
    {
        var graph = BuildGraph();
        var unknown = Guid.NewGuid();

        graph.Contains(unknown).ShouldBeFalse();
        graph.Find(unknown).ShouldBeNull();
        graph.GetParents(unknown).ShouldBeEmpty();
        graph.GetConnectedComponent(unknown).ShouldBeEmpty();
        Should.Throw<KeyNotFoundException>(() => graph.GetPerson(unknown));
    }

    [Fact]
    public void PersonCount_reflects_all_persons()
    {
        BuildGraph().PersonCount.ShouldBe(9);
    }
}
