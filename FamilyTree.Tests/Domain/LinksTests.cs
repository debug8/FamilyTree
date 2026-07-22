using FamilyTree.Domain;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

public class LinksTests
{
    [Fact]
    public void ParentChildLink_defaults_to_biological_role()
    {
        var link = new ParentChildLink { ParentId = Guid.NewGuid(), ChildId = Guid.NewGuid() };

        link.ParentRole.ShouldBe(ParentRole.Biological);
    }

    [Fact]
    public void ParentChildLink_Involves_parent_and_child()
    {
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        var link = new ParentChildLink { ParentId = parent, ChildId = child };

        link.Involves(parent).ShouldBeTrue();
        link.Involves(child).ShouldBeTrue();
        link.Involves(Guid.NewGuid()).ShouldBeFalse();
    }

    [Fact]
    public void SpouseLink_Create_normalizes_id_order()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var (expectedFirst, expectedSecond) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);

        var link = SpouseLink.Create(a, b);

        link.Person1Id.ShouldBe(expectedFirst);
        link.Person2Id.ShouldBe(expectedSecond);
    }

    [Fact]
    public void SpouseLink_Create_gives_same_representation_regardless_of_argument_order()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var link1 = SpouseLink.Create(a, b);
        var link2 = SpouseLink.Create(b, a);

        link1.Person1Id.ShouldBe(link2.Person1Id);
        link1.Person2Id.ShouldBe(link2.Person2Id);
    }

    [Fact]
    public void SpouseLink_is_active_until_divorce_date_set()
    {
        var link = SpouseLink.Create(Guid.NewGuid(), Guid.NewGuid());
        link.IsActive.ShouldBeTrue();

        link.DivorceDate = new DateOnly(2000, 1, 1);
        link.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void SpouseLink_SpouseOf_returns_the_other_partner()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var link = SpouseLink.Create(a, b);

        link.SpouseOf(a).ShouldBe(b);
        link.SpouseOf(b).ShouldBe(a);
        link.SpouseOf(Guid.NewGuid()).ShouldBeNull();
    }
}
