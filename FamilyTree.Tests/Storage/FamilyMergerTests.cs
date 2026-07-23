using FamilyTree.Domain;
using FamilyTree.Storage;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Storage;

/// <summary>
/// T-5.1 — злиття документів родини. Перевіряємо, що файл без перетинів додається
/// повністю, повторний імпорт того самого файлу нічого не дублює, а збіг ПІБ+дати
/// народження зливається в наявну особу з переприв'язкою зв'язків.
/// </summary>
public class FamilyMergerTests
{
    private readonly FamilyMerger _merger = new();

    private static Person Make(string last, string first, int year, Gender g = Gender.Male) => new()
    {
        LastName = last,
        FirstName = first,
        Gender = g,
        BirthDate = new DateOnly(year, 1, 1),
    };

    private static FamilyDocument DocOf(params Person[] persons)
    {
        var doc = FamilyDocument.CreateNew("test");
        doc.Persons.AddRange(persons);
        return doc;
    }

    [Fact]
    public void Non_overlapping_import_adds_all_records()
    {
        var father = Make("Коваленко", "Іван", 1950);
        var son = Make("Коваленко", "Петро", 1980);
        var source = DocOf(father, son);
        source.ParentChildLinks.Add(new ParentChildLink { ParentId = father.Id, ChildId = son.Id });
        source.SpouseLinks.Add(SpouseLink.Create(father.Id, son.Id, new DateOnly(1975, 5, 5)));

        var target = FamilyDocument.CreateNew("mine");
        var report = _merger.Merge(target, source);

        report.AddedPersons.ShouldBe(2);
        report.DuplicatePersons.ShouldBe(0);
        target.Persons.Count.ShouldBe(2);
        target.ParentChildLinks.Count.ShouldBe(1);
        target.SpouseLinks.Count.ShouldBe(1);
    }

    [Fact]
    public void Importing_same_file_twice_creates_no_duplicates()
    {
        var father = Make("Коваленко", "Іван", 1950);
        var son = Make("Коваленко", "Петро", 1980);
        var source = DocOf(father, son);
        source.ParentChildLinks.Add(new ParentChildLink { ParentId = father.Id, ChildId = son.Id });

        var target = FamilyDocument.CreateNew("mine");
        _merger.Merge(target, source);
        var second = _merger.Merge(target, source);

        second.AddedPersons.ShouldBe(0);
        second.DuplicatePersons.ShouldBe(2);
        target.Persons.Count.ShouldBe(2);
        target.ParentChildLinks.Count.ShouldBe(1); // зв'язок не продубльовано
    }

    [Fact]
    public void Duplicate_by_name_and_birthdate_is_merged_and_links_remapped()
    {
        // Наявна особа й «та сама» з іншого файлу (інший Id, той самий ПІБ+дата).
        var existing = Make("Шевченко", "Ольга", 1990, Gender.Female);
        var target = DocOf(existing);

        var sameOlha = Make("Шевченко", "Ольга", 1990, Gender.Female); // інший Id
        var child = Make("Шевченко", "Мала", 2015, Gender.Female);
        var source = DocOf(sameOlha, child);
        source.ParentChildLinks.Add(new ParentChildLink { ParentId = sameOlha.Id, ChildId = child.Id });

        var report = _merger.Merge(target, source);

        report.AddedPersons.ShouldBe(1);       // лише дитина
        report.DuplicatePersons.ShouldBe(1);   // Ольга злита
        target.Persons.Count.ShouldBe(2);

        // Зв'язок має вести від НАЯВНОЇ Ольги (її Id), а не від імпортованої.
        target.ParentChildLinks.ShouldHaveSingleItem();
        target.ParentChildLinks[0].ParentId.ShouldBe(existing.Id);
    }

    [Fact]
    public void Same_name_without_birthdate_is_not_auto_merged()
    {
        var existing = new Person { LastName = "Мороз", FirstName = "Юрій", Gender = Gender.Male };
        var target = DocOf(existing);

        var other = new Person { LastName = "Мороз", FirstName = "Юрій", Gender = Gender.Male };
        var source = DocOf(other);

        var report = _merger.Merge(target, source);

        report.AddedPersons.ShouldBe(1);      // без дати народження не зливаємо
        report.DuplicatePersons.ShouldBe(0);
        target.Persons.Count.ShouldBe(2);
    }
}
