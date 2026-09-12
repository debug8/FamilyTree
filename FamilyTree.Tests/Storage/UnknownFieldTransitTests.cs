using System.IO;
using System.Text.Json;
using FamilyTree.Domain;
using FamilyTree.Storage;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Storage;

/// <summary>
/// Незнайомі поля файлу не губляться: файл, записаний НОВІШОЮ збіркою, відкритий цією і
/// збережений назад, мусить зберегти все, чого ця збірка не розуміє. Інакше
/// <c>.familytree</c>, надісланий родичу зі старішою версією, повертається обрізаним
/// без жодного попередження.
/// </summary>
public sealed class UnknownFieldTransitTests : IDisposable
{
    private const string FatherId = "11111111-1111-1111-1111-111111111111";
    private const string MotherId = "22222222-2222-2222-2222-222222222222";
    private const string ChildId = "33333333-3333-3333-3333-333333333333";
    private const string OrphanId = "44444444-4444-4444-4444-444444444444";
    private const string ParentLinkId = "55555555-5555-5555-5555-555555555555";
    private const string SpouseLinkId = "66666666-6666-6666-6666-666666666666";

    private readonly string _dir;

    public UnknownFieldTransitTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ftextras_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Файл «з майбутнього»: валідний v2 плюс поля, яких ця збірка не знає, — на кожному
    /// рівні (корінь, meta, особа, обидва види зв'язків) і різних типів JSON.
    /// </summary>
    private const string FileFromNewerBuild = $$"""
    {
      "schemaVersion": 2,
      "places": [ { "id": "pl-1", "name": "Ковалівка" } ],
      "meta": {
        "title": "Родина Ковалів",
        "createdAt": "2026-01-01T00:00:00Z",
        "updatedAt": "2026-01-01T00:00:00Z",
        "appVersion": "9.9.9",
        "futureMetaField": "мітка новішої збірки"
      },
      "persons": [
        {
          "id": "{{FatherId}}",
          "lastName": "Коваль",
          "firstName": "Іван",
          "gender": "Male",
          "burialPlace": "Ковалівка",
          "futureObject": { "n": 1, "list": [ true, null, "х" ] },
          "createdAt": "2026-01-01T00:00:00Z",
          "updatedAt": "2026-01-01T00:00:00Z"
        },
        {
          "id": "{{MotherId}}",
          "lastName": "Коваль",
          "firstName": "Марія",
          "gender": "Female",
          "createdAt": "2026-01-01T00:00:00Z",
          "updatedAt": "2026-01-01T00:00:00Z"
        },
        {
          "id": "{{ChildId}}",
          "lastName": "Коваль",
          "firstName": "Петро",
          "gender": "Male",
          "createdAt": "2026-01-01T00:00:00Z",
          "updatedAt": "2026-01-01T00:00:00Z"
        },
        {
          "id": "{{OrphanId}}",
          "lastName": "Зайвий",
          "firstName": "Запис",
          "gender": "Unknown",
          "futureOrphanField": "має зникнути разом з особою",
          "createdAt": "2026-01-01T00:00:00Z",
          "updatedAt": "2026-01-01T00:00:00Z"
        }
      ],
      "parentChildLinks": [
        {
          "id": "{{ParentLinkId}}",
          "parentId": "{{FatherId}}",
          "childId": "{{ChildId}}",
          "parentRole": "Biological",
          "futureLinkField": 42
        },
        {
          "parentId": "{{MotherId}}",
          "childId": "{{ChildId}}",
          "parentRole": "Biological"
        }
      ],
      "spouseLinks": [
        {
          "id": "{{SpouseLinkId}}",
          "person1Id": "{{FatherId}}",
          "person2Id": "{{MotherId}}",
          "futureSpouseField": "так"
        }
      ]
    }
    """;

    private static JsonElement PersonIn(JsonDocument file, string id) =>
        file.RootElement
            .GetProperty("persons")
            .EnumerateArray()
            .Single(p => p.GetProperty("id").GetString() == id);

    [Fact]
    public async Task Unknown_fields_survive_open_and_save()
    {
        var storage = new JsonFamilyStorage("0.0.1");
        var path = PathFor("newer.familytree");
        await File.WriteAllTextAsync(path, FileFromNewerBuild);

        var document = await storage.LoadAsync(path);
        await storage.SaveAsync(document, path);

        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        // 1. Ціла незнайома секція кореня — найгірший випадок утрати: без транзиту
        //    зник би цілий майбутній довідник місць.
        var places = saved.RootElement.GetProperty("places");
        places.GetArrayLength().ShouldBe(1);
        places[0].GetProperty("name").GetString().ShouldBe("Ковалівка");

        // 2. Незнайоме поле meta.
        saved.RootElement.GetProperty("meta").GetProperty("futureMetaField").GetString()
            .ShouldBe("мітка новішої збірки");

        // 3. Незнайомі поля особи — і скаляр, і вкладений об'єкт із масивом.
        var father = PersonIn(saved, FatherId);
        father.GetProperty("burialPlace").GetString().ShouldBe("Ковалівка");
        father.GetProperty("futureObject").GetProperty("n").GetInt32().ShouldBe(1);
        father.GetProperty("futureObject").GetProperty("list").GetArrayLength().ShouldBe(3);

        // Знайомі поля тієї ж особи не постраждали.
        father.GetProperty("firstName").GetString().ShouldBe("Іван");

        // 4. Незнайомі поля обох видів зв'язків.
        saved.RootElement.GetProperty("parentChildLinks")
            .EnumerateArray()
            .Single(l => l.TryGetProperty("futureLinkField", out _))
            .GetProperty("futureLinkField").GetInt32().ShouldBe(42);

        saved.RootElement.GetProperty("spouseLinks")[0]
            .GetProperty("futureSpouseField").GetString().ShouldBe("так");
    }

