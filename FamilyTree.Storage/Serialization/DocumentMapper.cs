using System.Text.Json;
using FamilyTree.Domain;

namespace FamilyTree.Storage.Serialization;

/// <summary>
/// Двобічний мапінг між DTO формату файлу та доменним <see cref="FamilyDocument"/>.
/// </summary>
/// <remarks>
/// Крім відомих полів мапер везе в обидва боки <see cref="DocumentExtras"/> — незнайомі поля
/// файлу, які інакше зникали б при першому ж збереженні старішою збіркою. Зіставлення з
/// сутностями — за <c>Id</c>; словники живуть у документі, бо домену вони не стосуються.
/// </remarks>
internal static class DocumentMapper
{
    public static FamilyFileDto ToDto(FamilyDocument document, int schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(document);

        var extras = document.Extras;

        return new FamilyFileDto
        {
            SchemaVersion = schemaVersion,
            Meta = new MetaDto
            {
                Title = document.Meta.Title,
                CreatedAt = document.Meta.CreatedAt,
                UpdatedAt = document.Meta.UpdatedAt,
                AppVersion = document.Meta.AppVersion,
                Extra = extras.Meta,
            },

            // Пошук за Id сутності, яка в документі СПРАВДІ є: записи видалених осіб і
            // зв'язків лишаються у словнику, але у файл не потрапляють (див. DocumentExtras).
            Persons = document.Persons.Select(p => ToDto(p, extras)).ToList(),
            ParentChildLinks = document.ParentChildLinks.Select(l => ToDto(l, extras)).ToList(),
            SpouseLinks = document.SpouseLinks.Select(l => ToDto(l, extras)).ToList(),
            Extra = extras.Root,
        };
    }

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

        var extras = document.Extras;
        extras.Root = dto.Extra;
        extras.Meta = meta.Extra;

        // OfType<T>() відкидає null-елементи масивів (напр. "persons": [null, {...}])
        // і водночас звужує тип для аналізу nullable.
        //
        // Цикл замість Select: незнайомі поля треба класти під Id ГОТОВОЇ сутності.
        // Для зв'язків це не те саме, що Id у файлі — порожній Id мапер замінює на новий.
        if (dto.Persons is { } persons)
        {
            foreach (var personDto in persons.OfType<PersonDto>())
            {
                var person = ToDomain(personDto);
                document.Persons.Add(person);
                DocumentExtras.Remember(extras.Persons, person.Id, personDto.Extra);
                DocumentExtras.Remember(extras.PersonBirths, person.Id, personDto.Birth?.Extra);
                DocumentExtras.Remember(extras.PersonDeaths, person.Id, personDto.Death?.Extra);
            }
        }

        if (dto.ParentChildLinks is { } parentChildLinks)
        {
            foreach (var linkDto in parentChildLinks.OfType<ParentChildLinkDto>())
            {
                var link = ToDomain(linkDto);
                document.ParentChildLinks.Add(link);
                DocumentExtras.Remember(extras.ParentChildLinks, link.Id, linkDto.Extra);
            }
        }

        if (dto.SpouseLinks is { } spouseLinks)
        {
            foreach (var linkDto in spouseLinks.OfType<SpouseLinkDto>())
            {
                var link = ToDomain(linkDto);
                document.SpouseLinks.Add(link);
                DocumentExtras.Remember(extras.SpouseLinks, link.Id, linkDto.Extra);
            }
        }

