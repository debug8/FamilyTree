using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

public class FormerSpouseTests
{
    private static Person Make(string name, Gender g) => new() { LastName = name, FirstName = name, Gender = g };

    private readonly KinshipCalculator _calc = new(new CommonAncestorFinder(), new UkrainianKinshipFormatter());

    [Fact]
    public void Active_marriage_is_spouse()
    {
        var husband = Make("Ч", Gender.Male);
        var wife = Make("Ж", Gender.Female);
        var graph = new FamilyGraph(
            new[] { husband, wife },
            Array.Empty<ParentChildLink>(),
            new[] { SpouseLink.Create(husband.Id, wife.Id, new DateOnly(2000, 1, 1)) });

        _calc.Compute(husband, wife, graph).DisplayName.ShouldBe("дружина");
    }

    [Fact]
    public void Divorced_marriage_is_former_spouse()
    {
        var husband = Make("Ч", Gender.Male);
        var wife = Make("Ж", Gender.Female);
        var graph = new FamilyGraph(
            new[] { husband, wife },
            Array.Empty<ParentChildLink>(),
            new[] { SpouseLink.Create(husband.Id, wife.Id, new DateOnly(2000, 1, 1), new DateOnly(2010, 1, 1)) });

        _calc.Compute(husband, wife, graph).DisplayName.ShouldBe("колишня дружина");
        _calc.Compute(wife, husband, graph).DisplayName.ShouldBe("колишній чоловік");
    }
}
