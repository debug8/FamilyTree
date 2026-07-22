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
        var document = new FamilyDocument
        {
            Meta = new DocumentMeta
            {
                Title = dto.Meta.Title,
                CreatedAt = dto.Meta.CreatedAt,
                UpdatedAt = dto.Meta.UpdatedAt,
                AppVersion = dto.Meta.AppVersion,
            },
        };

        document.Persons.AddRange(dto.Persons.Select(ToDomain));
        document.ParentChildLinks.AddRange(dto.ParentChildLinks.Select(ToDomain));
        document.SpouseLinks.AddRange(dto.SpouseLinks.Select(ToDomain));
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
        LastName = d.LastName,
        FirstName = d.FirstName,
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

    private static ParentChildLink ToDomain(ParentChildLinkDto d) => new()
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

    // Ідентифікатори у файлі вже нормалізовані (Person1Id ≤ Person2Id) при збереженні.
    private static SpouseLink ToDomain(SpouseLinkDto d) => new()
    {
        Id = d.Id,
        Person1Id = d.Person1Id,
        Person2Id = d.Person2Id,
        MarriageDate = d.MarriageDate,
        DivorceDate = d.DivorceDate,
    };
}
