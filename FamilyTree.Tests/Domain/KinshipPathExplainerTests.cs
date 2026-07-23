using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

public class KinshipPathExplainerTests : IClassFixture<KinshipTestFamily>
{
    private readonly KinshipTestFamily _family;
    private readonly KinshipPathExplainer _explainer =
        new(new CommonAncestorFinder(), new KinshipCalculator(new CommonAncestorFinder(), new UkrainianKinshipFormatter()));

    public KinshipPathExplainerTests(KinshipTestFamily family) => _family = family;

    private KinshipPath Explain(string root, string relative) =>
        _explainer.Explain(_family[root], _family[relative], _family.Graph);

    [Fact]
    public void Cousin_path_goes_through_common_ancestor()
    {
        var path = Explain("Тарас", "Юрко");

        path.PersonChain[0].ShouldBe(_family["Тарас"].Id);
        path.PersonChain[^1].ShouldBe(_family["Юрко"].Id);
        path.PersonChain.Count.ShouldBe(5); // Тарас → батько → НСП → тітка → Юрко
        path.Steps.Count.ShouldBe(path.PersonChain.Count - 1);
        path.Summary.ShouldContain("двоюрідний брат");

        // Ланцюжок проходить через спільного предка (Іван або Марія).
        path.PersonChain.ShouldContain(id => id == _family["Іван"].Id || id == _family["Марія"].Id);
    }

    [Fact]
    public void Direct_parent_path_has_single_step()
    {
        var path = Explain("Тарас", "Андрій");

        path.PersonChain.ShouldBe(new[] { _family["Тарас"].Id, _family["Андрій"].Id });
        path.Steps.Count.ShouldBe(1);
        path.Steps[0].ShouldContain("батько");
        path.Summary.ShouldContain("батько");
    }

    [Fact]
    public void Same_person_has_no_steps()
    {
        var path = Explain("Тарас", "Тарас");

        path.PersonChain.ShouldHaveSingleItem();
        path.Steps.ShouldBeEmpty();
    }

    [Fact]
    public void Unrelated_has_empty_chain()
    {
        var path = Explain("Катя", "Леся");

        path.PersonChain.ShouldBeEmpty();
        path.Summary.ShouldContain("родинний зв'язок не встановлено");
    }

    [Fact]
    public void Spouse_has_direct_step()
    {
        var path = Explain("Тарас", "Катя");

        path.PersonChain.ShouldBe(new[] { _family["Тарас"].Id, _family["Катя"].Id });
        path.Steps.ShouldHaveSingleItem();
    }
}
