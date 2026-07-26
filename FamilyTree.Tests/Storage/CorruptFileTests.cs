using System.IO;
using System.Text;
using FamilyTree.Domain;
using FamilyTree.Storage;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Storage;

/// <summary>
/// Стійкість завантаження до «поганих» файлів: битий JSON, ручне редагування,
/// чужий експортер, неправильне кодування. До цих тестів такі файли давали
/// NullReferenceException / ArgumentException / InvalidOperationException,
/// і користувач бачив технічний текст .NET або «Неочікувану помилку».
/// </summary>
public sealed class CorruptFileTests : IDisposable
{
    private readonly string _dir;

    public CorruptFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ftcorrupt_" + Guid.NewGuid().ToString("N"));
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

    private async Task<string> WriteAsync(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
        return path;
    }

    private static Task<FamilyDocument> LoadAsync(string path) => new JsonFamilyStorage().LoadAsync(path);

    private const string PersonA = "\"id\":\"11111111-1111-4111-8111-111111111111\",\"lastName\":\"Іванов\",\"firstName\":\"Іван\",\"gender\":\"Male\"";
    private const string PersonB = "\"id\":\"22222222-2222-4222-8222-222222222222\",\"lastName\":\"Іванова\",\"firstName\":\"Ольга\",\"gender\":\"Female\"";

    private static readonly Guid IdA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid IdB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Missing = Guid.Parse("99999999-9999-4999-8999-999999999999");

    // ---- Помилки, після яких файл не відкривається -----------------------

    [Fact]
    public async Task Malformed_json_reports_localizable_error()
    {
        var path = await WriteAsync("broken.familytree", "{\"schemaVersion\":1, oops}");

        var ex = await Should.ThrowAsync<FamilyFileException>(() => LoadAsync(path));
        ex.MessageKey.ShouldBe(FileErrorKeys.MalformedJson);
    }

    [Fact]
    public async Task Json_array_root_is_rejected()
    {
        var path = await WriteAsync("array.familytree", "[]");

        var ex = await Should.ThrowAsync<FamilyFileException>(() => LoadAsync(path));
        ex.MessageKey.ShouldBe(FileErrorKeys.MalformedJson);
    }

    [Fact]
    public async Task Missing_schema_version_reports_localizable_error()
    {
        var path = await WriteAsync("nover.familytree", "{\"persons\":[]}");

        var ex = await Should.ThrowAsync<FamilyFileException>(() => LoadAsync(path));
        ex.MessageKey.ShouldBe(FileErrorKeys.BadSchemaVersion);
    }

    [Theory]
    [InlineData("\"1\"")]  // рядок замість числа
    [InlineData("1.5")]    // дріб
    [InlineData("true")]   // логічне
    [InlineData("null")]
    [InlineData("0")]      // немає міграції з версії 0
    [InlineData("-3")]
    public async Task Invalid_schema_version_reports_localizable_error(string version)
    {
        var path = await WriteAsync("badver.familytree", $"{{\"schemaVersion\":{version},\"persons\":[]}}");

        var ex = await Should.ThrowAsync<FamilyFileException>(() => LoadAsync(path));
        ex.MessageKey.ShouldBe(FileErrorKeys.BadSchemaVersion);
    }

    [Fact]
    public async Task Duplicate_person_id_is_refused_with_clear_error()
    {
        var path = await WriteAsync("dup.familytree",
            $"{{\"schemaVersion\":1,\"persons\":[{{{PersonA}}},{{{PersonA}}}]}}");

        var ex = await Should.ThrowAsync<FamilyFileException>(() => LoadAsync(path));
        ex.MessageKey.ShouldBe(FileErrorKeys.DuplicatePersonId);
        // Аргументи потрібні для локалізованого шаблону: кількість + приклад Id.
        ex.Arguments.Count.ShouldBe(2);
        ex.Arguments[0].ShouldBe(1);
        ex.Arguments[1].ShouldBe(IdA);
    }

    [Fact]
    public async Task Person_without_id_is_refused_with_clear_error()
    {
        var path = await WriteAsync("noid.familytree",
            "{\"schemaVersion\":1,\"persons\":[{\"lastName\":\"Без\",\"firstName\":\"Id\",\"gender\":\"Male\"}]}");

        var ex = await Should.ThrowAsync<FamilyFileException>(() => LoadAsync(path));
        ex.MessageKey.ShouldBe(FileErrorKeys.EmptyPersonId);
        ex.Arguments[0].ShouldBe(1);
    }

    [Fact]
    public async Task Missing_file_reports_not_found()
    {
        var ex = await Should.ThrowAsync<FamilyFileException>(
            () => LoadAsync(Path.Combine(_dir, "nope.familytree")));

        ex.MessageKey.ShouldBe(FileErrorKeys.NotFound);
    }

