using FamilyTree.Domain;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

public class EntityEqualityTests
{
    private static Person PersonWithId(Guid id) => new()
    {
        Id = id,
        LastName = "Тест",
        FirstName = "Тест",
        Gender = Gender.Unknown,
    };

    [Fact]
    public void Persons_with_same_id_are_equal()
    {
        var id = Guid.NewGuid();
        var a = PersonWithId(id);
        var b = PersonWithId(id);

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Persons_with_different_ids_are_not_equal()
    {
        var a = PersonWithId(Guid.NewGuid());
        var b = PersonWithId(Guid.NewGuid());

        a.Equals(b).ShouldBeFalse();
        (a != b).ShouldBeTrue();
    }

    [Fact]
    public void Different_entity_types_with_same_id_are_not_equal()
    {
        var id = Guid.NewGuid();
        var person = PersonWithId(id);
        var link = new ParentChildLink { Id = id, ParentId = Guid.NewGuid(), ChildId = Guid.NewGuid() };

        person.Equals(link).ShouldBeFalse();
    }

    [Fact]
    public void Entity_is_not_equal_to_null()
    {
        PersonWithId(Guid.NewGuid()).Equals(null).ShouldBeFalse();
    }
}
