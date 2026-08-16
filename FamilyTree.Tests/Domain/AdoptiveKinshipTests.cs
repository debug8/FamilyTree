using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// B-17: прийомні/зведені зв'язки (<see cref="ParentRole.Adoptive"/>/<see cref="ParentRole.Step"/>)
/// не є кровними, тож розрахунок спорідненості не має приписувати їм кровні назви.
/// Показ у дереві та вкладці «Особа» такі зв'язки зберігає (перевірка навігації — нижче).
/// </summary>
public class AdoptiveKinshipTests
{
    private static Person Make(string name, Gender g = Gender.Unknown) => new() { LastName = name, FirstName = name, Gender = g };

    private static ParentChildLink Pc(Person parent, Person child, ParentRole role) =>
        new() { ParentId = parent.Id, ChildId = child.Id, ParentRole = role };

    private readonly KinshipCalculator _calc = new(new CommonAncestorFinder(), new UkrainianKinshipFormatter());

    [Fact]
    public void Adoptive_parent_is_not_a_blood_ancestor_or_descendant()
    {
        var parent = Make("П", Gender.Male);
        var child = Make("Д", Gender.Male);
        var graph = new FamilyGraph(
            new[] { parent, child },
            new[] { Pc(parent, child, ParentRole.Adoptive) },
            Array.Empty<SpouseLink>());

        _calc.Compute(child, parent, graph).Kind.ShouldBe(KinshipKind.None);
        _calc.Compute(parent, child, graph).Kind.ShouldBe(KinshipKind.None);
    }

    [Fact]
    public void Two_adopted_children_of_same_parent_are_not_blood_siblings()
    {
        // Раніше спільний «батько» (навіть прийомний) давав «єдинокровний брат» — тобто
        // застосунок стверджував спільну кров, якої немає.
        var parent = Make("П", Gender.Female);
        var a = Make("A", Gender.Male);
        var b = Make("B", Gender.Female);
        var graph = new FamilyGraph(
            new[] { parent, a, b },
            new[] { Pc(parent, a, ParentRole.Adoptive), Pc(parent, b, ParentRole.Adoptive) },
            Array.Empty<SpouseLink>());

        _calc.Compute(a, b, graph).Kind.ShouldBe(KinshipKind.None);
    }

    [Fact]
    public void Step_child_of_same_parent_is_not_a_blood_sibling()
    {
        // Одна дитина рідна, друга — зведена (Step): спільної крові немає.
        var parent = Make("П", Gender.Male);
        var bioChild = Make("Рідна", Gender.Female);
        var stepChild = Make("Зведена", Gender.Female);
        var graph = new FamilyGraph(
            new[] { parent, bioChild, stepChild },
            new[] { Pc(parent, bioChild, ParentRole.Biological), Pc(parent, stepChild, ParentRole.Step) },
            Array.Empty<SpouseLink>());

        _calc.Compute(bioChild, stepChild, graph).Kind.ShouldBe(KinshipKind.None);
    }

    [Fact]
    public void Adoptive_parents_own_kin_is_not_a_blood_relative()
    {
        // Рідний батько прийомного батька не є прадідом прийомної дитини.
        var grandParent = Make("Дід", Gender.Male);
        var parent = Make("Прийомний батько", Gender.Male);
        var child = Make("Прийомна дитина", Gender.Female);
        var graph = new FamilyGraph(
            new[] { grandParent, parent, child },
            new[]
            {
                Pc(grandParent, parent, ParentRole.Biological),
                Pc(parent, child, ParentRole.Adoptive),
            },
            Array.Empty<SpouseLink>());

        _calc.Compute(child, grandParent, graph).Kind.ShouldBe(KinshipKind.None);
    }

    [Fact]
    public void Biological_relationship_still_resolves()
    {
        var father = Make("Батько", Gender.Male);
        var child = Make("Син", Gender.Male);
        var graph = new FamilyGraph(
            new[] { father, child },
            new[] { Pc(father, child, ParentRole.Biological) },
            Array.Empty<SpouseLink>());

        _calc.Compute(child, father, graph).DisplayName.ShouldBe("батько");
        _calc.Compute(father, child, graph).DisplayName.ShouldBe("син");
    }

    [Fact]
    public void GetBloodParents_excludes_adoptive_while_GetParents_keeps_it()
    {
        // Показ (GetParents) містить обох; кровний обхід (GetBloodParents) — лише рідного.
        var bio = Make("Біо", Gender.Female);
        var adoptive = Make("Прийомний", Gender.Male);
        var child = Make("Дитина");
        var graph = new FamilyGraph(
            new[] { bio, adoptive, child },
            new[] { Pc(bio, child, ParentRole.Biological), Pc(adoptive, child, ParentRole.Adoptive) },
            Array.Empty<SpouseLink>());

        graph.GetParents(child.Id).Select(p => p.Id).ShouldBe(new[] { bio.Id, adoptive.Id }, ignoreOrder: true);
        graph.GetBloodParents(child.Id).Select(p => p.Id).ShouldBe(new[] { bio.Id });
    }
}
