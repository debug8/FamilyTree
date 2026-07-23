using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Тести АНГЛІЙСЬКИХ назв родства (T-3.5) на родині з англійськими іменами.
/// Структура — дзеркало української тестової родини; назви відрізняються (не 1:1).
/// </summary>
public class EnglishKinshipFormatterTests : IClassFixture<EnglishKinshipTestFamily>
{
    private readonly EnglishKinshipTestFamily _family;
    private readonly KinshipCalculator _calc = new(new CommonAncestorFinder(), new EnglishKinshipFormatter());

    public EnglishKinshipFormatterTests(EnglishKinshipTestFamily family) => _family = family;

    [Theory]
    [InlineData("Andriy", "father")]
    [InlineData("Olena", "mother")]
    [InlineData("Ivan", "grandfather")]
    [InlineData("Maria", "grandmother")]
    [InlineData("Bohdan", "great-grandfather")]
    [InlineData("Ostap", "great-great-grandfather")]
    [InlineData("Danylo", "son")]
    [InlineData("Maksym", "grandson")]
    [InlineData("Ostapchyk", "great-grandson")]
    [InlineData("Lesia", "sister")]
    [InlineData("Marko", "half-brother")]
    [InlineData("Nastia", "half-sister")]
    [InlineData("Mykola", "uncle")]
    [InlineData("Sofia", "aunt")]
    [InlineData("Halyna", "grandaunt")]
    [InlineData("Hnat", "great-granduncle")]
    [InlineData("Yurko", "first cousin")]
    [InlineData("Nina", "second cousin")]
    [InlineData("Vlas", "first cousin once removed")]
    [InlineData("Petro", "first cousin twice removed")]
    [InlineData("Vlad", "nephew")]
    [InlineData("Tymko", "grandnephew")]
    public void Relatives_of_Taras(string relative, string expected) =>
        NameFor("Taras", relative).ShouldBe(expected);

    [Fact]
    public void Wife_and_husband()
    {
        NameFor("Taras", "Katya").ShouldBe("wife");
        NameFor("Katya", "Taras").ShouldBe("husband");
    }

    [Fact]
    public void Unknown_gender_child_shows_both() =>
        NameFor("Taras", "Child").ShouldBe("son / daughter");

    [Fact]
    public void Same_person_and_no_relation()
    {
        NameFor("Taras", "Taras").ShouldBe("the same person");
        NameFor("Katya", "Lesia").ShouldBe("no relation");
    }

    private string NameFor(string root, string relative) =>
        _calc.Compute(_family[root], _family[relative], _family.Graph).DisplayName;
}
