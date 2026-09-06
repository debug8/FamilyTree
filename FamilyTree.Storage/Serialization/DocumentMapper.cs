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
        BirthDate = ToDto(p.BirthDate),
        BirthPlace = p.BirthPlace,
        DeathDate = ToDto(p.DeathDate),
        PhotoPath = p.PhotoPath,
        PhotoThumbnail = p.PhotoThumbnail,
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
        BirthDate = ToDomain(d.BirthDate),
        BirthPlace = d.BirthPlace,
        DeathDate = ToDomain(d.DeathDate),
        PhotoPath = d.PhotoPath,
        PhotoThumbnail = d.PhotoThumbnail,
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
        MarriageDate = ToDto(l.MarriageDate),
        DivorceDate = ToDto(l.DivorceDate),
        Divorced = l.Divorced,
    };

    // Порядок Id (Person1Id ≤ Person2Id) нормалізує DocumentIntegrity після мапінгу:
    // покладатися на те, що у файлі він уже правильний, не можна.
    // Порожній Id зв'язку — див. коментар до ParentChildLink вище.
    private static SpouseLink ToDomain(SpouseLinkDto d) => d.Id == Guid.Empty
        ? new SpouseLink
        {
            Person1Id = d.Person1Id,
            Person2Id = d.Person2Id,
            MarriageDate = ToDomain(d.MarriageDate),
            DivorceDate = ToDomain(d.DivorceDate),
            Divorced = d.Divorced,
        }
        : new SpouseLink
        {
            Id = d.Id,
            Person1Id = d.Person1Id,
            Person2Id = d.Person2Id,
            MarriageDate = ToDomain(d.MarriageDate),
            DivorceDate = ToDomain(d.DivorceDate),
            Divorced = d.Divorced,
        };

    // ---- Дати: FamilyDate ↔ FamilyDateDto (формат v2, T-5.2a) ----------------

    private static FamilyDateDto? ToDto(FamilyDate? date)
    {
        if (date is null)
        {
            return null;
        }

        var dto = new FamilyDateDto { Gedcom = date.OriginalGedcom };
        switch (date.Kind)
        {
            case FamilyDateKind.Exact:
                dto.Kind = "exact";
                WriteInlinePoint(dto, date.Start);
                break;

            case FamilyDateKind.Approximate:
                dto.Kind = "approx";
                dto.Q = ApproximationToString(date.Approximation);
                WriteInlinePoint(dto, date.Start);
                break;

            case FamilyDateKind.Range:
                dto.Kind = "range";
                dto.Range = RangeToString(date.RangeKind);
                dto.From = PointToDto(date.Start);
                dto.To = PointToDto(date.End);
                break;

            case FamilyDateKind.Phrase:
                dto.Kind = "phrase";
                dto.Text = date.Phrase;
                break;
        }

        return dto;
    }

    // Будуємо запис напряму (не через фабрики, що кидають): «брудний» v2-файл із неповною
    // датою не має валити завантаження — структурно биту дату відкине DocumentIntegrity.
    private static FamilyDate? ToDomain(FamilyDateDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        FamilyDate? result = dto.Kind switch
        {
            "exact" => new FamilyDate { Kind = FamilyDateKind.Exact, Start = InlinePointToDomain(dto) },
            "approx" => new FamilyDate
            {
                Kind = FamilyDateKind.Approximate,
                Approximation = ParseApproximation(dto.Q),
                Start = InlinePointToDomain(dto),
            },
            "range" => new FamilyDate
            {
                Kind = FamilyDateKind.Range,
                RangeKind = ParseRange(dto.Range),
                Start = PointToDomain(dto.From),
                End = PointToDomain(dto.To),
            },
            "phrase" => new FamilyDate { Kind = FamilyDateKind.Phrase, Phrase = dto.Text },
            _ => null,
        };

        return dto.Gedcom is { } gedcom && result is not null ? result.WithOriginalGedcom(gedcom) : result;
    }

    private static void WriteInlinePoint(FamilyDateDto dto, DatePoint? point)
    {
        if (point is null)
        {
            return;
        }

        dto.Y = point.Year;
        dto.M = point.Month;
        dto.D = point.Day;
        dto.Cal = point.Calendar == DateCalendar.Julian ? "julian" : null;
    }

    private static DatePoint? InlinePointToDomain(FamilyDateDto dto) =>
        dto.Y is { } year
            ? new DatePoint { Year = year, Month = dto.M, Day = dto.D, Calendar = ParseCalendar(dto.Cal) }
            : null;

    private static DatePointDto? PointToDto(DatePoint? point) =>
        point is null
            ? null
            : new DatePointDto
            {
                Y = point.Year,
                M = point.Month,
                D = point.Day,
                Cal = point.Calendar == DateCalendar.Julian ? "julian" : null,
            };

    private static DatePoint? PointToDomain(DatePointDto? point) =>
        point?.Y is { } year
            ? new DatePoint { Year = year, Month = point.M, Day = point.D, Calendar = ParseCalendar(point.Cal) }
            : null;

    private static DateCalendar ParseCalendar(string? cal) =>
        string.Equals(cal, "julian", StringComparison.OrdinalIgnoreCase)
            ? DateCalendar.Julian
            : DateCalendar.Gregorian;

    private static DateApproximation? ParseApproximation(string? q) => q?.ToLowerInvariant() switch
    {
        "about" => DateApproximation.About,
        "calculated" => DateApproximation.Calculated,
        "estimated" => DateApproximation.Estimated,
        _ => null,
    };

    private static string ApproximationToString(DateApproximation? approximation) => approximation switch
    {
        DateApproximation.Calculated => "calculated",
        DateApproximation.Estimated => "estimated",
        _ => "about",
    };

    private static DateRangeKind? ParseRange(string? range) => range?.ToLowerInvariant() switch
    {
        "before" => DateRangeKind.Before,
        "after" => DateRangeKind.After,
        "between" => DateRangeKind.Between,
        _ => null,
    };

    private static string RangeToString(DateRangeKind? range) => range switch
    {
        DateRangeKind.After => "after",
        DateRangeKind.Between => "between",
        _ => "before",
    };
}
