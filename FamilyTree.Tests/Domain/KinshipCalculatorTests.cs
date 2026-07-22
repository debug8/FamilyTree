using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Структурні тести KinshipCalculator (тип зв'язку, кроки, спільні предки, подружжя)
/// на спільній родині KinshipTestFamily.
/// </summary>
public class KinshipCalculatorTests : IClassFixture<KinshipTestFamily>
{
    private readonly KinshipTestFamily _family;
    private readonly KinshipCalculator _calc = new(new CommonAncestorFinder(), new UkrainianKinshipFormatter());

    public KinshipCalculatorTests(KinshipTestFamily family) => _family = family;

    private KinshipResult Relation(string root, string relative) =>
        _calc.Compute(_family[root], _family[relative], _family.Graph);

    [Fact]
    public void Parent_is_direct_ancestor_one_step_up()
    {
        var r = Relation("Тарас", "Андрій");

        r.Kind.ShouldBe(KinshipKind.DirectAncestor);
        r.StepsUp.ShouldBe(1);
        r.StepsDown.ShouldBe(0);
    }

    [Fact]
    public void Child_is_direct_descendant_one_step_down()
    {
        var r = Relation("Тарас", "Данило");

        r.Kind.ShouldBe(KinshipKind.DirectDescendant);
        r.StepsUp.ShouldBe(0);
        r.StepsDown.ShouldBe(1);
    }

    [Fact]
    public void Full_sibling_has_two_common_ancestors()
    {
        var r = Relation("Тарас", "Леся");

        r.Kind.ShouldBe(KinshipKind.Collateral);
        r.SiblingKind.ShouldBe(SiblingKind.Full);
        r.StepsUp.ShouldBe(1);
        r.StepsDown.ShouldBe(1);
        r.CommonAncestorIds.Count.ShouldBe(2); // Андрій + Олена
    }

    [Fact]
    public void Half_sibling_shares_one_parent()
    {
        Relation("Тарас", "Марко").SiblingKind.ShouldBe(SiblingKind.HalfPaternal);
        Relation("Тарас", "Настя").SiblingKind.ShouldBe(SiblingKind.HalfMaternal);
    }

    [Fact]
    public void Spouse_has_no_blood_link()
    {
        Relation("Тарас", "Катя").Kind.ShouldBe(KinshipKind.Spouse);
    }

    [Fact]
    public void Same_person_is_detected()
    {
        Relation("Тарас", "Тарас").Kind.ShouldBe(KinshipKind.SamePerson);
    }

    [Fact]
    public void Unrelated_persons_report_none()
    {
        Relation("Катя", "Леся").Kind.ShouldBe(KinshipKind.None);
    }
}
