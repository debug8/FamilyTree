using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Тести УКРАЇНСЬКИХ назв родства (T-3.2): через повний виклик KinshipCalculator
/// на спільній родині KinshipTestFamily. Майбутній EnglishKinshipFormatterTests (T-3.5)
/// має дзеркалити цей клас на тих самих парах осіб.
/// </summary>
public class UkrainianKinshipFormatterTests : IClassFixture<KinshipTestFamily>
{
    private readonly KinshipTestFamily _family;
    private readonly KinshipCalculator _calc = new(new CommonAncestorFinder(), new UkrainianKinshipFormatter());

    public UkrainianKinshipFormatterTests(KinshipTestFamily family) => _family = family;

    [Theory]
    [InlineData("Андрій", "батько")]
    [InlineData("Олена", "мати")]
    [InlineData("Іван", "дід")]
    [InlineData("Марія", "баба")]
    [InlineData("Богдан", "прадід")]
    [InlineData("Віра", "прабаба")]
    [InlineData("Остап", "прапрадід")]
    [InlineData("Одарка", "прапрабаба")]
    public void Direct_ancestors(string relative, string expected) =>
        NameFor("Тарас", relative).ShouldBe(expected);

    [Theory]
    [InlineData("Данило", "син")]
    [InlineData("Соломія", "дочка")]
    [InlineData("Максим", "онук")]
    [InlineData("Остапчик", "правнук")]
    public void Direct_descendants(string relative, string expected) =>
        NameFor("Тарас", relative).ShouldBe(expected);

    [Theory]
    [InlineData("Богдан", "правнук")]
    [InlineData("Остап", "праправнук")]
    public void Descendants_seen_from_great_grandparents(string root, string expected) =>
        NameFor(root, "Тарас").ShouldBe(expected);

    [Theory]
    [InlineData("Леся", "сестра")]
    [InlineData("Марко", "єдинокровний брат")]
    [InlineData("Настя", "єдиноутробна сестра")]
    public void Siblings_full_and_half(string relative, string expected) =>
        NameFor("Тарас", relative).ShouldBe(expected);

    [Theory]
    [InlineData("Софія", "тітка")]
    [InlineData("Микола", "дядько")]
    [InlineData("Ірина", "тітка")]
    [InlineData("Галя", "двоюрідна баба")]
    [InlineData("Гнат", "двоюрідний прадід")]
    [InlineData("Петро", "троюрідний дід")]
    [InlineData("Влас", "двоюрідний дядько")]
    [InlineData("Роман", "троюрідний дядько")]
    public void Older_collateral_branch(string relative, string expected) =>
        NameFor("Тарас", relative).ShouldBe(expected);

    [Theory]
    [InlineData("Юрко", "двоюрідний брат")]
    [InlineData("Дарина", "двоюрідна сестра")]
    [InlineData("Ніна", "троюрідна сестра")]
    public void Cousins(string relative, string expected) =>
        NameFor("Тарас", relative).ShouldBe(expected);

    [Theory]
    [InlineData("Влад", "племінник")]
    [InlineData("Тимко", "внучатий племінник")]
    [InlineData("Зірка", "двоюрідна племінниця")]
    public void Younger_collateral_branch(string relative, string expected) =>
        NameFor("Тарас", relative).ShouldBe(expected);

    [Theory]
    [InlineData("Влад", "Тарас", "дядько")]
    [InlineData("Юрко", "Тарас", "двоюрідний брат")]
    [InlineData("Галя", "Тарас", "внучатий племінник")]
    [InlineData("Гнат", "Тарас", "правнучатий племінник")]
    [InlineData("Петро", "Тарас", "двоюрідний внучатий племінник")]
    public void Relation_seen_from_the_other_side(string root, string relative, string expected) =>
        NameFor(root, relative).ShouldBe(expected);

    [Fact]
    public void Wife_is_druzhyna() => NameFor("Тарас", "Катя").ShouldBe("дружина");

    [Fact]
    public void Husband_is_cholovik() => NameFor("Катя", "Тарас").ShouldBe("чоловік");

    [Fact]
    public void No_relation_message() =>
        NameFor("Катя", "Леся").ShouldBe("родинний зв'язок не встановлено");

    [Fact]
    public void Same_person_message() =>
        NameFor("Тарас", "Тарас").ShouldBe("та сама особа");

    [Fact]
    public void Unknown_gender_shows_both_variants() =>
        NameFor("Тарас", "Дитя").ShouldBe("син / дочка");

    [Fact]
    public void Deep_cousins_get_ordinal_prefix()
    {
        var (graph, left, right) = KinshipTestFamily.DeepCousinChain(depth: 5);

        _calc.Compute(left, right, graph).DisplayName.ShouldBe("п'ятиюрідний брат");
    }

    private string NameFor(string root, string relative) =>
        _calc.Compute(_family[root], _family[relative], _family.Graph).DisplayName;
}
