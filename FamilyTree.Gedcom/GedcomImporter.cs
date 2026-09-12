using System.Globalization;
using FamilyTree.Domain;
using FamilyTree.Storage;
using FamilyTree.Storage.Serialization;

namespace FamilyTree.Gedcom;

/// <summary>
/// Імпорт GEDCOM у документ родини (T-5.2, Частина 3).
///
/// <para>
/// Результат — <b>новий</b> <see cref="FamilyDocument"/>: за рішенням у специфікації
/// імпорт не зливається з відкритою родиною (у GEDCOM дата народження часто лише з роком,
/// і ключ злиття «ПІБ + дата» давав би хибні збіги). Злити можна потім наявною
/// командою «Імпорт» — уже з `.familytree`.
/// </para>
///
/// <para>
/// Чистка графа делегована <see cref="DocumentIntegrity"/> — тому самому, що чистить
/// чужі `.familytree`. Він відкидає висячі й самозв'язки, дублі, цикли «батько-дитина»,
/// зайвих біологічних батьків, структурно биті дати й небезпечні шляхи, причому за один
/// прохід по графу. Перевіряти кожне ребро окремо через <c>RelationshipValidator</c>
/// (як це робить <c>FamilyMerger</c>) тут не варто: валідатор щоразу перебудовує індекс
/// осіб, і на файлі в тисячі записів це квадратично.
/// </para>
/// </summary>
public static class GedcomImporter
{
    /// <summary>
    /// Теги, які профіль споживає, — <b>кваліфікованими шляхами</b> від запису
    /// (<c>INDI</c> або <c>FAM</c>). Решта потрапляє у звіт як пропущені.
    /// </summary>
    /// <remarks>
    /// Шляхи, а не голі імена тегів: той самий <c>PLAC</c> під <c>BIRT</c> читається,
    /// а під <c>DEAT</c> — ні, і плоский список цього не розрізняв. Через це
    /// <c>DEAT.PLAC</c> і <c>DEAT.NOTE</c> мовчки вважалися спожитими: дані губилися,
    /// а звіт про це не казав. Кожен рядок тут мусить відповідати місцю в коді, яке
    /// цей тег справді читає — інакше повертається та сама мовчазна втрата.
    /// </remarks>
    private static readonly HashSet<string> ConsumedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        // Ім'я. TYPE — для запасного шляху «окремий NAME з TYPE married».
        "NAME", "NAME.GIVN", "NAME.SURN", "NAME._MARNM", "NAME.TYPE",

        "SEX",

        // Народження й смерть — дата, місце й нотатка при кожній події.
        // DIV.PLAC свідомо НЕ тут: у реальних файлах він не трапляється, поля в моделі
        // немає, і хай звіт про нього чесно повідомляє, якщо колись трапиться.
        "BIRT", "BIRT.DATE", "BIRT.PLAC", "BIRT.NOTE",
        "DEAT", "DEAT.DATE", "DEAT.PLAC", "DEAT.NOTE",

        // Життєві факти (PersonFact). ADDR — звідки RESI/OCCU беруть місце, коли
        // PLAC відсутній; у чужих файлах це звичайна річ. NOTE читається лише під
        // RESI: у професії пояснення нікуди покласти.
        "OCCU", "OCCU.DATE", "OCCU.PLAC", "OCCU.ADDR",
        "OCCU.ADDR.ADR1", "OCCU.ADDR.CITY", "OCCU.ADDR.STAE", "OCCU.ADDR.POST", "OCCU.ADDR.CTRY",
        "RESI", "RESI.DATE", "RESI.PLAC", "RESI.NOTE", "RESI.ADDR",
        "RESI.ADDR.ADR1", "RESI.ADDR.CITY", "RESI.ADDR.STAE", "RESI.ADDR.POST", "RESI.ADDR.CTRY",

        "NOTE",
        "FAMC", "FAMC.PEDI", "FAMS",
        "CHAN", "CHAN.DATE", "CHAN.DATE.TIME",
        "_UID",

