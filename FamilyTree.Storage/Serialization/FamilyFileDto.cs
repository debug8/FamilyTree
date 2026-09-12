using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyTree.Domain;

namespace FamilyTree.Storage.Serialization;

/// <summary>
/// DTO формату файлу .familytree. Відокремлений від доменних сутностей, щоб формат
/// зберігання можна було версіонувати й мігрувати незалежно від домену.
/// </summary>
/// <remarks>
/// Посилальні властивості оголошені nullable свідомо: у файлі, зробленому вручну або
/// стороннім експортером, будь-яке поле може прийти як <c>null</c>, і ініціалізатор
/// властивості від цього НЕ захищає — <c>System.Text.Json</c> запише null поверх нього.
/// Приведення до непорожніх значень робить <see cref="DocumentMapper.ToDomain(FamilyFileDto)"/>.
/// </remarks>
internal sealed class FamilyFileDto
{
    public int SchemaVersion { get; set; }

    public MetaDto? Meta { get; set; } = new();

    public List<PersonDto?>? Persons { get; set; } = new();

    public List<ParentChildLinkDto?>? ParentChildLinks { get; set; } = new();

    public List<SpouseLinkDto?>? SpouseLinks { get; set; } = new();

    /// <summary>
    /// Цілі секції файлу, яких ця збірка не знає (напр. майбутній довідник місць).
    /// Без цього стара збірка, відкривши новіший файл, стерла б їх при збереженні.
    /// Див. <see cref="DocumentExtras"/>.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class MetaDto
{
    public string? Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    // Без ініціалізатора: версію проставляє лише той, хто пише (B-65). null = у файлі секції нема.
    public string? AppVersion { get; set; }

    /// <summary>Незнайомі поля секції <c>meta</c> — див. <see cref="DocumentExtras"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class PersonDto
{
    public Guid Id { get; set; }
    public string? LastName { get; set; } = string.Empty;
    public string? FirstName { get; set; } = string.Empty;
    public Gender Gender { get; set; }
    public string? MiddleName { get; set; }
    public string? MaidenName { get; set; }
    public FamilyDateDto? BirthDate { get; set; }
    public string? BirthPlace { get; set; }
    public string? BirthNote { get; set; }
    public FamilyDateDto? DeathDate { get; set; }
    public string? DeathPlace { get; set; }
    public string? DeathNote { get; set; }

    // Померла, дата невідома. WhenWritingDefault — точно як SpouseLinkDto.Divorced:
    // false у файл не пишемо (щоб не роздувати й не засмічувати diff), а відсутнє
    // поле в старих файлах читається як false, тобто «жива». Тож bump схеми не потрібен.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Deceased { get; set; }

    public string? PhotoPath { get; set; }

    // byte[] у System.Text.Json серіалізується як base64-рядок. Поле опційне й у
    // звичайному файлі відсутнє (DefaultIgnoreCondition = WhenWritingNull), тож
    // bump версії схеми не потрібен — як свого часу зі SpouseLink.Divorced.
    public byte[]? PhotoThumbnail { get; set; }

    public string? Notes { get; set; }

    // Життєві факти (OCCU/RESI). Без ініціалізатора: null = у файлі поля немає.
    // Порожній список мапер теж пише як null — інакше кожна особа без фактів
    // тягла б у файл зайвий рядок "facts": [].
    public List<PersonFactDto?>? Facts { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Незнайомі поля особи — див. <see cref="DocumentExtras"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// DTO життєвого факту особи (<see cref="PersonFact"/>).
/// </summary>
/// <remarks>
/// <see cref="Kind"/> — рядок, а не enum через <c>JsonStringEnumConverter</c>: файл,
/// записаний новішою збіркою з видом, якого ця ще не знає, має читатися без падіння.
/// Конвертер кинув би <c>JsonException</c>, і користувач побачив би «файл пошкоджено»
/// замість одного незнайомого запису. Невідомий рядок читається як
/// <see cref="PersonFactKind.Other"/> зі збереженням оригіналу в <c>Label</c> і
/// повертається у файл незміненим. Той самий підхід, що й у <see cref="FamilyDateDto.Kind"/>.
/// </remarks>
internal sealed class PersonFactDto
{
    public string? Kind { get; set; }      // occupation | residence | <оригінал невідомого виду>
    public string? Value { get; set; }
    public FamilyDateDto? Date { get; set; }
    public string? Place { get; set; }
}

internal sealed class ParentChildLinkDto
{
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }
    public Guid ChildId { get; set; }
    public ParentRole ParentRole { get; set; }

    /// <summary>Незнайомі поля зв'язку — див. <see cref="DocumentExtras"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class SpouseLinkDto
{
    public Guid Id { get; set; }
    public Guid Person1Id { get; set; }
    public Guid Person2Id { get; set; }
    public FamilyDateDto? MarriageDate { get; set; }
    public string? MarriagePlace { get; set; }
    public FamilyDateDto? DivorceDate { get; set; }

    // Шлюб завершено без дати. WhenWritingDefault: false не пишемо у файл (щоб не роздувати
    // й не засмічувати diff), а відсутнє поле в старих файлах читається як false — сумісно.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Divorced { get; set; }

    /// <summary>Незнайомі поля зв'язку — див. <see cref="DocumentExtras"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// DTO неточної дати (формат v2, T-5.2a). Мапиться в/з <see cref="FamilyDate"/> у
/// <see cref="DocumentMapper"/>. Порожні поля не серіалізуються (<c>WhenWritingNull</c>),
/// тож точна дата виглядає компактно: <c>{"kind":"exact","y":1980,"m":3,"d":14}</c>.
/// Основна точка (exact/approx і нижня межа діапазону) інлайниться полями <c>y/m/d/cal</c>;
/// діапазон використовує вкладені <c>from</c>/<c>to</c>.
/// </summary>
internal sealed class FamilyDateDto
{
    public string? Kind { get; set; }      // exact | approx | range | phrase

    // Інлайн-точка для exact/approx.
    public int? Y { get; set; }
    public int? M { get; set; }
    public int? D { get; set; }
    public string? Cal { get; set; }       // julian (для gregorian не пишемо)

    public string? Q { get; set; }         // approx: about | calculated | estimated

    public string? Range { get; set; }     // range: before | after | between
    public DatePointDto? From { get; set; }
    public DatePointDto? To { get; set; }

    public string? Text { get; set; }      // phrase

    public string? Gedcom { get; set; }    // сирий GEDCOM-вираз для round-trip
}

/// <summary>Вкладена дата-точка діапазону (<see cref="FamilyDateDto.From"/>/<see cref="FamilyDateDto.To"/>).</summary>
internal sealed class DatePointDto
{
    public int? Y { get; set; }
    public int? M { get; set; }
    public int? D { get; set; }
    public string? Cal { get; set; }
}
