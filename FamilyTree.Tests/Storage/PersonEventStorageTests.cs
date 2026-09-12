using System.IO;
using System.Text.Json;
using FamilyTree.Domain;
using FamilyTree.Storage;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Storage;

/// <summary>
/// Події особи у файлі (схема v3): міграція з плоских полів v2, вкладений запис,
/// інваріант «порожньої події не буває» і транзит незнайомих полів усередині події.
/// </summary>
public sealed class PersonEventStorageTests : IDisposable
{
    private const string PersonId = "11111111-1111-1111-1111-111111111111";

    private readonly string _dir;

    public PersonEventStorageTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ftevents_" + Guid.NewGuid().ToString("N"));
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

    private async Task<FamilyDocument> LoadRawAsync(string json, string name = "in.familytree")
    {
        var path = PathFor(name);
        await File.WriteAllTextAsync(path, json);
        return await new JsonFamilyStorage("0.0.1").LoadAsync(path);
    }

    /// <summary>Файл схеми v2 з довільним «тілом» особи (плоскі поля подій).</summary>
    private static string V2File(string personBody) => $$"""
    {
      "schemaVersion": 2,
      "meta": { "title": "v2", "createdAt": "2026-01-01T00:00:00Z", "updatedAt": "2026-01-01T00:00:00Z" },
      "persons": [
        {
          "id": "{{PersonId}}",
          "lastName": "Коваль",
          "firstName": "Іван",
          "gender": "Male",
          {{personBody}}
          "createdAt": "2026-01-01T00:00:00Z",
          "updatedAt": "2026-01-01T00:00:00Z"
        }
      ],
      "parentChildLinks": [],
      "spouseLinks": []
    }
    """;

    [Fact]
    public async Task V2_flat_fields_migrate_into_events()
    {
        var document = await LoadRawAsync(V2File("""
          "birthDate": { "kind": "exact", "y": 1900, "m": 1, "d": 2 },
          "birthPlace": "Ковалівка",
          "birthNote": "за метричною книгою",
          "deathDate": { "kind": "exact", "y": 1975, "m": 3, "d": 4 },
          "deathPlace": "Чернівці",
          "deathNote": "помер удома",
        """));

        var person = document.Persons.ShouldHaveSingleItem();

        person.Birth.ShouldNotBeNull();
        (person.Birth?.Date).ShouldBe(FamilyDate.Exact(new DateOnly(1900, 1, 2)));
        (person.Birth?.Place).ShouldBe("Ковалівка");
        (person.Birth?.Note).ShouldBe("за метричною книгою");

        person.Death.ShouldNotBeNull();
        (person.Death?.Date).ShouldBe(FamilyDate.Exact(new DateOnly(1975, 3, 4)));
        (person.Death?.Place).ShouldBe("Чернівці");
        (person.Death?.Note).ShouldBe("помер удома");

        person.IsAlive.ShouldBeFalse();
    }

    [Fact]
    public async Task V2_person_without_event_fields_gets_no_events()
    {
        var document = await LoadRawAsync(V2File(string.Empty));

        var person = document.Persons.ShouldHaveSingleItem();
        person.Birth.ShouldBeNull();
        person.Death.ShouldBeNull();
        person.IsAlive.ShouldBeTrue();
    }

    [Fact]
    public async Task V2_whitespace_only_place_does_not_create_an_event()
    {
        // Порожньої події не буває: пробіл — це не дані (інваріант PersonEvent).
        var document = await LoadRawAsync(V2File("""
          "birthPlace": "   ",
        """));

        document.Persons.ShouldHaveSingleItem().Birth.ShouldBeNull();
    }

    [Fact]
    public async Task V2_death_without_date_keeps_both_place_and_deceased()
    {
        // Найтонший випадок міграції: «помер, дата невідома, але місце знаємо».
        // Прапорець живе на особі, а не в події, — і мусить пережити перенесення.
        var document = await LoadRawAsync(V2File("""
          "deathPlace": "Чернівці",
          "deceased": true,
        """));

        var person = document.Persons.ShouldHaveSingleItem();
        (person.Death?.Place).ShouldBe("Чернівці");
        (person.Death?.Date).ShouldBeNull();
        person.Deceased.ShouldBeTrue();
        person.IsAlive.ShouldBeFalse();
    }

