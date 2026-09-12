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
            // Знімаємо ReadOnly перед видаленням: тест B-10 навмисно ставить цей
            // атрибут, а File.Copy переносить його ще й на резервну копію —
            // інакше Directory.Delete лишав би теку в %TEMP% назавжди.
            foreach (var file in Directory.GetFiles(_dir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

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
            Birth = PersonEvent.Create(null, "Київ"),
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

    // ---- Прапорець незбережених змін (B-11) -------------------------------

    [Fact]
    public async Task Change_made_during_save_keeps_the_document_dirty()
    {
        // Запис асинхронний і на великому документі в синхронізованій теці триває
        // секунди, а UI у цей час вільний. Правка, зроблена в цьому вікні, у знімок
        // не потрапила — але прапорець «є незбережені зміни» знімався все одно:
        // зірочка з заголовка зникала, закриття не питало, правка гинула.
        // FaultBeforePromote тут не «ламає» запис, а грає роль користувача, який
        // редагує документ саме тоді, коли файл ще пишеться.
        var storage = new JsonFamilyStorage();
        var path = PathFor("dirty-during-save.familytree");
        var doc = Doc("зберігається");
        doc.MarkChanged();

        storage.FaultBeforePromote = () =>
        {
            doc.Persons.Add(new Person
            {
                LastName = "Пізній",
                FirstName = "Запис",
                Gender = Gender.Unknown,
            });
            doc.MarkChanged();
        };

        await storage.SaveAsync(doc, path);
        storage.FaultBeforePromote = null;

        doc.IsDirty.ShouldBeTrue();

        // Сам файл — коректний знімок ДО пізньої правки; це не втрата, а очікувана
        // семантика: наступне збереження допише решту.
        (await storage.LoadAsync(path)).Persons.Count.ShouldBe(1);
        doc.Persons.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Second_save_clears_the_flag_when_nothing_changed_meanwhile()
    {
        // Зворотний бік: якщо під час запису ніхто нічого не чіпав, прапорець
        // мусить зніматися — інакше застосунок питав би про збереження вічно.
        var storage = new JsonFamilyStorage();
        var path = PathFor("clean-after-save.familytree");
        var doc = Doc("чистий після запису");
        doc.MarkChanged();

        await storage.SaveAsync(doc, path);

        doc.IsDirty.ShouldBeFalse();
    }

    // ---- Гейт цілісності на записі (B-12) ---------------------------------

    [Fact]
    public async Task Save_refuses_a_document_with_duplicate_person_ids()
    {
        // Читання завжди відмовлялося від дублікатів Id, а запис — ні, тож документ
        // із дублікатом перетворювався збереженням на назавжди невідкриваний файл.
        var storage = new JsonFamilyStorage();
        var path = PathFor("duplicate-ids.familytree");
        var doc = Doc("з дублікатом");
        var sharedId = doc.Persons[0].Id;
        doc.Persons.Add(new Person
        {
            Id = sharedId,
            LastName = "Двійник",
            FirstName = "Той самий Id",
            Gender = Gender.Unknown,
        });

        var ex = await Should.ThrowAsync<FamilyFileException>(() => storage.SaveAsync(doc, path));

        ex.MessageKey.ShouldBe(FileErrorKeys.WriteDuplicatePersonId);

        // Нічого не записано: ні файлу, ні temp, ні зсуву резервних копій.
        File.Exists(path).ShouldBeFalse();
        Directory.GetFiles(_dir, "*.tmp").ShouldBeEmpty();
        Directory.Exists(BackupsDir).ShouldBeFalse();
    }

    [Fact]
    public async Task Save_refuses_a_document_with_an_empty_person_id()
    {
        var storage = new JsonFamilyStorage();
        var path = PathFor("empty-id.familytree");
        var doc = Doc("без Id");
        doc.Persons.Add(new Person
        {
            Id = Guid.Empty,
            LastName = "Безідентифікаційний",
            FirstName = "Запис",
            Gender = Gender.Unknown,
        });

        var ex = await Should.ThrowAsync<FamilyFileException>(() => storage.SaveAsync(doc, path));

        ex.MessageKey.ShouldBe(FileErrorKeys.WriteEmptyPersonId);
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Refused_save_leaves_the_previous_file_and_its_backups_intact()
    {
        // Найдорожчий сценарій: файл уже є, і невдала спроба не має ні зіпсувати його,
        // ні прокрутити слоти резервних копій (інакше «відкотитися» стало б нікуди).
        var storage = new JsonFamilyStorage();
        var path = PathFor("intact.familytree");

        await storage.SaveAsync(Doc("версія 1"), path);
        await storage.SaveAsync(Doc("версія 2"), path);
        var originalContent = await File.ReadAllTextAsync(path);
        var backupContent = await File.ReadAllTextAsync(Path.Combine(BackupsDir, "intact.familytree.1.bak"));

        var broken = Doc("зламана");
        broken.Persons.Add(new Person
        {
            Id = broken.Persons[0].Id,
            LastName = "Двійник",
            FirstName = "Той самий Id",
            Gender = Gender.Unknown,
        });

        await Should.ThrowAsync<FamilyFileException>(() => storage.SaveAsync(broken, path));

        (await File.ReadAllTextAsync(path)).ShouldBe(originalContent);
        (await File.ReadAllTextAsync(Path.Combine(BackupsDir, "intact.familytree.1.bak")))
            .ShouldBe(backupContent);
        Directory.GetFiles(_dir, "*.tmp").ShouldBeEmpty();
    }

    // ---- Фолбек заміни файлу (B-10) ---------------------------------------

    [Theory]
    [InlineData("io")]          // FAT32/exFAT-флешка, частина SMB-шар
    [InlineData("access")]      // ERROR_ACCESS_DENIED від ReplaceFile — саме B-10
    [InlineData("platform")]    // ReplaceFile не підтримується платформою
    public async Task Replace_failure_falls_back_to_rename(string kind)
    {
        // Кожна з цих відмов означає «ReplaceFile тут не працює», а не «зберегти
        // не можна»: звичайне перейменування з перезаписом проходить. До B-10
        // фільтр не містив UnauthorizedAccessException, тож варіант "access"
        // пролітав повз фолбек і збереження падало на рівному місці.
        var storage = new JsonFamilyStorage();
        var path = PathFor($"fallback-{kind}.familytree");

        await storage.SaveAsync(Doc("попередня"), path);

        Exception fault = kind switch
        {
            "io" => new IOException("ReplaceFile недоступний на цій ФС"),
            "access" => new UnauthorizedAccessException("Access to the path is denied."),
            _ => new PlatformNotSupportedException("ReplaceFile не підтримується"),
        };

        storage.FaultOnReplace = () => throw fault;

        await storage.SaveAsync(Doc("нова"), path);
        storage.FaultOnReplace = null;

        (await storage.LoadAsync(path)).Meta.Title.ShouldBe("нова");
        Directory.GetFiles(_dir, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task Read_only_target_is_refused_with_a_localizable_error()
    {
        // Свідоме рішення B-10: атрибут ReadOnly не знімаємо — користувач поставив
        // його навмисно. Фолбек спробує перейменування, воно теж відмовить, і замість
        // сирого системного тексту користувач отримає локалізовану помилку.
        if (!OperatingSystem.IsWindows())
        {
            return; // ReadOnly як заборону запису гарантує лише Windows
        }

        var storage = new JsonFamilyStorage();
        var path = PathFor("readonly.familytree");

        await storage.SaveAsync(Doc("недоторкана"), path);
        var originalContent = await File.ReadAllTextAsync(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var ex = await Should.ThrowAsync<FamilyFileException>(
            () => storage.SaveAsync(Doc("нова"), path));

        ex.MessageKey.ShouldBe(FileErrorKeys.AccessDenied);
        ex.Arguments[0].ShouldBe(Path.GetFullPath(path));

        // Файл лишився тим самим, temp прибрано.
        File.SetAttributes(path, FileAttributes.Normal);
        (await File.ReadAllTextAsync(path)).ShouldBe(originalContent);
        Directory.GetFiles(_dir, "*.tmp").ShouldBeEmpty();
    }

    // ---- Помилки запису (B-09) --------------------------------------------
    //
    // Збій моделюється через FaultBeforePromote: він спрацьовує вже після запису
    // temp, тобто рівно там, де в реальності падають File.Replace/File.Move.
    // Так тест не залежить від прав, вільного місця й файлової системи агента.

    [Fact]
    public async Task Write_io_failure_becomes_a_localizable_error()
    {
        // Диск заповнений / мережевий носій відпав. Раніше IOException летів «як є»,
        // і застосунок показував системний англійський текст замість перекладеного.
        var storage = new JsonFamilyStorage();
        var path = PathFor("iofail.familytree");
        storage.FaultBeforePromote =
            () => throw new IOException("There is not enough space on the disk.");

        var ex = await Should.ThrowAsync<FamilyFileException>(
            () => storage.SaveAsync(Doc("не збережеться"), path));

        ex.MessageKey.ShouldBe(FileErrorKeys.WriteIo);

        // Користувачу показуємо цільовий файл, а не внутрішній temp.
        ex.Arguments.Count.ShouldBe(1);
        ex.Arguments[0].ShouldBe(Path.GetFullPath(path));
        ex.Message.ShouldNotContain(".tmp");

        // Temp прибрано, цільового файлу не з'явилося.
        Directory.GetFiles(_dir, "*.tmp").ShouldBeEmpty();
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Write_access_denied_becomes_a_localizable_error()
    {
        // Файл read-only, тека без прав, блокування антивірусом.
        var storage = new JsonFamilyStorage();
        var path = PathFor("denied.familytree");
        storage.FaultBeforePromote =
            () => throw new UnauthorizedAccessException("Access to the path is denied.");

        var ex = await Should.ThrowAsync<FamilyFileException>(
            () => storage.SaveAsync(Doc("не збережеться"), path));

        ex.MessageKey.ShouldBe(FileErrorKeys.AccessDenied);
        ex.Arguments[0].ShouldBe(Path.GetFullPath(path));
        Directory.GetFiles(_dir, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_is_not_disguised_as_a_file_error()
    {
        // Скасування — не помилка файлу: діалог із «не вдалося зберегти» тут був би
        // неправдою, тож OperationCanceledException має пройти наскрізь.
        var storage = new JsonFamilyStorage();
        var path = PathFor("cancelled.familytree");
        storage.FaultBeforePromote = () => throw new OperationCanceledException();

        await Should.ThrowAsync<OperationCanceledException>(
            () => storage.SaveAsync(Doc("скасовано"), path));
    }

    [Fact]
    public async Task Domain_error_from_deeper_is_not_wrapped_twice()
    {
        // Ключ і аргументи, поставлені нижче за стеком, мусять дійти до UI без змін
        // (важливо для B-10, де Promote почне кидати власну FamilyFileException).
        var storage = new JsonFamilyStorage();
        var path = PathFor("domain.familytree");
        var original = FamilyFileException.Create(FileErrorKeys.AccessDenied, inner: null, "з глибини");
        storage.FaultBeforePromote = () => throw original;

        var ex = await Should.ThrowAsync<FamilyFileException>(
            () => storage.SaveAsync(Doc("не збережеться"), path));

        ex.ShouldBeSameAs(original);
    }
}
