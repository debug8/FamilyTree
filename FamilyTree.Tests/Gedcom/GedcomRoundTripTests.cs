using FamilyTree.Domain;
using FamilyTree.Gedcom;
using FamilyTree.Storage;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Gedcom;

/// <summary>
/// Головний критерій T-5.2: експорт → імпорт повертає той самий документ.
/// Зіставлення осіб точне, бо ідентичність їде в <c>INDI._UID</c>.
/// </summary>
public sealed class GedcomRoundTripTests
{
    private static readonly DateTime Stamp = new(2026, 9, 3, 14, 5, 0, DateTimeKind.Utc);

    /// <summary>
    /// Родина, що зачіпає все, що вміє профіль: по батькові, дівоче прізвище, усі види дат,
    /// місце народження, нотатки, усиновлення, повторний шлюб, розлучення без дати,
    /// неодружені співбатьки, одинокий батько й особа без жодних ребер.
    /// </summary>
    private static GedcomTestDocument BuildFamily()
    {
        var doc = new GedcomTestDocument();

        var grandfather = doc.Add(
            "Коваленко", "Іван", Gender.Male,
            middleName: "Петрович",
            birth: FamilyDate.Exact(new DateOnly(1930, 3, 12)),
            death: FamilyDate.Approximate(DateApproximation.About, new DatePoint { Year = 2005 }),
            birthPlace: "Полтава",
            birthNote: "за метричною книгою",
            deathPlace: "Чернівці",
            deathNote: "помер удома\nпричина: запалення легень",
            notes: "коваль; нотатка з @ і\nдвома рядками");

        var grandmother = doc.Add(
            "Коваленко", "Марія", Gender.Female,
            maidenName: "Шевченко",
            birth: FamilyDate.Exact(new DatePoint { Year = 1932, Month = 5 }));

        var father = doc.Add(
            "Коваленко", "Андрій", Gender.Male,
            middleName: "Іванович",
            birth: FamilyDate.Between(new DatePoint { Year = 1955 }, new DatePoint { Year = 1957 }));

        var mother = doc.Add(
            "Лисенко", "Олена", Gender.Female,
            birth: FamilyDate.FromPhrase("за переказами, після війни"));

        var child = doc.Add(
            "Коваленко", "Оксана", Gender.Female,
            birth: FamilyDate.Before(new DatePoint { Year = 1985 }));

        var adopted = doc.Add("Коваленко", "Марко", Gender.Male);
        var singleMother = doc.Add("Гриценко", "Ніна", Gender.Female);
        var herChild = doc.Add("Гриценко", "Влас", Gender.Male);

        // Помер, дата невідома («1 DEAT Y») — окремий стан, який тримає Person.Deceased.
        doc.Add("Безвісний", "Степан", Gender.Male, deceased: true);

        doc.Add(
            "Самітник", "Богдан", Gender.Unknown,
            birth: FamilyDate.Exact(new DatePoint { Year = 1700, Month = 2, Day = 29, Calendar = DateCalendar.Julian }));

        // Подружжя з дитиною + повторний шлюб тієї самої пари.
        doc.Parent(grandfather, father);
        doc.Parent(grandmother, father);
        doc.Marry(grandfather, grandmother,
            FamilyDate.Exact(new DateOnly(1950, 6, 1)),
            FamilyDate.Exact(new DateOnly(1960, 2, 2)),
            marriagePlace: "Полтава");
        doc.Marry(grandfather, grandmother, FamilyDate.Exact(new DateOnly(1965, 8, 8)));

        // Неодружені співбатьки.
        doc.Parent(father, child);
        doc.Parent(mother, child);

        // Усиновлення подружжям, що вже має власну дитину.
        doc.Parent(father, adopted, ParentRole.Adoptive);
        doc.Parent(mother, adopted, ParentRole.Adoptive);

        // Одинока мати + розлучення без дати з окремим партнером.
        doc.Parent(singleMother, herChild);
        doc.Marry(singleMother, grandfather, marriage: null, divorce: null, divorced: true);

        return doc;
    }

