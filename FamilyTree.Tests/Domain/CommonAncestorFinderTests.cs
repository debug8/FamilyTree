using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

public class CommonAncestorFinderTests
{
    // gp → p, uncle;  p → me, sib;  uncle → cousin;  stranger — окремо
    private static readonly Person Gp = Make("Дід", Gender.Male);
    private static readonly Person P = Make("Батько", Gender.Male);
    private static readonly Person Uncle = Make("Дядько", Gender.Male);
    private static readonly Person Me = Make("Я", Gender.Male);
    private static readonly Person Sib = Make("Сестра", Gender.Female);
    private static readonly Person Cousin = Make("Кузен", Gender.Male);
    private static readonly Person Stranger = Make("Чужий", Gender.Male);

    private static Person Make(string name, Gender g) => new() { LastName = name, FirstName = name, Gender = g };

    private static FamilyGraph Graph() => new(
        new[] { Gp, P, Uncle, Me, Sib, Cousin, Stranger },
        new[]
        {
            new ParentChildLink { ParentId = Gp.Id, ChildId = P.Id },
            new ParentChildLink { ParentId = Gp.Id, ChildId = Uncle.Id },
            new ParentChildLink { ParentId = P.Id, ChildId = Me.Id },
            new ParentChildLink { ParentId = P.Id, ChildId = Sib.Id },
            new ParentChildLink { ParentId = Uncle.Id, ChildId = Cousin.Id },
        },
        Array.Empty<SpouseLink>());

    private readonly CommonAncestorFinder _finder = new();

    [Fact]
    public void Siblings_share_parent_at_distance_one_one()
    {
        var nca = _finder.FindNearest(Me.Id, Sib.Id, Graph());

        nca.ShouldNotBeNull();
        nca!.AncestorId.ShouldBe(P.Id);
        nca.StepsFromA.ShouldBe(1);
        nca.StepsFromB.ShouldBe(1);
    }

    [Fact]
    public void Cousins_share_grandparent_at_distance_two_two()
    {
        var nca = _finder.FindNearest(Me.Id, Cousin.Id, Graph());

        nca!.AncestorId.ShouldBe(Gp.Id);
        nca.StepsFromA.ShouldBe(2);
        nca.StepsFromB.ShouldBe(2);
    }

    [Fact]
    public void Direct_ancestor_is_its_own_nca_with_zero_distance()
    {
        var nca = _finder.FindNearest(Me.Id, P.Id, Graph());

        nca!.AncestorId.ShouldBe(P.Id);
        nca.StepsFromA.ShouldBe(1);
        nca.StepsFromB.ShouldBe(0);
    }

    [Fact]
    public void No_common_ancestor_returns_null()
    {
        _finder.FindNearest(Me.Id, Stranger.Id, Graph()).ShouldBeNull();
    }

    [Fact]
    public void Same_person_is_own_ancestor_at_zero_zero()
    {
        var nca = _finder.FindNearest(Me.Id, Me.Id, Graph());

        nca!.AncestorId.ShouldBe(Me.Id);
        nca.StepsFromA.ShouldBe(0);
        nca.StepsFromB.ShouldBe(0);
    }
}