    [Fact]
    public async Task Non_utf8_file_reports_bad_encoding()
    {
        // Байти Windows-1251 («Пр») — некоректна послідовність UTF-8.
        // Раніше вони тихо декодувалися в U+FFFD, і кирилиця ставала крякозябрами.
        var path = Path.Combine(_dir, "ansi.familytree");
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"meta\":{\"title\":\""));
        bytes.AddRange(new byte[] { 0xCF, 0xF0 });
        bytes.AddRange(Encoding.UTF8.GetBytes("\"},\"persons\":[]}"));
        await File.WriteAllBytesAsync(path, bytes.ToArray());

        var ex = await Should.ThrowAsync<FamilyFileException>(() => LoadAsync(path));
        ex.MessageKey.ShouldBe(FileErrorKeys.BadEncoding);
    }

    // ---- Дефекти, які лагодяться зі звітом --------------------------------

    [Fact]
    public async Task Explicit_nulls_do_not_throw()
    {
        // Ініціалізатори властивостей DTO не рятують: System.Text.Json пише null
        // поверх них. Раніше це давало NullReferenceException.
        var path = await WriteAsync("nulls.familytree",
            "{\"schemaVersion\":1,\"meta\":null,\"persons\":null,\"parentChildLinks\":null,\"spouseLinks\":null}");

        var doc = await LoadAsync(path);

        doc.Meta.Title.ShouldBe(string.Empty);
        doc.Persons.ShouldBeEmpty();
        doc.ParentChildLinks.ShouldBeEmpty();
        doc.SpouseLinks.ShouldBeEmpty();
        doc.RepairedIssues.ShouldBeEmpty();
    }

    [Fact]
    public async Task Null_array_elements_are_skipped()
    {
        var path = await WriteAsync("nullitems.familytree",
            $"{{\"schemaVersion\":1,\"persons\":[null,{{{PersonA}}},null]}}");

        var doc = await LoadAsync(path);

        doc.Persons.Count.ShouldBe(1);
        doc.Persons[0].Id.ShouldBe(IdA);
    }

    [Fact]
    public async Task Dangling_links_are_dropped_and_reported()
    {
        var path = await WriteAsync("dangling.familytree",
            $"{{\"schemaVersion\":1,\"persons\":[{{{PersonA}}},{{{PersonB}}}]," +
            $"\"parentChildLinks\":[{{\"parentId\":\"{IdA}\",\"childId\":\"{Missing}\"}}," +
            $"{{\"parentId\":\"{IdA}\",\"childId\":\"{IdB}\"}}]," +
            $"\"spouseLinks\":[{{\"person1Id\":\"{Missing}\",\"person2Id\":\"{IdB}\"}}]}}");

        var doc = await LoadAsync(path);

        doc.ParentChildLinks.Count.ShouldBe(1);
        doc.ParentChildLinks[0].ChildId.ShouldBe(IdB);
        doc.SpouseLinks.ShouldBeEmpty();

        var issue = doc.RepairedIssues.Single(i => i.MessageKey == FileErrorKeys.RepairedDanglingLinks);
        issue.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Self_links_are_dropped_and_reported()
    {
        var path = await WriteAsync("self.familytree",
            $"{{\"schemaVersion\":1,\"persons\":[{{{PersonA}}}]," +
            $"\"parentChildLinks\":[{{\"parentId\":\"{IdA}\",\"childId\":\"{IdA}\"}}]," +
            $"\"spouseLinks\":[{{\"person1Id\":\"{IdA}\",\"person2Id\":\"{IdA}\"}}]}}");

        var doc = await LoadAsync(path);

        doc.ParentChildLinks.ShouldBeEmpty();
        doc.SpouseLinks.ShouldBeEmpty();
        doc.RepairedIssues.Single(i => i.MessageKey == FileErrorKeys.RepairedSelfLinks).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Duplicate_links_are_dropped_and_reported()
    {
        var path = await WriteAsync("duplinks.familytree",
            $"{{\"schemaVersion\":1,\"persons\":[{{{PersonA}}},{{{PersonB}}}]," +
            $"\"parentChildLinks\":[{{\"parentId\":\"{IdA}\",\"childId\":\"{IdB}\"}}," +
            $"{{\"parentId\":\"{IdA}\",\"childId\":\"{IdB}\"}}]}}");

        var doc = await LoadAsync(path);

        doc.ParentChildLinks.Count.ShouldBe(1);
        doc.RepairedIssues.Single(i => i.MessageKey == FileErrorKeys.RepairedDuplicateLinks).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Reversed_spouse_ids_are_normalized_silently()
    {
        // Домен декларує інваріант Person1Id ≤ Person2Id, але required init дозволяє
        // порушити його; тоді перевірка дубля шлюбу у валідаторі не знаходить дубля.
        // Нормалізація без втрат, тому в звіт не потрапляє (інакше кожне відкриття
        // rodyna-500.familytree показувало б попередження на 114 зв'язків).
        var path = await WriteAsync("revspouse.familytree",
            $"{{\"schemaVersion\":1,\"persons\":[{{{PersonA}}},{{{PersonB}}}]," +
            $"\"spouseLinks\":[{{\"person1Id\":\"{IdB}\",\"person2Id\":\"{IdA}\"}}]}}");

        var doc = await LoadAsync(path);

        doc.SpouseLinks[0].Person1Id.ShouldBe(IdA);
        doc.SpouseLinks[0].Person2Id.ShouldBe(IdB);
        doc.RepairedIssues.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reversed_spouse_link_becomes_detectable_duplicate()
    {
        // Після нормалізації та сама пара у двох порядках стає одним зв'язком.
        var path = await WriteAsync("revdup.familytree",
            $"{{\"schemaVersion\":1,\"persons\":[{{{PersonA}}},{{{PersonB}}}]," +
            $"\"spouseLinks\":[{{\"person1Id\":\"{IdA}\",\"person2Id\":\"{IdB}\"}}," +
            $"{{\"person1Id\":\"{IdB}\",\"person2Id\":\"{IdA}\"}}]}}");

        var doc = await LoadAsync(path);

        doc.SpouseLinks.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Out_of_range_enum_values_are_reset_and_reported()
    {
        // JsonStringEnumConverter за замовчуванням приймає цілі числа, тож "gender": 7
        // ставало (Gender)7 і текло в усі форматери родства без перевірки.
        var path = await WriteAsync("badenum.familytree",
            $"{{\"schemaVersion\":1,\"persons\":[{{\"id\":\"{IdA}\",\"lastName\":\"Х\",\"firstName\":\"Х\",\"gender\":7}},{{{PersonB}}}]," +
            $"\"parentChildLinks\":[{{\"parentId\":\"{IdA}\",\"childId\":\"{IdB}\",\"parentRole\":42}}]}}");

        var doc = await LoadAsync(path);

        doc.Persons.Single(p => p.Id == IdA).Gender.ShouldBe(Gender.Unknown);
        doc.ParentChildLinks[0].ParentRole.ShouldBe(ParentRole.Biological);
        doc.RepairedIssues.Single(i => i.MessageKey == FileErrorKeys.RepairedBadEnums).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Clean_file_reports_no_issues()
    {
        var path = await WriteAsync("clean.familytree",
            $"{{\"schemaVersion\":1,\"meta\":{{\"title\":\"Чисто\"}},\"persons\":[{{{PersonA}}},{{{PersonB}}}]," +
            $"\"parentChildLinks\":[{{\"parentId\":\"{IdA}\",\"childId\":\"{IdB}\"}}]," +
            $"\"spouseLinks\":[]}}");

        var doc = await LoadAsync(path);

        doc.Meta.Title.ShouldBe("Чисто");
        doc.RepairedIssues.ShouldBeEmpty();
    }

    [Fact]
    public async Task Repaired_document_survives_graph_construction()
    {
        // Головний сценарій регресії: раніше битий файл валив застосунок аж
        // у FamilyGraph/ToDictionary — тобто ПІСЛЯ того, як документ уже було
        // встановлено в сесію, з напівзламаним UI.
        var path = await WriteAsync("regress.familytree",
            $"{{\"schemaVersion\":1,\"persons\":[{{{PersonA}}},{{{PersonB}}}]," +
            $"\"parentChildLinks\":[{{\"parentId\":\"{IdA}\",\"childId\":\"{Missing}\"}}," +
            $"{{\"parentId\":\"{IdA}\",\"childId\":\"{IdA}\"}}]," +
            $"\"spouseLinks\":[{{\"person1Id\":\"{IdB}\",\"person2Id\":\"{IdA}\"}}]}}");

        var doc = await LoadAsync(path);

        var graph = Should.NotThrow(() =>
            new FamilyGraph(doc.Persons, doc.ParentChildLinks, doc.SpouseLinks));

        graph.GetSpouses(IdA).Single().Id.ShouldBe(IdB);
        graph.GetChildren(IdA).ShouldBeEmpty();
        doc.RepairedIssues.ShouldNotBeEmpty();
    }

    [Fact]
    public void Graph_tolerates_duplicate_person_ids_as_last_resort()
    {
        // Гейт проти дублікатів — у сховищі; граф лише не має падати,
        // якщо дублікат прийшов іншим шляхом (раніше — ArgumentException із ctor).
        var a = new Person { Id = IdA, LastName = "Перший", FirstName = "Запис", Gender = Gender.Male };
        var b = new Person { Id = IdA, LastName = "Другий", FirstName = "Запис", Gender = Gender.Female };

        var graph = Should.NotThrow(() => new FamilyGraph([a, b], [], []));

        graph.GetPerson(IdA).ShouldNotBeNull();
    }
}
