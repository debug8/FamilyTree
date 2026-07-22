using FamilyTree.Domain;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

public class PersonTests
{
    private static Person NewPerson() => new()
    {
        LastName = "Шевченко",
        FirstName = "Тарас",
        Gender = Gender.Male,
    };

    [Fact]
    public void IsAlive_is_true_when_no_death_date()
    {
        var person = NewPerson();

        person.IsAlive.ShouldBeTrue();
    }

    [Fact]
    public void IsAlive_is_false_when_death_date_set()
    {
        var person = NewPerson();
        person.DeathDate = new DateOnly(1861, 3, 10);

        person.IsAlive.ShouldBeFalse();
    }

    [Fact]
    public void FullName_combines_available_parts_and_skips_missing_middle()
    {
        var person = NewPerson();

        person.FullName.ShouldBe("Шевченко Тарас");

        person.MiddleName = "Григорович";
        person.FullName.ShouldBe("Шевченко Тарас Григорович");
    }

    [Fact]
    public void New_person_gets_nonempty_id_by_default()
    {
        NewPerson().Id.ShouldNotBe(Guid.Empty);
    }
}
