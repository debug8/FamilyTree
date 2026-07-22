using System.IO;
using FamilyTree.Domain;
using FamilyTree.Storage;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Storage;

public sealed class JsonFamilyStorageTests : IDisposable
{
    private readonly string _dir;

    public JsonFamilyStorageTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fttests_" + Guid.NewGuid().ToString("N"));
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

    private static FamilyDocument BuildSampleDocument()
    {
        var doc = FamilyDocument.CreateNew("Родина Шевченків");

        var father = new Person
        {
            LastName = "Шевченко",
            FirstName = "Тарас",
            MiddleName = "Григорович",
            Gender = Gender.Male,
            BirthDate = new DateOnly(1814, 3, 9),
            DeathDate = new DateOnly(1861, 3, 10),
            Notes = "поет",
        };
        var child = new Person
        {
            LastName = "Шевченко",
            FirstName = "Оксана",
            Gender = Gender.Female,
        };

        doc.Persons.Add(father);
        doc.Persons.Add(child);
        doc.ParentChildLinks.Add(new ParentChildLink { ParentId = father.Id, ChildId = child.Id });
        doc.SpouseLinks.Add(SpouseLink.Create(father.Id, child.Id, new DateOnly(1840, 6, 1)));
        return doc;
    }

    [Fact]
    public async Task Save_then_load_roundtrips_document()
    {
        var storage = new JsonFamilyStorage();
        var path = PathFor("family.familytree");
        var original = BuildSampleDocument();
        var father = original.Persons[0];

        await storage.SaveAsync(original, path);
        var loaded = await storage.LoadAsync(path);

        loaded.Meta.Title.ShouldBe("Родина Шевченків");
        loaded.Persons.Count.ShouldBe(2);
        loaded.ParentChildLinks.Count.ShouldBe(1);
        loaded.SpouseLinks.Count.ShouldBe(1);

        var loadedFather = loaded.Persons.Single(p => p.Id == father.Id);
        loadedFather.LastName.ShouldBe("Шевченко");
        loadedFather.FirstName.ShouldBe("Тарас");
        loadedFather.MiddleName.ShouldBe("Григорович");
        loadedFather.Gender.ShouldBe(Gender.Male);
        loadedFather.BirthDate.ShouldBe(new DateOnly(1814, 3, 9));
        loadedFather.DeathDate.ShouldBe(new DateOnly(1861, 3, 10));
        loadedFather.IsAlive.ShouldBeFalse();

        loaded.SpouseLinks[0].MarriageDate.ShouldBe(new DateOnly(1840, 6, 1));
        original.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task Interrupted_save_does_not_corrupt_existing_file()
    {
        var storage = new JsonFamilyStorage();
        var path = PathFor("family.familytree");
        var doc = BuildSampleDocument();

        await storage.SaveAsync(doc, path);
        var originalContent = await File.ReadAllTextAsync(path);

        // Симулюємо збій після запису temp, але до заміни цільового файлу.
        doc.Persons.Add(new Person { LastName = "Новий", FirstName = "Запис", Gender = Gender.Unknown });
        storage.FaultBeforePromote = () => throw new IOException("симульований збій");

        await Should.ThrowAsync<IOException>(async () => await storage.SaveAsync(doc, path));

        // Наявний файл недоторканий, temp прибрано.
        (await File.ReadAllTextAsync(path)).ShouldBe(originalContent);
        File.Exists(path + ".tmp").ShouldBeFalse();

        storage.FaultBeforePromote = null;
        var reloaded = await storage.LoadAsync(path);
        reloaded.Persons.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Load_file_with_newer_schema_throws_clear_error()
    {
        var path = PathFor("future.familytree");
        await File.WriteAllTextAsync(path,
            "{\"schemaVersion\":99,\"meta\":{},\"persons\":[],\"parentChildLinks\":[],\"spouseLinks\":[]}");

        var storage = new JsonFamilyStorage();

        var ex = await Should.ThrowAsync<UnsupportedSchemaVersionException>(
            async () => await storage.LoadAsync(path));
        ex.FileVersion.ShouldBe(99);
        ex.SupportedVersion.ShouldBe(JsonFamilyStorage.CurrentSchemaVersion);
    }

    [Fact]
    public async Task Seven_saves_keep_exactly_five_backups()
    {
        var storage = new JsonFamilyStorage();
        var path = PathFor("rotating.familytree");
        var doc = BuildSampleDocument();

        for (var i = 0; i < 7; i++)
        {
            doc.Meta.Title = $"версія {i}";
            await storage.SaveAsync(doc, path);
        }

        var backupsDir = Path.Combine(_dir, ".backups");
        Directory.GetFiles(backupsDir, "rotating.familytree.*.bak").Length.ShouldBe(5);
    }
}
