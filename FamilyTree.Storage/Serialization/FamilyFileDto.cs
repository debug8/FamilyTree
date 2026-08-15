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
}

internal sealed class MetaDto
{
    public string? Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    // Без ініціалізатора: версію проставляє лише той, хто пише (B-65). null = у файлі секції нема.
    public string? AppVersion { get; set; }
}

internal sealed class PersonDto
{
    public Guid Id { get; set; }
    public string? LastName { get; set; } = string.Empty;
    public string? FirstName { get; set; } = string.Empty;
    public Gender Gender { get; set; }
    public string? MiddleName { get; set; }
    public string? MaidenName { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? BirthPlace { get; set; }
    public DateOnly? DeathDate { get; set; }
    public string? PhotoPath { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal sealed class ParentChildLinkDto
{
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }
    public Guid ChildId { get; set; }
    public ParentRole ParentRole { get; set; }
}

internal sealed class SpouseLinkDto
{
    public Guid Id { get; set; }
    public Guid Person1Id { get; set; }
    public Guid Person2Id { get; set; }
    public DateOnly? MarriageDate { get; set; }
    public DateOnly? DivorceDate { get; set; }
}
