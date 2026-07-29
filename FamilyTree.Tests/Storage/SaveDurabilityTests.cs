using System.IO;
using System.Text;
using FamilyTree.Domain;
using FamilyTree.Storage;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Storage;

/// <summary>
/// Надійність шляху запису: паралельні збереження, нумеровані резервні копії,
/// відсутність escape-послідовностей у кирилиці, поведінка при гонках із файловою
/// системою. Раніше тут були: фіксоване ім'я temp (два збереження обрізали дані
/// одне одного), <c>File.Move</c> без <c>overwrite</c> (свіжий temp видалявся при
/// гонці), 66-символьні імена бекапів і ротація, що спиралася на випадковий GUID.
/// </summary>
public sealed class SaveDurabilityTests : IDisposable
{
    private readonly string _dir;

    public SaveDurabilityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ftdur_" + Guid.NewGuid().ToString("N"));
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

    private string BackupsDir => Path.Combine(_dir, ".backups");

    private static FamilyDocument Doc(string title)
    {
        var doc = FamilyDocument.CreateNew(title);
        doc.Persons.Add(new Person
        {
            LastName = "Коваленко",
            FirstName = "Оксана",
            MiddleName = "Іванівна",
            Gender = Gender.Female,
            BirthPlace = "Київ",
        });
        return doc;
    }

    // ---- Читабельність файлу ---------------------------------------------

    [Fact]
    public async Task Cyrillic_is_written_as_readable_text()
    {
        var storage = new JsonFamilyStorage();
        var path = PathFor("readable.familytree");

        await storage.SaveAsync(Doc("Тестова родина"), path);
        var text = await File.ReadAllTextAsync(path, new UTF8Encoding(false));

        // Раніше тут було "Тест..." — файл роздувався втричі
        // й не піддавався ні читанню, ні diff-у, ні grep-у.
        text.ShouldContain("Тестова родина");
        text.ShouldContain("Коваленко");
        text.ShouldNotContain("\\u04");
    }

    [Fact]
    public async Task Readable_cyrillic_still_roundtrips()
    {
        var storage = new JsonFamilyStorage();
        var path = PathFor("roundtrip.familytree");

        await storage.SaveAsync(Doc("Родина Ґудзь-О'Браєн"), path);
        var loaded = await storage.LoadAsync(path);

        loaded.Meta.Title.ShouldBe("Родина Ґудзь-О'Браєн");
        loaded.Persons[0].MiddleName.ShouldBe("Іванівна");
    }

    // ---- Резервні копії ---------------------------------------------------

    [Fact]
    public async Task Backup_slots_are_short_and_ordered_newest_first()
    {
        var storage = new JsonFamilyStorage();
        var path = PathFor("slots.familytree");

        await storage.SaveAsync(Doc("версія 1"), path);
        await storage.SaveAsync(Doc("версія 2"), path);
        await storage.SaveAsync(Doc("версія 3"), path);

        // Копія робиться ПЕРЕД заміною, тож .1 — це стан перед останнім збереженням.
        (await File.ReadAllTextAsync(Path.Combine(BackupsDir, "slots.familytree.1.bak")))
            .ShouldContain("версія 2");
        (await File.ReadAllTextAsync(Path.Combine(BackupsDir, "slots.familytree.2.bak")))
            .ShouldContain("версія 1");

        // Ім'я додає лише кілька символів до шляху (було +66 через тики та GUID).
        foreach (var backup in Directory.GetFiles(BackupsDir))
        {
            Path.GetFileName(backup).Length.ShouldBeLessThan("slots.familytree".Length + 12);
        }
    }

    [Fact]
    public async Task Backups_are_capped_and_oldest_is_dropped()
    {
        var storage = new JsonFamilyStorage();
        var path = PathFor("cap.familytree");

        // 8 збережень → 7 копій, але слотів лише 5.
        for (var i = 1; i <= 8; i++)
        {
            await storage.SaveAsync(Doc($"версія {i}"), path);
        }

        Directory.GetFiles(BackupsDir, "cap.familytree.*.bak").Length.ShouldBe(5);

        // Найновіша копія — стан перед останнім збереженням, найстарша — на 5 кроків раніше.
        (await File.ReadAllTextAsync(Path.Combine(BackupsDir, "cap.familytree.1.bak")))
            .ShouldContain("версія 7");
        (await File.ReadAllTextAsync(Path.Combine(BackupsDir, "cap.familytree.5.bak")))
            .ShouldContain("версія 3");
    }

