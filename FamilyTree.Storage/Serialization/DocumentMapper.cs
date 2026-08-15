using FamilyTree.Domain;

namespace FamilyTree.Storage.Serialization;

/// <summary>
/// Двобічний мапінг між DTO формату файлу та доменним <see cref="FamilyDocument"/>.
/// </summary>
internal static class DocumentMapper
{
    public static FamilyFileDto ToDto(FamilyDocument document, int schemaVersion) => new()
    {
        SchemaVersion = schemaVersion,
        Meta = new MetaDto
        {
            Title = document.Meta.Title,
            CreatedAt = document.Meta.CreatedAt,
            UpdatedAt = document.Meta.UpdatedAt,
            AppVersion = document.Meta.AppVersion,
        },
        Persons = document.Persons.Select(ToDto).ToList(),
        ParentChildLinks = document.ParentChildLinks.Select(ToDto).ToList(),
        SpouseLinks = document.SpouseLinks.Select(ToDto).ToList(),
    };

    public static FamilyDocument ToDomain(FamilyFileDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Ініціалізатори властивостей у DTO НЕ рятують від явного null у JSON:
        // System.Text.Json записує null поверх ініціалізатора, а
        // DefaultIgnoreCondition.WhenWritingNull впливає лише на серіалізацію.
        // Тому {"meta":null,"persons":null} без цих ?? давав NullReferenceException.
        var meta = dto.Meta ?? new MetaDto();

        var document = new FamilyDocument
        {
            Meta = new DocumentMeta
            {
                Title = meta.Title ?? string.Empty,
                CreatedAt = meta.CreatedAt,
                UpdatedAt = meta.UpdatedAt,
                // Невідому версію лишаємо порожньою, а не вигадуємо «1.0.0»: інакше застосунок
                // приписував би собі авторство чужого файлу без секції meta (B-65).
                AppVersion = meta.AppVersion ?? string.Empty,
            },
        };

        // OfType<T>() відкидає null-елементи масивів (напр. "persons": [null, {...}])
        // і водночас звужує тип для аналізу nullable.
        if (dto.Persons is { } persons)
        {
            document.Persons.AddRange(persons.OfType<PersonDto>().Select(ToDomain));
        }

        if (dto.ParentChildLinks is { } parentChildLinks)
        {
            document.ParentChildLinks.AddRange(parentChildLinks.OfType<ParentChildLinkDto>().Select(ToDomain));
        }

        if (dto.SpouseLinks is { } spouseLinks)
        {
            document.SpouseLinks.AddRange(spouseLinks.OfType<SpouseLinkDto>().Select(ToDomain));
        }

        return document;
    }

    private static PersonDto ToDto(Person p) => new()
    {
        Id = p.Id,
        LastName = p.LastName,
        FirstName = p.FirstName,
        Gender = p.Gender,
        MiddleName = p.MiddleName,
        MaidenName = p.MaidenName,
        BirthDate = p.BirthDate,
        BirthPlace = p.BirthPlace,
        DeathDate = p.DeathDate,
        PhotoPath = p.PhotoPath,
        Notes = p.Notes,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
    };

    private static Person ToDomain(PersonDto d) => new()
    {
        Id = d.Id,
        LastName = d.LastName ?? string.Empty,
        FirstName = d.FirstName ?? string.Empty,
        Gender = d.Gender,
        MiddleName = d.MiddleName,
        MaidenName = d.MaidenName,
        BirthDate = d.BirthDate,
        BirthPlace = d.BirthPlace,
        DeathDate = d.DeathDate,
        PhotoPath = d.PhotoPath,
        Notes = d.Notes,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
    };

    private static ParentChildLinkDto ToDto(ParentChildLink l) => new()
    {
        Id = l.Id,
        ParentId = l.ParentId,
        ChildId = l.ChildId,
        ParentRole = l.ParentRole,
    };

    // Порожній Id зв'язку (поле "id" відсутнє у файлі) замінюємо на новий: Entity.Equals
    // порівнює за Id, тож два зв'язки з Guid.Empty вважалися б рівними — і List.Remove
    // видаляв би не той зв'язок. Ідентичність самого зв'язку визначається парою Id осіб,
    // тому згенерувати новий Id тут безпечно.
    private static ParentChildLink ToDomain(ParentChildLinkDto d) => d.Id == Guid.Empty
        ? new ParentChildLink
        {
            ParentId = d.ParentId,
            ChildId = d.ChildId,
            ParentRole = d.ParentRole,
        }
        : new ParentChildLink
        {
            Id = d.Id,
            ParentId = d.ParentId,
            ChildId = d.ChildId,
            ParentRole = d.ParentRole,
        };

    private static SpouseLinkDto ToDto(SpouseLink l) => new()
    {
        Id = l.Id,
        Person1Id = l.Person1Id,
        Person2Id = l.Person2Id,
        MarriageDate = l.MarriageDate,
        DivorceDate = l.DivorceDate,
    };

    // Порядок Id (Person1Id ≤ Person2Id) нормалізує DocumentIntegrity після мапінгу:
    // покладатися на те, що у файлі він уже правильний, не можна.
    // Порожній Id зв'язку — див. коментар до ParentChildLink вище.
    private static SpouseLink ToDomain(SpouseLinkDto d) => d.Id == Guid.Empty
        ? new SpouseLink
        {
            Person1Id = d.Person1Id,
            Person2Id = d.Person2Id,
            MarriageDate = d.MarriageDate,
            DivorceDate = d.DivorceDate,
        }
        : new SpouseLink
        {
            Id = d.Id,
            Person1Id = d.Person1Id,
            Person2Id = d.Person2Id,
            MarriageDate = d.MarriageDate,
            DivorceDate = d.DivorceDate,
        };
}
