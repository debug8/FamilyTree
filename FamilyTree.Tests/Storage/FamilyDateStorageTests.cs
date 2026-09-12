using System.IO;
using FamilyTree.Domain;
using FamilyTree.Storage;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Storage;

/// <summary>
/// Серіалізація неточних дат (формат v2) і міграція v1→v2 (T-5.2a, Частина 1b).
/// </summary>
public sealed class FamilyDateStorageTests : IDisposable
{
    private readonly string _dir;

    public FamilyDateStorageTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ftdates_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // прибирання best-effort
        }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    private async Task<FamilyDate?> RoundTripBirth(FamilyDate birth)
    {
        var storage = new JsonFamilyStorage();
        var path = PathFor(Guid.NewGuid().ToString("N") + ".familytree");

        var doc = FamilyDocument.CreateNew("t");
        doc.Persons.Add(new Person { LastName = "А", FirstName = "А", Gender = Gender.Male, Birth = PersonEvent.Create(birth) });

        await storage.SaveAsync(doc, path);
        var loaded = await storage.LoadAsync(path);
        loaded.RepairedIssues.ShouldBeEmpty();
        return loaded.Persons.Single().Birth?.Date;
    }

    [Fact]
    public async Task Exact_full_date_roundtrips()
    {
        var date = FamilyDate.Exact(new DateOnly(1980, 3, 14));
        (await RoundTripBirth(date)).ShouldBe(date);
    }

    [Fact]
    public async Task Partial_year_roundtrips_and_keeps_precision()
    {
        var date = FamilyDate.Exact(new DatePoint { Year = 1850 });
        var loaded = await RoundTripBirth(date);

        loaded.ShouldBe(date);
        loaded!.Start!.Precision.ShouldBe(DatePrecision.Year);
    }

    [Fact]
    public async Task Approximate_date_roundtrips()
    {
        var date = FamilyDate.Approximate(DateApproximation.About, new DatePoint { Year = 1850 });
        (await RoundTripBirth(date)).ShouldBe(date);
    }

    [Fact]
    public async Task Before_and_after_roundtrip()
    {
        var before = FamilyDate.Before(new DatePoint { Year = 1900 });
        (await RoundTripBirth(before)).ShouldBe(before);

        var after = FamilyDate.After(new DatePoint { Year = 1800, Month = 6 });
        (await RoundTripBirth(after)).ShouldBe(after);
    }

    [Fact]
    public async Task Between_range_roundtrips()
    {
        var date = FamilyDate.Between(new DatePoint { Year = 1980 }, new DatePoint { Year = 1985 });
        (await RoundTripBirth(date)).ShouldBe(date);
    }

    [Fact]
    public async Task Phrase_roundtrips()
    {
        var date = FamilyDate.FromPhrase("близько Різдва 1900");
        (await RoundTripBirth(date)).ShouldBe(date);
    }

    [Fact]
    public async Task Julian_calendar_roundtrips()
    {
        var date = FamilyDate.Exact(new DatePoint { Year = 1700, Month = 2, Day = 10, Calendar = DateCalendar.Julian });
        var loaded = await RoundTripBirth(date);

        loaded.ShouldBe(date);
        loaded!.Start!.Calendar.ShouldBe(DateCalendar.Julian);
    }

    [Fact]
    public async Task Original_gedcom_is_preserved()
    {
        var date = FamilyDate.Approximate(DateApproximation.About, new DatePoint { Year = 1850 })
            .WithOriginalGedcom("ABT 1850");
        var loaded = await RoundTripBirth(date);

        loaded.ShouldBe(date);
        loaded!.OriginalGedcom.ShouldBe("ABT 1850");
    }

    // ---- Міграція v1 → v2 ------------------------------------------------

    [Fact]
    public async Task V1_iso_dates_migrate_to_exact_silently()
    {
        // Якби міграція не спрацювала, рядок дати не десеріалізувався б у об'єкт FamilyDate
        // і LoadAsync кинув би MalformedJson. Тож успіх сам по собі доводить міграцію.
        var path = PathFor("v1.familytree");
        await File.WriteAllTextAsync(path,
            "{\"schemaVersion\":1,\"meta\":{\"title\":\"старий\"}," +
            "\"persons\":[{\"id\":\"11111111-1111-4111-8111-111111111111\",\"lastName\":\"Шевченко\"," +
            "\"firstName\":\"Тарас\",\"gender\":\"Male\",\"birthDate\":\"1814-03-09\",\"deathDate\":\"1861-03-10\"}," +
            "{\"id\":\"22222222-2222-4222-8222-222222222222\",\"lastName\":\"Шевченко\",\"firstName\":\"Оксана\",\"gender\":\"Female\"}]," +
            "\"spouseLinks\":[{\"person1Id\":\"11111111-1111-4111-8111-111111111111\"," +
            "\"person2Id\":\"22222222-2222-4222-8222-222222222222\",\"marriageDate\":\"1840-06-01\"}]}");

        var loaded = await new JsonFamilyStorage().LoadAsync(path);

        var taras = loaded.Persons.Single(p => p.FirstName == "Тарас");
        (taras.Birth?.Date).ShouldBe(FamilyDate.Exact(new DateOnly(1814, 3, 9)));
        (taras.Death?.Date).ShouldBe(FamilyDate.Exact(new DateOnly(1861, 3, 10)));
        loaded.SpouseLinks.Single().MarriageDate.ShouldBe(FamilyDate.Exact(new DateOnly(1840, 6, 1)));
        loaded.RepairedIssues.ShouldBeEmpty();
    }
}
