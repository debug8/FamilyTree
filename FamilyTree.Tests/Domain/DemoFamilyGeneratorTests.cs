using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;
using FamilyTree.Domain.Seeding;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Тести генератора демо-родини (T-5.5): детермінізм за насінням, дотримання стелі осіб
/// та поколінь, і головний критерій приймання — режим «Усі родичі» від запропонованого
/// кореня показує щонайменше 10 різних назв родства.
/// </summary>
public class DemoFamilyGeneratorTests
{
    private static DemoFamilyOptions Options(int? seed = 12345, int generations = 5, int maxPersons = 120) => new()
    {
        Generations = generations,
        MaxPersons = maxPersons,
        MaxChildrenPerCouple = 4,
        ComplexRelations = true,
        IncludeDivorces = true,
        Seed = seed,
    };

    [Fact]
    public void Same_seed_produces_identical_structure()
    {
        var a = DemoFamilyGenerator.Generate(Options());
        var b = DemoFamilyGenerator.Generate(Options());

        a.Persons.Count.ShouldBe(b.Persons.Count);
        a.ParentChildLinks.Count.ShouldBe(b.ParentChildLinks.Count);
        a.SpouseLinks.Count.ShouldBe(b.SpouseLinks.Count);

        // Ідентифікатори випадкові, але імена/стать/дати керуються насінням — мають збігатися.
        var shapeA = a.Persons.Select(p => (p.LastName, p.FirstName, p.Gender, p.BirthDate, p.DeathDate));
        var shapeB = b.Persons.Select(p => (p.LastName, p.FirstName, p.Gender, p.BirthDate, p.DeathDate));
        shapeA.ShouldBe(shapeB);
    }

    [Fact]
    public void Different_seed_changes_the_family()
    {
        var a = DemoFamilyGenerator.Generate(Options(seed: 1));
        var b = DemoFamilyGenerator.Generate(Options(seed: 2));

        var shapeA = a.Persons.Select(p => $"{p.FirstName} {p.Gender} {p.BirthDate}").ToList();
        var shapeB = b.Persons.Select(p => $"{p.FirstName} {p.Gender} {p.BirthDate}").ToList();
        shapeA.ShouldNotBe(shapeB);
    }

    [Fact]
    public void Respects_max_persons_cap()
    {
        var result = DemoFamilyGenerator.Generate(Options(maxPersons: 25));

        result.Persons.Count.ShouldBeLessThanOrEqualTo(25);
        result.Persons.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Out_of_range_options_are_clamped()
    {
        var result = DemoFamilyGenerator.Generate(new DemoFamilyOptions
        {
            Generations = 999,
            MaxPersons = 1,             // нижче мінімуму → підніметься до MinPersons
            MaxChildrenPerCouple = 0,
            Seed = 7,
        });

        // Має згенеруватися хоч якась родина, без винятків.
        result.Persons.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Links_reference_only_known_persons()
    {
        var result = DemoFamilyGenerator.Generate(Options());
        var ids = result.Persons.Select(p => p.Id).ToHashSet();

        foreach (var link in result.ParentChildLinks)
        {
            ids.ShouldContain(link.ParentId);
            ids.ShouldContain(link.ChildId);
            link.ParentId.ShouldNotBe(link.ChildId);
        }

        foreach (var spouse in result.SpouseLinks)
        {
            ids.ShouldContain(spouse.Person1Id);
            ids.ShouldContain(spouse.Person2Id);
        }
    }

    [Fact]
    public void Suggested_root_exists_and_has_ancestors_and_descendants()
    {
        var result = DemoFamilyGenerator.Generate(Options());

        result.SuggestedRootId.ShouldNotBeNull();
        var rootId = result.SuggestedRootId!.Value;
        result.Persons.ShouldContain(p => p.Id == rootId);

        result.ParentChildLinks.ShouldContain(l => l.ChildId == rootId);  // має батьків
        result.ParentChildLinks.ShouldContain(l => l.ParentId == rootId); // має дітей
    }

    [Fact]
    public void Full_relatives_view_shows_at_least_ten_distinct_relation_names()
    {
        var result = DemoFamilyGenerator.Generate(Options());
        var graph = new FamilyGraph(result.Persons, result.ParentChildLinks, result.SpouseLinks);
        var calc = new KinshipCalculator(new CommonAncestorFinder(), new UkrainianKinshipFormatter());

        var root = result.Persons.Single(p => p.Id == result.SuggestedRootId);

        var names = result.Persons
            .Where(p => p.Id != root.Id)
            .Select(p => calc.Compute(root, p, graph, includeAffinity: true))
            .Where(r => r.Kind != KinshipKind.None)
            .Select(r => r.DisplayName)
            .Distinct()
            .ToList();

        names.Count.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void Two_generations_still_produce_a_valid_family()
    {
        var result = DemoFamilyGenerator.Generate(Options(generations: 2, maxPersons: 40));

        result.Persons.Count.ShouldBeGreaterThan(0);
        // Двоє засновників + принаймні одна дитина.
        result.ParentChildLinks.Count.ShouldBeGreaterThan(0);
    }
}
