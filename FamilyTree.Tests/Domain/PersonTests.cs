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
        person.Death = PersonEvent.WithDate(person.Death, new DateOnly(1861, 3, 10));

        person.IsAlive.ShouldBeFalse();
    }

    [Fact]
    public void IsAlive_is_false_when_marked_deceased_without_a_date()
    {
        // Стан «помер, дата невідома»: у файлі GEDCOM це «1 DEAT» без DATE,
        // у діалозі — знята галочка «Живий» без указаної дати.
        var person = NewPerson();
        person.Deceased = true;

        (person.Death?.Date).ShouldBeNull();
        person.IsAlive.ShouldBeFalse();
    }

    [Fact]
    public void Death_date_alone_still_means_deceased_for_old_files()
    {
        // У старих файлах поля Deceased немає, тож воно читається як false, а смерть
        // виражена лише датою. IsAlive мусить це витримувати.
        var person = NewPerson();
        person.Death = PersonEvent.WithDate(person.Death, new DateOnly(1861, 3, 10));
        person.Deceased = false;

        person.IsAlive.ShouldBeFalse();
    }

    [Fact]
    public void Copy_preserves_deceased_flag()
    {
        var person = NewPerson();
        person.Deceased = true;

        person.Copy().Deceased.ShouldBeTrue();
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
