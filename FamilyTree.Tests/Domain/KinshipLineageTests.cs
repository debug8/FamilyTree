using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Тести лінії спорідненості (по батькові/по матері) та детального стилю назв.
/// </summary>
public class KinshipLineageTests : IClassFixture<KinshipTestFamily>
{
    private readonly KinshipTestFamily _family;

    public KinshipLineageTests(KinshipTestFamily family) => _family = family;

    private KinshipResult Relation(string root, string relative, KinshipNamingStyle style = KinshipNamingStyle.Standard)
    {
        var calc = new KinshipCalculator(
            new CommonAncestorFinder(),
            new UkrainianKinshipFormatter { Style = style });
        return calc.Compute(_family[root], _family[relative], _family.Graph);
    }

    [Fact]
    public void Fathers_sister_is_paternal_lineage()
    {
        Relation("Тарас", "Софія").Lineage.ShouldBe(Lineage.Paternal);
    }

    [Fact]
    public void Mothers_sister_is_maternal_lineage()
    {
        Relation("Тарас", "Ірина").Lineage.ShouldBe(Lineage.Maternal);
    }

    [Fact]
    public void Full_sibling_lineage_is_mixed()
    {
        Relation("Тарас", "Леся").Lineage.ShouldBe(Lineage.Mixed);
    }

    [Fact]
    public void Cousin_carries_paternal_lineage()
    {
        Relation("Тарас", "Юрко").Lineage.ShouldBe(Lineage.Paternal);
    }

    [Theory]
    [InlineData("Софія", "тітка (по батькові)")]
    [InlineData("Ірина", "тітка (по матері)")]
    [InlineData("Юрко", "двоюрідний брат (по батькові)")]
    public void Detailed_style_appends_lineage(string relative, string expected)
    {
        Relation("Тарас", relative, KinshipNamingStyle.Detailed).DisplayName.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Софія", "тітка")]
    [InlineData("Ірина", "тітка")]
    public void Standard_style_has_no_lineage_suffix(string relative, string expected)
    {
        Relation("Тарас", relative).DisplayName.ShouldBe(expected);
    }

    [Fact]
    public void Detailed_style_does_not_touch_siblings()
    {
        Relation("Тарас", "Леся", KinshipNamingStyle.Detailed).DisplayName.ShouldBe("сестра");
    }
}
