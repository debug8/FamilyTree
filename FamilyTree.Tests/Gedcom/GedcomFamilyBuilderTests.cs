using FamilyTree.Domain;
using FamilyTree.Gedcom;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Gedcom;

/// <summary>
/// Виведення записів FAM з ребер документа (T-5.2, Частина 2) — крайні випадки,
/// на яких ця задача й ламається.
/// </summary>
public sealed class GedcomFamilyBuilderTests
{
    [Fact]
    public void Couple_with_child_gives_one_family()
    {
        var doc = new GedcomTestDocument();
        var father = doc.Add("Коваленко", "Іван", Gender.Male);
        var mother = doc.Add("Коваленко", "Марія", Gender.Female);
        var child = doc.Add("Коваленко", "Оксана", Gender.Female);
        doc.Parent(father, child);
        doc.Parent(mother, child);

        var model = GedcomFamilyBuilder.Build(doc.Document);

        var family = model.Families.ShouldHaveSingleItem();
        family.HusbandId.ShouldBe(father.Id);
        family.WifeId.ShouldBe(mother.Id);
        family.ChildIds.ShouldBe(new[] { child.Id });
        family.Marriages.ShouldBeEmpty();
    }

    [Fact]
    public void Marriage_joins_the_existing_family_instead_of_making_a_second()
    {
        var doc = new GedcomTestDocument();
        var father = doc.Add("Коваленко", "Іван", Gender.Male);
        var mother = doc.Add("Коваленко", "Марія", Gender.Female);
        var child = doc.Add("Коваленко", "Оксана", Gender.Female);
        doc.Parent(father, child);
        doc.Parent(mother, child);
        doc.Marry(father, mother, FamilyDate.Exact(new DateOnly(1975, 6, 1)));

        var model = GedcomFamilyBuilder.Build(doc.Document);

        var family = model.Families.ShouldHaveSingleItem();
        family.ChildIds.ShouldHaveSingleItem();
        family.Marriages.ShouldHaveSingleItem();
    }

    [Fact]
    public void Childless_couple_gets_its_own_family()
    {
        var doc = new GedcomTestDocument();
        var husband = doc.Add("Мороз", "Петро", Gender.Male);
        var wife = doc.Add("Мороз", "Ганна", Gender.Female);
        doc.Marry(husband, wife);

        var family = GedcomFamilyBuilder.Build(doc.Document).Families.ShouldHaveSingleItem();

        family.ChildIds.ShouldBeEmpty();
        family.Marriages.ShouldHaveSingleItem();
    }

    [Fact]
    public void Unmarried_co_parents_still_form_a_family()
    {
        // FAM у GEDCOM не означає шлюб: спільна дитина — уже підстава для запису.
        var doc = new GedcomTestDocument();
        var father = doc.Add("Бондар", "Остап", Gender.Male);
        var mother = doc.Add("Лисенко", "Віра", Gender.Female);
        var child = doc.Add("Бондар", "Юрко", Gender.Male);
        doc.Parent(father, child);
        doc.Parent(mother, child);

        var family = GedcomFamilyBuilder.Build(doc.Document).Families.ShouldHaveSingleItem();

        family.HusbandId.ShouldBe(father.Id);
        family.WifeId.ShouldBe(mother.Id);
        family.Marriages.ShouldBeEmpty();
    }

    [Fact]
    public void Single_known_parent_leaves_the_other_slot_empty()
    {
        var doc = new GedcomTestDocument();
        var mother = doc.Add("Гриценко", "Оксана", Gender.Female);
        var child = doc.Add("Гриценко", "Влас", Gender.Male);
        doc.Parent(mother, child);

        var family = GedcomFamilyBuilder.Build(doc.Document).Families.ShouldHaveSingleItem();

        family.HusbandId.ShouldBeNull();
        family.WifeId.ShouldBe(mother.Id);
    }