        return document;
    }

    private static PersonDto ToDto(Person p, DocumentExtras extras) => new()
    {
        Id = p.Id,
        LastName = p.LastName,
        FirstName = p.FirstName,
        Gender = p.Gender,
        MiddleName = p.MiddleName,
        MaidenName = p.MaidenName,
        Birth = ToDto(p.Birth, DocumentExtras.Lookup(extras.PersonBirths, p.Id)),
        Death = ToDto(p.Death, DocumentExtras.Lookup(extras.PersonDeaths, p.Id)),
        Deceased = p.Deceased,
        PhotoPath = p.PhotoPath,
        PhotoThumbnail = p.PhotoThumbnail,
        Notes = p.Notes,
        Facts = ToDto(p.Facts),
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        Extra = DocumentExtras.Lookup(extras.Persons, p.Id),
    };

    private static Person ToDomain(PersonDto d) => new()
    {
        Id = d.Id,
        LastName = d.LastName ?? string.Empty,
        FirstName = d.FirstName ?? string.Empty,
        Gender = d.Gender,
        MiddleName = d.MiddleName,
        MaidenName = d.MaidenName,
        Birth = ToDomain(d.Birth),
        Death = ToDomain(d.Death),
        Deceased = d.Deceased,
        PhotoPath = d.PhotoPath,
        PhotoThumbnail = d.PhotoThumbnail,
        Notes = d.Notes,
        Facts = ToDomain(d.Facts),
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
    };

    // ---- Події: PersonEvent ↔ PersonEventDto (схема v3) ----------------------

    private static PersonEventDto? ToDto(PersonEvent? e, Dictionary<string, JsonElement>? extra)
    {
        // Порожньої події в моделі не буває (інваріант PersonEvent), тож null тут означає
        // «нічого не відомо» — і в такому разі об'єкта у файлі немає взагалі. Виняток —
        // незнайомі поля з файлу: якщо вони є, подія у файлі БУЛА, і викидати її вміст
        // не можна, навіть коли все знайоме в ній порожнє.
        if (e is null)
        {
            return extra is { Count: > 0 } ? new PersonEventDto { Extra = extra } : null;
        }

        return new PersonEventDto
        {
            Date = ToDto(e.Date),
            Place = e.Place,
            Note = e.Note,
            Extra = extra,
        };
    }

    // Через фабрику, а не конструктор: вона тримає інваріант «порожньої події не буває»,
    // тож {"birth":{}} чи {"birth":{"place":"  "}} з чужого файлу стає null, а не порожнім
    // об'єктом, який потім поїхав би назад у файл.
    private static PersonEvent? ToDomain(PersonEventDto? d) =>
        d is null ? null : PersonEvent.Create(ToDomain(d.Date), d.Place, d.Note);

    // ---- Життєві факти: PersonFact ↔ PersonFactDto (OCCU/RESI) --------------

    private const string FactKindOccupation = "occupation";
    private const string FactKindResidence = "residence";

    /// <summary>Запасна назва виду, коли факт має <c>Other</c> без збереженого <c>Label</c>.</summary>
    private const string FactKindOther = "other";

    private static List<PersonFactDto?>? ToDto(List<PersonFact> facts)
    {
        // null, а не порожній список: "facts": [] у кожній особі без фактів — це
        // зайвий рядок у файлі, який позиціонується як людиночитний (пор. PhotoThumbnail).
        if (facts.Count == 0)
        {
            return null;
        }

        var list = new List<PersonFactDto?>(facts.Count);

        foreach (var fact in facts)
        {
            list.Add(new PersonFactDto
            {
                Kind = KindToString(fact),
                Value = fact.Value,
                Date = ToDto(fact.Date),
                Place = fact.Place,
            });
        }

        return list;
    }

    private static List<PersonFact> ToDomain(List<PersonFactDto?>? dtos)
    {
        var facts = new List<PersonFact>();

        if (dtos is null)
        {
            return facts;
        }

        // OfType<T>() відкидає null-елементи масиву — так само, як для осіб і зв'язків.
        foreach (var dto in dtos.OfType<PersonFactDto>())
        {
            var (kind, label) = ParseKind(dto.Kind);

            var fact = new PersonFact
            {
                Kind = kind,
                Label = label,
                Value = dto.Value,
                Date = ToDomain(dto.Date),
                Place = dto.Place,
            };

            // Запис без виду, місця й дати не несе інформації: у чужому чи ручному
            // файлі це шум, у нашому — не з'являється. Мовчки пропускаємо: втрачати
            // тут нічого, тож і повідомляти користувачу нема про що.
            if (!fact.IsEmpty)
            {
                facts.Add(fact);
            }
        }

        return facts;
    }

    private static string KindToString(PersonFact fact) => fact.Kind switch
    {
        PersonFactKind.Occupation => FactKindOccupation,
        PersonFactKind.Residence => FactKindResidence,

        // Label тримає оригінальну назву виду з файлу новішої збірки — повертаємо
        // її незміненою, щоб round-trip нічого не загубив.
        _ => string.IsNullOrWhiteSpace(fact.Label) ? FactKindOther : fact.Label,
    };

    private static (PersonFactKind Kind, string? Label) ParseKind(string? raw)
    {
        var kind = raw?.Trim();

        if (string.Equals(kind, FactKindOccupation, StringComparison.OrdinalIgnoreCase))
        {
            return (PersonFactKind.Occupation, null);
        }

        if (string.Equals(kind, FactKindResidence, StringComparison.OrdinalIgnoreCase))
        {
            return (PersonFactKind.Residence, null);
        }

        return (PersonFactKind.Other, string.IsNullOrWhiteSpace(kind) ? null : kind);
    }

    private static ParentChildLinkDto ToDto(ParentChildLink l, DocumentExtras extras) => new()
    {
        Id = l.Id,
        ParentId = l.ParentId,
        ChildId = l.ChildId,
        ParentRole = l.ParentRole,
        Extra = DocumentExtras.Lookup(extras.ParentChildLinks, l.Id),
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

    private static SpouseLinkDto ToDto(SpouseLink l, DocumentExtras extras) => new()
    {
        Id = l.Id,
        Person1Id = l.Person1Id,
        Person2Id = l.Person2Id,
        MarriageDate = ToDto(l.MarriageDate),
        MarriagePlace = l.MarriagePlace,
        DivorceDate = ToDto(l.DivorceDate),
        Divorced = l.Divorced,
        Extra = DocumentExtras.Lookup(extras.SpouseLinks, l.Id),
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
            MarriagePlace = d.MarriagePlace,
            DivorceDate = ToDomain(d.DivorceDate),
            Divorced = d.Divorced,
        }
        : new SpouseLink
        {
            Id = d.Id,
            Person1Id = d.Person1Id,
            Person2Id = d.Person2Id,
            MarriageDate = ToDomain(d.MarriageDate),
            MarriagePlace = d.MarriagePlace,
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