        // FAM. _FREL/_MREL — єдине, що задає роль окремо для батька й матері.
        "HUSB", "WIFE",
        "CHIL", "CHIL._FREL", "CHIL._MREL",
        "MARR", "MARR.DATE", "MARR.PLAC",
        "DIV", "DIV.DATE",
    };

    /// <summary>Складові <c>ADDR</c> у порядку, у якому вони склеюються в один рядок місця.</summary>
    private static readonly string[] AddressParts = { "ADR1", "CITY", "STAE", "POST", "CTRY" };

    private static readonly string[] MonthAbbreviations =
        { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    public static FamilyDocument Import(byte[] bytes, out GedcomImportReport report) =>
        Import(GedcomReader.Read(bytes), out report);

    public static FamilyDocument Import(GedcomFile file, out GedcomImportReport report)
    {
        ArgumentNullException.ThrowIfNull(file);

        var counters = new Counters();
        var document = FamilyDocument.CreateNew(TitleOf(file));

        var byXref = ReadIndividuals(file, document, counters);
        var pedigree = ReadPedigree(file);
        var families = ReadFamilies(file, document, byXref, pedigree, counters);

        // Чистка чужого графа — тим самим механізмом, що й для чужих .familytree.
        var repaired = DocumentIntegrity.Verify(document);
        document.RepairedIssues = repaired;

        report = new GedcomImportReport
        {
            Persons = document.Persons.Count,
            Families = families,
            ParentChildLinks = document.ParentChildLinks.Count,
            SpouseLinks = document.SpouseLinks.Count,
            UnnamedPersons = counters.Unnamed,
            DeathsWithoutDate = counters.DeathsWithoutDate,
            TextOnlyDates = counters.TextDates,
            SkippedRecords = counters.SkippedRecords,
            MalformedLines = file.Info.MalformedLines,
            SkippedTags = CountSkippedTags(file),
            EncodingName = file.Info.EncodingName,
            EncodingFallbackFrom = file.Info.EncodingFallbackFrom,
            Version = file.Info.Version,
            Source = file.Info.Source,
            RepairedIssues = repaired,
        };

        // MarkChanged, а не IsDirty = true: прапорець і ревізія мусять рухатися разом (B-11).
        document.MarkChanged();
        return document;
    }

    // ------------------------------------------------------------------ INDI

    private static Dictionary<string, Person> ReadIndividuals(
        GedcomFile file, FamilyDocument document, Counters counters)
    {
        var byXref = new Dictionary<string, Person>(StringComparer.OrdinalIgnoreCase);
        var usedIds = new HashSet<Guid>();

        foreach (var indi in file.Records("INDI"))
        {
            if (indi.Xref is not { Length: > 0 } xref || byXref.ContainsKey(xref))
            {
                counters.SkippedRecords++;
                continue;
            }

            var person = ReadPerson(indi, usedIds, counters);
            byXref[xref] = person;
            document.Persons.Add(person);
        }

        return byXref;
    }

    private static Person ReadPerson(GedcomNode indi, HashSet<Guid> usedIds, Counters counters)
    {
        var name = ParseName(indi, counters);

        var birth = ReadDate(indi.Path("BIRT", "DATE"), counters);
        var death = ReadDate(indi.Path("DEAT", "DATE"), counters);

        // Будь-який DEAT — «1 DEAT Y», порожній тег, або з самими підтегами — означає
        // смерть. Дата може бути невідома; цей стан тримає Person.Deceased.
        var deceased = indi.Child("DEAT") is not null;

        if (death is null && deceased)
        {
            counters.DeathsWithoutDate++;
        }

        var changed = ReadChange(indi) ?? DateTime.UtcNow;

        return new Person
        {
            Id = ReadId(indi, usedIds),
            LastName = name.LastName,
            FirstName = name.FirstName,
            MiddleName = name.MiddleName,
            MaidenName = name.MaidenName,
            Gender = indi.ChildValue("SEX")?.Trim().ToUpperInvariant() switch
            {
                "M" => Gender.Male,
                "F" => Gender.Female,
                _ => Gender.Unknown,
            },
            BirthDate = birth,
            BirthPlace = Clean(indi.Path("BIRT", "PLAC")),
            BirthNote = Clean(indi.Path("BIRT", "NOTE")),
            DeathDate = death,
            DeathPlace = Clean(indi.Path("DEAT", "PLAC")),
            DeathNote = Clean(indi.Path("DEAT", "NOTE")),
            Deceased = deceased,
            Notes = Clean(indi.ChildValue("NOTE")),
            Facts = ReadFacts(indi, counters),
            CreatedAt = changed,
            UpdatedAt = changed,
        };
    }

    /// <summary>
    /// Життєві факти особи: <c>OCCU</c> (професія) і <c>RESI</c> (проживання).
    /// Обидва теги повторювані, тож читаємо ВСІ входження в порядку з файлу —
    /// послідовність професій і переїздів має значення й відновити її потім нізвідки.
    /// </summary>
    /// <remarks>
    /// Розподіл полів іде за самим стандартом. У <c>OCCU</c> значення лежить у рядку
    /// тега (<c>1 OCCU Коваль</c>) — воно й стає <see cref="PersonFact.Value"/>.
    /// <c>RESI</c> у 5.5.1 — подія БЕЗ значення, усе несуть підтеги, тому місце
    /// береться з <c>PLAC</c>, а за його відсутності — з <c>ADDR</c>. Пояснення при
    /// проживанні (<c>NOTE</c>) кладемо у <see cref="PersonFact.Value"/>: інакше
    /// експорт не мав би куди його повернути.
    /// </remarks>
    private static List<PersonFact> ReadFacts(GedcomNode indi, Counters counters)
    {
        var facts = new List<PersonFact>();

        foreach (var node in indi.Children)
        {
            PersonFactKind kind;

            if (string.Equals(node.Tag, "OCCU", StringComparison.OrdinalIgnoreCase))
            {
                kind = PersonFactKind.Occupation;
            }
            else if (string.Equals(node.Tag, "RESI", StringComparison.OrdinalIgnoreCase))
            {
                kind = PersonFactKind.Residence;
            }
            else
            {
                continue;
            }

            var inline = Clean(node.Value);
            var place = Clean(node.ChildValue("PLAC")) ?? ReadAddress(node);

            string? value;

            if (kind == PersonFactKind.Occupation)
            {
                value = inline;
            }
            else
            {
                value = Clean(node.ChildValue("NOTE"));

                // «1 RESI Полтава» — не за стандартом (RESI має бути подією без значення),
                // але так пише чимало програм. Без цієї гілки місце просто зникало б:
                // факт лишався б порожнім і його відкинуло б як голий RESI.
                place ??= inline;
            }

            var fact = new PersonFact
            {
                Kind = kind,
                Value = value,
                Date = ReadDate(node.ChildValue("DATE"), counters),
                Place = place,
            };

            // Голий «1 RESI» без місця, дати й пояснення трапляється в чужих файлах
            // як залишок після редагування — інформації в ньому нуль.
            if (!fact.IsEmpty)
            {
                facts.Add(fact);
            }
        }

        return facts;
    }

    /// <summary>
    /// Місце зі структурованої адреси: власне значення <c>ADDR</c>, а якщо воно порожнє —
    /// склейка складових. Обидві форми законні, і програми пишуть то одну, то другу.
    /// </summary>
    private static string? ReadAddress(GedcomNode node)
    {
        if (node.Child("ADDR") is not { } address)
        {
            return null;
        }

        if (Clean(address.Value) is { } inline)
        {
            return inline;
        }

        var joined = string.Join(", ", AddressParts
            .Select(tag => Clean(address.ChildValue(tag)))
            .Where(part => part is not null));

        return Clean(joined);
    }

    /// <summary>
    /// Ідентичність: власний <c>_UID</c> робить зворотний імпорт нашого ж експорту точним.
    /// Чужий або зіпсований <c>_UID</c> (а також колізія) — новий Guid.
    /// </summary>
    private static Guid ReadId(GedcomNode indi, HashSet<Guid> usedIds)
    {
        var raw = indi.ChildValue("_UID")?.Trim();

        if (raw is not null && Guid.TryParse(raw, out var id) && usedIds.Add(id))
        {
            return id;
        }

        Guid fresh;
        do
        {
            fresh = Guid.CreateVersion7();
        }
        while (!usedIds.Add(fresh));

        return fresh;
    }

    /// <summary>
    /// Розбір імені. Пріоритет — явні підтеги <c>GIVN</c>/<c>SURN</c>; якщо їх немає,
    /// прізвище береться зі слешів у значенні <c>NAME</c>.
    /// По батькові — усе, що в <c>GIVN</c> після першого пробілу (рішення зі специфікації).
    /// Дівоче прізвище: якщо є <c>_MARNM</c> — основне прізвище шлюбне, а <c>SURN</c> дівоче.
    /// </summary>
    private static NameParts ParseName(GedcomNode indi, Counters counters)
    {
        var nameNode = indi.ChildrenOf("NAME").FirstOrDefault();

        var surname = Clean(nameNode?.ChildValue("SURN"));
        var given = Clean(nameNode?.ChildValue("GIVN"));

        if (nameNode?.Value is { } raw && (surname is null || given is null))
        {
            var (fromSlashes, rest) = SplitNameValue(raw);
            surname ??= fromSlashes;
            given ??= rest;
        }

        // Шлюбне прізвище: або власний _MARNM, або окремий NAME з TYPE married.
        var married = Clean(nameNode?.ChildValue("_MARNM"))
            ?? Clean(indi.ChildrenOf("NAME")
                .FirstOrDefault(n => string.Equals(n.ChildValue("TYPE"), "married", StringComparison.OrdinalIgnoreCase))
                ?.ChildValue("SURN"));

        string? maiden = null;
        if (married is not null && !string.Equals(married, surname, StringComparison.Ordinal))
        {
            maiden = surname;
            surname = married;
        }

        string? middle = null;
        if (given is not null)
        {
            var space = given.IndexOf(' ', StringComparison.Ordinal);
            if (space > 0)
            {
                middle = given[(space + 1)..].Trim();
                given = given[..space];
            }
        }

        if (surname is null || given is null)
        {
            counters.Unnamed++;
        }

        return new NameParts(surname ?? "?", given ?? "?", middle, maiden);
    }

    /// <summary>«Іван Петрович /Коваленко/» → прізвище зі слешів + решта як ім'я.</summary>
    private static (string? Surname, string? Given) SplitNameValue(string value)
    {
        var open = value.IndexOf('/', StringComparison.Ordinal);
        if (open < 0)
        {
            var only = value.Trim();
            return (null, only.Length == 0 ? null : only);
        }

        var close = value.IndexOf('/', open + 1);
        var surname = close > open
            ? value[(open + 1)..close].Trim()
            : value[(open + 1)..].Trim();

        var given = (value[..open] + (close > open ? value[(close + 1)..] : string.Empty)).Trim();

        return (surname.Length == 0 ? null : surname, given.Length == 0 ? null : given);
    }

    // ------------------------------------------------------------------- FAM

    /// <summary>
    /// <c>PEDI</c> лежить не у <c>FAM.CHIL</c>, а в <c>INDI.FAMC</c> дитини, тож індекс
    /// «(особа, родина) → роль» доводиться будувати окремим проходом до розбору родин.
    /// </summary>
    private static Dictionary<(string Person, string Family), ParentRole> ReadPedigree(GedcomFile file)
    {
        var map = new Dictionary<(string, string), ParentRole>();

        foreach (var indi in file.Records("INDI"))
        {
            if (indi.Xref is not { Length: > 0 } xref)
            {
                continue;
            }

            foreach (var famc in indi.ChildrenOf("FAMC"))
            {
                if (famc.Pointer is not { Length: > 0 } family || famc.ChildValue("PEDI") is not { } pedi)
                {
                    continue;
                }

                map[(xref, family)] = pedi.Trim().ToUpperInvariant() switch
                {
                    "ADOPTED" => ParentRole.Adoptive,
                    "FOSTER" or "STEP" => ParentRole.Step,
                    _ => ParentRole.Biological,
                };
            }
        }

        return map;
    }

    private static int ReadFamilies(
        GedcomFile file,
        FamilyDocument document,
        Dictionary<string, Person> byXref,
        Dictionary<(string, string), ParentRole> pedigree,
        Counters counters)
    {
        var families = 0;

        foreach (var fam in file.Records("FAM"))
        {
            families++;

            var famXref = fam.Xref ?? string.Empty;
            var husband = Resolve(byXref, fam.ChildPointer("HUSB"));
            var wife = Resolve(byXref, fam.ChildPointer("WIFE"));

            foreach (var chil in fam.ChildrenOf("CHIL"))
            {
                if (Resolve(byXref, chil.Pointer) is not { } child)
                {
                    continue;
                }

                var childXref = chil.Pointer!;
                AddParent(document, husband, child, RoleOf(chil, "_FREL", pedigree, childXref, famXref));
                AddParent(document, wife, child, RoleOf(chil, "_MREL", pedigree, childXref, famXref));
            }

            ReadMarriages(document, fam, husband, wife, counters);
        }

        return families;
    }

    /// <summary>
    /// Роль батька щодо дитини: спершу власні теги <c>_FREL</c>/<c>_MREL</c> у <c>FAM.CHIL</c>
    /// (вони єдині розрізняють батька й матір окремо), далі <c>PEDI</c> дитини, далі — рідний.
    /// </summary>
    private static ParentRole RoleOf(
        GedcomNode chil,
        string relationTag,
        Dictionary<(string, string), ParentRole> pedigree,
        string childXref,
        string famXref)
    {
        if (chil.ChildValue(relationTag) is { } relation)
        {
            return relation.Trim().ToUpperInvariant() switch
            {
                "ADOPTED" => ParentRole.Adoptive,
                "STEP" or "FOSTER" => ParentRole.Step,
                _ => ParentRole.Biological,
            };
        }

        return pedigree.TryGetValue((childXref, famXref), out var role) ? role : ParentRole.Biological;
    }

    private static void AddParent(FamilyDocument document, Person? parent, Person child, ParentRole role)
    {
        if (parent is null)
        {
            return;
        }

        document.ParentChildLinks.Add(new ParentChildLink
        {
            ParentId = parent.Id,
            ChildId = child.Id,
            ParentRole = role,
        });
    }

    /// <summary>
    /// Подружжя створюється <b>лише</b> за наявності <c>MARR</c> або <c>DIV</c>:
    /// <c>FAM</c> сам собою шлюбу не означає, а спільні батьки без шлюбу однаково
    /// відновляться з дітей при зворотному експорті.
    /// </summary>
    private static void ReadMarriages(
        FamilyDocument document, GedcomNode fam, Person? husband, Person? wife, Counters counters)
    {
        var marriages = fam.ChildrenOf("MARR").ToList();
        var divorces = fam.ChildrenOf("DIV").ToList();

        if (marriages.Count == 0 && divorces.Count == 0)
        {
            return;
        }

        if (husband is null || wife is null)
        {
            counters.SkippedRecords++;
            return;
        }

        // GEDCOM не пов'язує конкретний DIV із конкретним MARR, тож пари складаються
        // за порядком. Для типового файлу (один шлюб, або «шлюб-розлучення-шлюб», який
        // пише наш же експортер) це точно; для екзотики на кшталт «перший шлюб без
        // розлучення, другий із розлученням» дата розлучення може прилипнути не до того
        // шлюбу. Кращого критерію формат не дає.
        var count = Math.Max(marriages.Count, Math.Max(divorces.Count, 1));
        for (var i = 0; i < count; i++)
        {
            var marriageNode = i < marriages.Count ? marriages[i] : null;
            var marriage = ReadDate(marriageNode?.ChildValue("DATE"), counters);
            var divorceNode = i < divorces.Count ? divorces[i] : null;
            var divorce = ReadDate(divorceNode?.ChildValue("DATE"), counters);

            document.SpouseLinks.Add(SpouseLink.Create(
                husband.Id,
                wife.Id,
                marriage,
                divorce,
                divorced: divorceNode is not null && divorce is null,
                marriagePlace: Clean(marriageNode?.ChildValue("PLAC"))));
        }
    }

    // ---------------------------------------------------------------- Спільне

    private static FamilyDate? ReadDate(string? raw, Counters counters)
    {
        var date = GedcomDateMapper.FromGedcom(raw);

        if (date?.OriginalGedcom is not null)
        {
            counters.TextDates++;
        }

        return date;
    }

    /// <summary><c>CHAN.DATE</c> + <c>CHAN.TIME</c> → мітка часу оновлення (best effort).</summary>
    private static DateTime? ReadChange(GedcomNode indi)
    {
        if (indi.Path("CHAN", "DATE") is not { } raw)
        {
            return null;
        }

        var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
        {
            return null;
        }

        var month = Array.FindIndex(MonthAbbreviations, m => string.Equals(m, parts[1], StringComparison.OrdinalIgnoreCase)) + 1;
        if (month == 0)
        {
            return null;
        }

        try
        {
            var stamp = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

            if (indi.Child("CHAN")?.Child("DATE")?.ChildValue("TIME") is { } time
                && TimeSpan.TryParse(time.Trim(), CultureInfo.InvariantCulture, out var offset))
            {
                stamp = stamp.Add(offset);
            }

            return stamp;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static Person? Resolve(Dictionary<string, Person> byXref, string? pointer) =>
        pointer is { Length: > 0 } && byXref.TryGetValue(pointer, out var person) ? person : null;

    private static string TitleOf(GedcomFile file)
    {
        var head = file.Root.Child("HEAD");
        return Clean(head?.ChildValue("NOTE"))
            ?? Clean(head?.ChildValue("SOUR"))
            ?? "GEDCOM";
    }

    private static IReadOnlyDictionary<string, int> CountSkippedTags(GedcomFile file)
    {
        var skipped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in file.Records("INDI").Concat(file.Records("FAM")))
        {
            foreach (var path in record.UnknownDescendantPaths(ConsumedTags.Contains))
            {
                skipped[path] = skipped.GetValueOrDefault(path) + 1;
            }
        }

        return skipped;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record NameParts(string LastName, string FirstName, string? MiddleName, string? MaidenName);

    private sealed class Counters
    {
        public int Unnamed;
        public int DeathsWithoutDate;
        public int TextDates;
        public int SkippedRecords;
    }
}