    [Fact]
    public async Task Unknown_fields_of_removed_person_are_not_written()
    {
        var storage = new JsonFamilyStorage("0.0.1");
        var path = PathFor("newer.familytree");
        await File.WriteAllTextAsync(path, FileFromNewerBuild);

        var document = await storage.LoadAsync(path);
        document.Persons.RemoveAll(p => p.Id == Guid.Parse(OrphanId));
        await storage.SaveAsync(document, path);

        var text = await File.ReadAllTextAsync(path);

        // Запис у словнику лишається (див. DocumentExtras), але у файл не потрапляє:
        // інакше видалена особа воскресала б у файлі самими своїми невідомими полями.
        text.ShouldNotContain("futureOrphanField");

        // Поля решти осіб на місці — прибралося саме те, що треба.
        text.ShouldContain("burialPlace");
    }

    [Fact]
    public async Task Link_without_id_keeps_its_unknown_fields()
    {
        // Зв'язок без "id" отримує НОВИЙ Guid у мапері. Якби транзит ключувався за Id
        // з файлу (Guid.Empty), поля такого зв'язку загубилися б мовчки.
        const string file = $$"""
        {
          "schemaVersion": 2,
          "meta": { "title": "Без Id", "createdAt": "2026-01-01T00:00:00Z", "updatedAt": "2026-01-01T00:00:00Z" },
          "persons": [
            { "id": "{{FatherId}}", "lastName": "Коваль", "firstName": "Іван", "gender": "Male",
              "createdAt": "2026-01-01T00:00:00Z", "updatedAt": "2026-01-01T00:00:00Z" },
            { "id": "{{MotherId}}", "lastName": "Коваль", "firstName": "Марія", "gender": "Female",
              "createdAt": "2026-01-01T00:00:00Z", "updatedAt": "2026-01-01T00:00:00Z" }
          ],
          "parentChildLinks": [],
          "spouseLinks": [
            { "person1Id": "{{FatherId}}", "person2Id": "{{MotherId}}", "futureSpouseField": "так" }
          ]
        }
        """;

        var storage = new JsonFamilyStorage("0.0.1");
        var path = PathFor("noid.familytree");
        await File.WriteAllTextAsync(path, file);

        var document = await storage.LoadAsync(path);
        document.SpouseLinks[0].Id.ShouldNotBe(Guid.Empty);
        await storage.SaveAsync(document, path);

        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        saved.RootElement.GetProperty("spouseLinks")[0]
            .GetProperty("futureSpouseField").GetString().ShouldBe("так");
    }

    [Fact]
    public async Task Own_file_produces_no_unknown_fields()
    {
        // Канарейка: у файлі, записаному ЦІЄЮ збіркою, незнайомих полів бути не може.
        // Якщо тест почервонів — якесь поле DTO перестало збігатися з ключем у файлі,
        // і з цього моменту воно возиться як «невідоме» замість того, щоб читатися.
        var storage = new JsonFamilyStorage("0.0.1");
        var path = PathFor("own.familytree");

        var document = FamilyDocument.CreateNew("Своя родина");
        var father = new Person
        {
            LastName = "Коваль",
            FirstName = "Іван",
            Gender = Gender.Male,
            Birth = PersonEvent.Create(new DateOnly(1900, 1, 1), "Ковалівка"),
            Death = PersonEvent.Create(null, "Чернівці"),
            Deceased = true,
            Notes = "нотатка",
        };
        var child = new Person { LastName = "Коваль", FirstName = "Петро", Gender = Gender.Male };
        father.Facts.Add(new PersonFact { Kind = PersonFactKind.Occupation, Value = "коваль" });

        document.Persons.Add(father);
        document.Persons.Add(child);
        document.ParentChildLinks.Add(new ParentChildLink { ParentId = father.Id, ChildId = child.Id });

        await storage.SaveAsync(document, path);
        var loaded = await storage.LoadAsync(path);

        loaded.Extras.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task Merge_keeps_unknown_fields_of_the_target()
    {
        // Свідоме рішення: транзит належить документу-ЦІЛІ (саме він переживе збереження),
        // поля документа-джерела втрачаються. Тест фіксує обидві половини.
        var storage = new JsonFamilyStorage("0.0.1");
        var targetPath = PathFor("target.familytree");
        await File.WriteAllTextAsync(targetPath, FileFromNewerBuild);

        var target = await storage.LoadAsync(targetPath);

        var source = FamilyDocument.CreateNew("Джерело");
        source.Persons.Add(new Person { LastName = "Новий", FirstName = "Родич", Gender = Gender.Male });

        new FamilyMerger().Merge(target, source);
        await storage.SaveAsync(target, targetPath);

        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(targetPath));
        saved.RootElement.GetProperty("places").GetArrayLength().ShouldBe(1);
        PersonIn(saved, FatherId).GetProperty("burialPlace").GetString().ShouldBe("Ковалівка");
    }
}