    [Fact]
    public void Adoption_makes_a_second_family_with_its_own_role()
    {
        var doc = new GedcomTestDocument();
        var bioFather = doc.Add("Аденко", "Іван", Gender.Male);
        var bioMother = doc.Add("Аденко", "Ніна", Gender.Female);
        var adoptiveFather = doc.Add("Беденко", "Степан", Gender.Male);
        var adoptiveMother = doc.Add("Беденко", "Ольга", Gender.Female);
        var child = doc.Add("Беденко", "Марко", Gender.Male);

        doc.Parent(bioFather, child);
        doc.Parent(bioMother, child);
        doc.Parent(adoptiveFather, child, ParentRole.Adoptive);
        doc.Parent(adoptiveMother, child, ParentRole.Adoptive);
        doc.Marry(adoptiveFather, adoptiveMother);

        var model = GedcomFamilyBuilder.Build(doc.Document);

        model.Families.Count.ShouldBe(2);

        var biological = model.Families.Single(f => f.Role == ParentRole.Biological);
        biological.HusbandId.ShouldBe(bioFather.Id);

        var adoptive = model.Families.Single(f => f.Role == ParentRole.Adoptive);
        adoptive.HusbandId.ShouldBe(adoptiveFather.Id);

        // Шлюб усиновлювачів чіпляється до їхньої ж родини, а не породжує третю.
        adoptive.Marriages.ShouldHaveSingleItem();

        model.ChildFamilies(child.Id).Count.ShouldBe(2);
    }

    [Fact]
    public void Remarriage_of_the_same_pair_stays_one_family_with_two_events()
    {
        var doc = new GedcomTestDocument();
        var husband = doc.Add("Ткач", "Роман", Gender.Male);
        var wife = doc.Add("Ткач", "Леся", Gender.Female);

        doc.Marry(husband, wife, FamilyDate.Exact(new DateOnly(1990, 1, 1)), FamilyDate.Exact(new DateOnly(1995, 1, 1)));
        doc.Marry(husband, wife, FamilyDate.Exact(new DateOnly(2000, 1, 1)));

        var family = GedcomFamilyBuilder.Build(doc.Document).Families.ShouldHaveSingleItem();

        family.Marriages.Count.ShouldBe(2);
        family.Marriages[0].MarriageDate!.EffectiveYear.ShouldBe(1990);
        family.Marriages[1].MarriageDate!.EffectiveYear.ShouldBe(2000);
    }

    [Fact]
    public void Two_parents_of_unknown_gender_are_ordered_by_id()
    {
        var doc = new GedcomTestDocument();
        var firstAdded = doc.Add("Невідомко", "А", Gender.Unknown);
        var secondAdded = doc.Add("Невідомко", "Б", Gender.Unknown);
        var child = doc.Add("Невідомко", "В", Gender.Unknown);
        doc.Parent(secondAdded, child);
        doc.Parent(firstAdded, child);

        var family = GedcomFamilyBuilder.Build(doc.Document).Families.ShouldHaveSingleItem();

        // Порядок ребер у документі не впливає — вирішує менший Id.
        family.HusbandId.ShouldBe(firstAdded.Id);
        family.WifeId.ShouldBe(secondAdded.Id);
    }

    [Fact]
    public void Person_without_links_belongs_to_no_family()
    {
        var doc = new GedcomTestDocument();
        var loner = doc.Add("Самітник", "Богдан", Gender.Male);

        var model = GedcomFamilyBuilder.Build(doc.Document);

        model.Families.ShouldBeEmpty();
        model.SpouseFamilies(loner.Id).ShouldBeEmpty();
        model.ChildFamilies(loner.Id).ShouldBeEmpty();
    }

    [Fact]
    public void Xrefs_are_assigned_by_name_order()
    {
        var doc = new GedcomTestDocument();
        var second = doc.Add("Яценко", "Андрій", Gender.Male);
        var first = doc.Add("Андрієнко", "Яна", Gender.Female);

        var model = GedcomFamilyBuilder.Build(doc.Document);

        model.XrefOf(first.Id).ShouldBe("I1");
        model.XrefOf(second.Id).ShouldBe("I2");
        model.Persons.Select(p => p.Id).ShouldBe(new[] { first.Id, second.Id });
    }

    [Fact]
    public void Links_to_missing_persons_are_ignored()
    {
        var doc = new GedcomTestDocument();
        var child = doc.Add("Сирота", "Тарас", Gender.Male);

        doc.Document.ParentChildLinks.Add(new ParentChildLink
        {
            ParentId = Guid.NewGuid(),
            ChildId = child.Id,
        });

        GedcomFamilyBuilder.Build(doc.Document).Families.ShouldBeEmpty();
    }
}