    [Fact]
    public async Task Backup_is_a_valid_document_that_can_be_opened()
    {
        // Сенс копій — щоб їх можна було відкрити; перевіряємо, що це не «сміттєвий» файл.
        var storage = new JsonFamilyStorage();
        var path = PathFor("openable.familytree");

        await storage.SaveAsync(Doc("попередня"), path);
        await storage.SaveAsync(Doc("поточна"), path);

        var restored = await storage.LoadAsync(Path.Combine(BackupsDir, "openable.familytree.1.bak"));

        restored.Meta.Title.ShouldBe("попередня");
        restored.Persons.Count.ShouldBe(1);
    }

    // ---- Паралельні збереження -------------------------------------------

    [Fact]
    public async Task Concurrent_saves_do_not_corrupt_the_target()
    {
        // Раніше ім'я temp було фіксованим (fullPath + ".tmp"): друге збереження
        // відкривало той самий файл із FileMode.Create й обрізало JSON, який перше
        // вже готувалося промоутити через File.Replace.
        var storage = new JsonFamilyStorage();
        var path = PathFor("concurrent.familytree");

        var saves = Enumerable.Range(1, 12)
            .Select(i => storage.SaveAsync(Doc($"паралельна {i}"), path))
            .ToArray();

        await Task.WhenAll(saves);

        // Файл лишився валідним документом, а не обрізаним JSON.
        var loaded = await storage.LoadAsync(path);
        loaded.Persons.Count.ShouldBe(1);
        loaded.Meta.Title.ShouldStartWith("паралельна");

        // Жодного «осиротілого» temp після себе.
        Directory.GetFiles(_dir, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_saves_to_different_files_all_succeed()
    {
        var storage = new JsonFamilyStorage();

        var saves = Enumerable.Range(1, 6)
            .Select(i => storage.SaveAsync(Doc($"родина {i}"), PathFor($"multi{i}.familytree")))
            .ToArray();

        await Task.WhenAll(saves);

        for (var i = 1; i <= 6; i++)
        {
            (await storage.LoadAsync(PathFor($"multi{i}.familytree"))).Meta.Title.ShouldBe($"родина {i}");
        }
    }

    // ---- Гонки з файловою системою ----------------------------------------

    [Fact]
    public async Task Foreign_file_appearing_during_save_does_not_lose_work()
    {
        // Цільовий файл з'явився вже після початку збереження (інший екземпляр,
        // синхронізація OneDrive). Наші дані мусять дійти до диска, а не загубитися
        // в catch разом із видаленим temp.
        var storage = new JsonFamilyStorage();
        var path = PathFor("race.familytree");

        storage.FaultBeforePromote = () => File.WriteAllText(path, "чужий вміст, що з'явився під час збереження");

        await storage.SaveAsync(Doc("мої дані"), path);
        storage.FaultBeforePromote = null;

        (await storage.LoadAsync(path)).Meta.Title.ShouldBe("мої дані");
    }

    [Fact]
    public async Task Orphan_temp_from_a_previous_crash_does_not_block_saving()
    {
        // Ім'я temp тепер унікальне, тож після жорсткого краху в теці може лишитися
        // «осиротілий» *.tmp. Він не має ні ламати наступне збереження, ні бути
        // прийнятим за документ.
        var storage = new JsonFamilyStorage();
        var path = PathFor("orphan.familytree");
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "orphan.familytree.deadbeefdeadbeefdeadbeefdeadbeef.tmp"),
            "{\"schemaVersion\":1,\"meta\":{\"title\":\"недописаний\"}");

        await storage.SaveAsync(Doc("свіжі дані"), path);

        (await storage.LoadAsync(path)).Meta.Title.ShouldBe("свіжі дані");
    }

    [Fact]
    public async Task Save_creates_missing_directories()
    {
        var storage = new JsonFamilyStorage();
        var path = Path.Combine(_dir, "нова", "вкладена", "тека", "doc.familytree");

        await storage.SaveAsync(Doc("у новій теці"), path);

        (await storage.LoadAsync(path)).Meta.Title.ShouldBe("у новій теці");
    }

    // ---- Час оновлення ----------------------------------------------------

    [Fact]
    public async Task Successful_save_advances_updated_at()
    {
        var storage = new JsonFamilyStorage();
        var path = PathFor("stamp.familytree");
        var doc = Doc("з часом");
        var before = doc.Meta.UpdatedAt;

        await Task.Delay(5);
        await storage.SaveAsync(doc, path);

        doc.Meta.UpdatedAt.ShouldBeGreaterThan(before);
        doc.IsDirty.ShouldBeFalse();

        // У файлі — той самий час, що й у документі.
        (await storage.LoadAsync(path)).Meta.UpdatedAt.ShouldBe(doc.Meta.UpdatedAt);
    }
}