    [Fact]
    public void Document_survives_export_and_import()
    {
        var original = BuildFamily().Document;

        var bytes = GedcomExporter.Export(original, "0.9.4", Stamp);
        var restored = GedcomImporter.Import(bytes, out var report);

        report.MalformedLines.ShouldBe(0);
        report.UnnamedPersons.ShouldBe(0);
        report.SkippedRecords.ShouldBe(0);
        report.SkippedTags.ShouldBeEmpty();
        report.RepairedIssues.ShouldBeEmpty();

        ShouldMatch(original, restored);
    }

    [Fact]
    public void Second_export_is_identical_to_the_first()
    {
        // Найсильніша перевірка: цикл експорт→імпорт→експорт не зсуває жодного байта.
        var original = BuildFamily().Document;

        var first = GedcomExporter.ExportText(original, "0.9.4", Stamp);
        var restored = GedcomImporter.Import(GedcomExporter.Export(original, "0.9.4", Stamp), out _);
        var second = GedcomExporter.ExportText(restored, "0.9.4", Stamp);

        second.ShouldBe(first);
    }

    [Fact]
    public void Unsupported_date_forms_survive_as_raw_text()
    {
        // Період FROM…TO модель не тримає, але сирий вираз доїжджає назад незмінним.
        var doc = new GedcomTestDocument();
        doc.Add(
            "Тест", "Особа", Gender.Male,
            birth: FamilyDate.FromPhrase("1750–1760").WithOriginalGedcom("FROM 1750 TO 1760"));

        var restored = GedcomImporter.Import(
            GedcomExporter.Export(doc.Document, "0.9.4", Stamp), out var report);

        var birth = (restored.Persons.Single().Birth?.Date).ShouldNotBeNull();
        birth.OriginalGedcom.ShouldBe("FROM 1750 TO 1760");
        report.TextOnlyDates.ShouldBe(1);

        // І при наступному експорті віддається дослівно.
        GedcomExporter.ExportText(restored, "0.9.4", Stamp).ShouldContain("FROM 1750 TO 1760");
    }

    private static void ShouldMatch(FamilyDocument original, FamilyDocument restored)
    {
        restored.Persons.Count.ShouldBe(original.Persons.Count);

        var byId = restored.Persons.ToDictionary(p => p.Id);

        foreach (var expected in original.Persons)
        {
            byId.ShouldContainKey(expected.Id);
            var actual = byId[expected.Id];

            actual.LastName.ShouldBe(expected.LastName);
            actual.FirstName.ShouldBe(expected.FirstName);
            actual.MiddleName.ShouldBe(expected.MiddleName);
            actual.MaidenName.ShouldBe(expected.MaidenName);
            actual.Gender.ShouldBe(expected.Gender);
            (actual.Birth?.Date).ShouldBe(expected.Birth?.Date);
            (actual.Birth?.Place).ShouldBe(expected.Birth?.Place);
            (actual.Birth?.Note).ShouldBe(expected.Birth?.Note);
            (actual.Death?.Date).ShouldBe(expected.Death?.Date);
            (actual.Death?.Place).ShouldBe(expected.Death?.Place);
            (actual.Death?.Note).ShouldBe(expected.Death?.Note);
            actual.Deceased.ShouldBe(expected.Deceased);
            actual.IsAlive.ShouldBe(expected.IsAlive);
            actual.Notes.ShouldBe(expected.Notes);
        }

        Parents(restored).ShouldBe(Parents(original), ignoreOrder: true);
        Spouses(restored).ShouldBe(Spouses(original), ignoreOrder: true);
    }

    private static IEnumerable<(Guid, Guid, ParentRole)> Parents(FamilyDocument document) =>
        document.ParentChildLinks.Select(l => (l.ParentId, l.ChildId, l.ParentRole)).ToList();

    private static IEnumerable<(Guid, Guid, FamilyDate?, string?, FamilyDate?, bool)> Spouses(FamilyDocument document) =>
        document.SpouseLinks
            .Select(l => (l.Person1Id, l.Person2Id, l.MarriageDate, l.MarriagePlace, l.DivorceDate, l.Divorced))
            .ToList();
}
