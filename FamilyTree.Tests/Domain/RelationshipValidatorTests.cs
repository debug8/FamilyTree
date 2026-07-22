using FamilyTree.Domain;
using FamilyTree.Domain.Validation;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

public class RelationshipValidatorTests
{
    private readonly RelationshipValidator _validator = new();

    private static Person Make(string name, Gender gender = Gender.Unknown, DateOnly? birth = null, DateOnly? death = null) => new()
    {
        LastName = name,
        FirstName = name,
        Gender = gender,
        BirthDate = birth,
        DeathDate = death,
    };

    private static ParentChildLink Pc(Person parent, Person child, ParentRole role = ParentRole.Biological) =>
        new() { ParentId = parent.Id, ChildId = child.Id, ParentRole = role };

    // --- Жорсткі помилки --------------------------------------------------

    [Fact]
    public void Self_parent_is_error()
    {
        var a = Make("A");
        var candidate = new ParentChildLink { ParentId = a.Id, ChildId = a.Id };

        var result = _validator.ValidateParentChild(candidate, new[] { a }, Array.Empty<ParentChildLink>());

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(m => m.Key == ValidationKeys.SelfParent);
    }

    [Fact]
    public void Duplicate_parent_child_is_error()
    {
        var parent = Make("P");
        var child = Make("C");
        var existing = new[] { Pc(parent, child) };

        var result = _validator.ValidateParentChild(Pc(parent, child), new[] { parent, child }, existing);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(m => m.Key == ValidationKeys.DuplicateParentChild);
    }

    [Fact]
    public void Ancestor_cycle_is_error()
    {
        var a = Make("A");
        var b = Make("B");
        var c = Make("C");
        // A -> B -> C існують; додаємо C -> A, що замикає цикл.
        var existing = new[] { Pc(a, b), Pc(b, c) };

        var result = _validator.ValidateParentChild(Pc(c, a), new[] { a, b, c }, existing);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(m => m.Key == ValidationKeys.CycleDetected);
    }

    [Fact]
    public void Third_biological_parent_of_same_gender_is_error()
    {
        var father1 = Make("Father1", Gender.Male);
        var father2 = Make("Father2", Gender.Male);
        var child = Make("Child");
        var existing = new[] { Pc(father1, child) };

        var result = _validator.ValidateParentChild(Pc(father2, child), new[] { father1, father2, child }, existing);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(m => m.Key == ValidationKeys.SecondBiologicalFather);
    }

    [Fact]
    public void Second_biological_parent_of_other_gender_is_allowed()
    {
        var father = Make("Father", Gender.Male);
        var mother = Make("Mother", Gender.Female);
        var child = Make("Child");
        var existing = new[] { Pc(father, child) };

        var result = _validator.ValidateParentChild(Pc(mother, child), new[] { father, mother, child }, existing);

        result.IsValid.ShouldBeTrue();
    }

    // --- М'які попередження ----------------------------------------------

    [Fact]
    public void Parent_younger_than_child_is_warning_not_error()
    {
        var parent = Make("P", Gender.Female, new DateOnly(2010, 1, 1));
        var child = Make("C", Gender.Male, new DateOnly(1990, 1, 1));

        var result = _validator.ValidateParentChild(Pc(parent, child), new[] { parent, child }, Array.Empty<ParentChildLink>());

        result.IsValid.ShouldBeTrue();
        result.Warnings.ShouldContain(m => m.Key == ValidationKeys.ParentYoungerThanChild);
    }

    [Fact]
    public void Parent_under_twelve_at_birth_is_warning()
    {
        var parent = Make("P", Gender.Female, new DateOnly(2000, 1, 1));
        var child = Make("C", Gender.Male, new DateOnly(2010, 1, 1)); // різниця 10 років

        var result = _validator.ValidateParentChild(Pc(parent, child), new[] { parent, child }, Array.Empty<ParentChildLink>());

        result.IsValid.ShouldBeTrue();
        result.Warnings.ShouldContain(m => m.Key == ValidationKeys.ChildBornBeforeParentAdult);
    }

    [Fact]
    public void Valid_parent_child_has_no_errors_or_warnings()
    {
        var parent = Make("P", Gender.Female, new DateOnly(1970, 1, 1));
        var child = Make("C", Gender.Male, new DateOnly(1995, 1, 1));

        var result = _validator.ValidateParentChild(Pc(parent, child), new[] { parent, child }, Array.Empty<ParentChildLink>());

        result.IsValid.ShouldBeTrue();
        result.HasWarnings.ShouldBeFalse();
    }

    // --- Подружжя ---------------------------------------------------------

    [Fact]
    public void Self_spouse_is_error()
    {
        var a = Make("A");
        var candidate = SpouseLink.Create(a.Id, a.Id);

        var result = _validator.ValidateSpouse(candidate, Array.Empty<SpouseLink>());

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(m => m.Key == ValidationKeys.SelfSpouse);
    }

    [Fact]
    public void Duplicate_spouse_regardless_of_order_is_error()
    {
        var a = Make("A");
        var b = Make("B");
        var existing = new[] { SpouseLink.Create(a.Id, b.Id) };

        var result = _validator.ValidateSpouse(SpouseLink.Create(b.Id, a.Id), existing);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(m => m.Key == ValidationKeys.DuplicateSpouse);
    }

    // --- Дати особи -------------------------------------------------------

    [Fact]
    public void Death_before_birth_is_warning()
    {
        var person = Make("X", Gender.Male, new DateOnly(2000, 1, 1), new DateOnly(1990, 1, 1));

        var result = _validator.ValidatePerson(person);

        result.IsValid.ShouldBeTrue();
        result.Warnings.ShouldContain(m => m.Key == ValidationKeys.DeathBeforeBirth);
    }
}