    [Fact]
    public async Task Saved_file_is_v3_with_nested_events_and_no_flat_fields()
    {
        var storage = new JsonFamilyStorage("0.0.1");
        var path = PathFor("out.familytree");

        var document = FamilyDocument.CreateNew("Тест");
        document.Persons.Add(new Person
        {
            LastName = "Коваль",
            FirstName = "Іван",
            Gender = Gender.Male,
            Birth = PersonEvent.Create(new DateOnly(1900, 1, 2), "Ковалівка", "за метричною книгою"),
        });

        await storage.SaveAsync(document, path);

        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        saved.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(3);

        var person = saved.RootElement.GetProperty("persons")[0];
        person.GetProperty("birth").GetProperty("place").GetString().ShouldBe("Ковалівка");
        person.GetProperty("birth").GetProperty("note").GetString().ShouldBe("за метричною книгою");
        person.GetProperty("birth").TryGetProperty("date", out _).ShouldBeTrue();

        // Плоских полів більше немає — інакше вони возилися б як «незнайомі» вічно.
        person.TryGetProperty("birthPlace", out _).ShouldBeFalse();
        person.TryGetProperty("birthDate", out _).ShouldBeFalse();

        // Події, про яку нічого не відомо, у файлі немає взагалі.
        person.TryGetProperty("death", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Unknown_fields_inside_an_event_survive_open_and_save()
    {
        // Вкладення подій не мало пробити дірку в гарантії [JsonExtensionData]:
        // до v3 невідоме поле лежало прямо в особі й зберігалося.
        var storage = new JsonFamilyStorage("0.0.1");
        var path = PathFor("nested.familytree");

        await File.WriteAllTextAsync(path, $$"""
        {
          "schemaVersion": 3,
          "meta": { "title": "v3", "createdAt": "2026-01-01T00:00:00Z", "updatedAt": "2026-01-01T00:00:00Z" },
          "persons": [
            {
              "id": "{{PersonId}}",
              "lastName": "Коваль",
              "firstName": "Іван",
              "gender": "Male",
              "birth": {
                "date": { "kind": "exact", "y": 1900, "m": 1, "d": 2 },
                "place": "Ковалівка",
                "futureEventField": { "source": "метрична книга", "page": 17 }
              },
              "createdAt": "2026-01-01T00:00:00Z",
              "updatedAt": "2026-01-01T00:00:00Z"
            }
          ],
          "parentChildLinks": [],
          "spouseLinks": []
        }
        """);

        var document = await storage.LoadAsync(path);
        await storage.SaveAsync(document, path);

        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var birth = saved.RootElement.GetProperty("persons")[0].GetProperty("birth");
        birth.GetProperty("place").GetString().ShouldBe("Ковалівка");
        birth.GetProperty("futureEventField").GetProperty("page").GetInt32().ShouldBe(17);
    }

    [Fact]
    public async Task An_event_that_is_only_unknown_fields_is_not_dropped()
    {
        // Крайовий випадок попереднього: усе знайоме в події порожнє, але сама подія
        // у файлі БУЛА. Викинути її означало б стерти дані новішої збірки.
        var storage = new JsonFamilyStorage("0.0.1");
        var path = PathFor("onlyextra.familytree");

        await File.WriteAllTextAsync(path, $$"""
        {
          "schemaVersion": 3,
          "meta": { "title": "v3", "createdAt": "2026-01-01T00:00:00Z", "updatedAt": "2026-01-01T00:00:00Z" },
          "persons": [
            {
              "id": "{{PersonId}}",
              "lastName": "Коваль",
              "firstName": "Іван",
              "gender": "Male",
              "death": { "futureEventField": "щось про смерть" },
              "createdAt": "2026-01-01T00:00:00Z",
              "updatedAt": "2026-01-01T00:00:00Z"
            }
          ],
          "parentChildLinks": [],
          "spouseLinks": []
        }
        """);

        var document = await storage.LoadAsync(path);

        // У домені події немає — знайомих даних у ній нуль.
        document.Persons.ShouldHaveSingleItem().Death.ShouldBeNull();

        await storage.SaveAsync(document, path);

        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        saved.RootElement.GetProperty("persons")[0]
            .GetProperty("death").GetProperty("futureEventField").GetString()
            .ShouldBe("щось про смерть");
    }

    [Fact]
    public async Task Migration_does_not_overwrite_an_event_object_that_is_already_there()
    {
        // Гібрид після ручної правки: є і вкладений об'єкт, і плоскі поля. Вкладений —
        // головніший; плоскі прибираються, щоб не возитися вічно як незнайомі.
        var document = await LoadRawAsync(V2File("""
          "birth": { "place": "Ковалівка" },
          "birthPlace": "Полтава",
        """));

        var person = document.Persons.ShouldHaveSingleItem();
        (person.Birth?.Place).ShouldBe("Ковалівка");
    }
}
