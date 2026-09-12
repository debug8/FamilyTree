using System.Globalization;
using FamilyTree.Domain;
using FamilyTree.Storage;

namespace FamilyTree.Gedcom;

/// <summary>
/// Експорт документа родини в GEDCOM 5.5.1 (T-5.2, Частина 2).
/// Профіль тегів — «робочий» зі специфікації: покриває всі поля моделі, крім
/// <see cref="Person.PhotoPath"/> (медіа поза межами задачі).
/// </summary>
public static class GedcomExporter
{
    private const string SubmitterXref = "SUBM1";

    private static readonly string[] MonthAbbreviations =
        { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    /// <summary>Готовий до запису файл у байтах (UTF-8 з BOM, CRLF).</summary>
    public static byte[] Export(FamilyDocument document, string appVersion, DateTime? nowUtc = null) =>
        GedcomWriter.WriteBytes(BuildTree(document, appVersion, nowUtc));

    /// <summary>Те саме текстом — зручно для тестів і діагностики.</summary>
    public static string ExportText(FamilyDocument document, string appVersion, DateTime? nowUtc = null) =>
        GedcomWriter.Write(BuildTree(document, appVersion, nowUtc));

    /// <summary>
    /// Будує дерево записів. <paramref name="nowUtc"/> винесено в параметр навмисно:
    /// без нього <c>HEAD.DATE</c> робив би експорт невідтворюваним, і критерій
    /// «два експорти збігаються побайтово» неможливо було б перевірити тестом.
    /// </summary>
    public static GedcomNode BuildTree(FamilyDocument document, string appVersion, DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var model = GedcomFamilyBuilder.Build(document);
        var stamp = nowUtc ?? DateTime.UtcNow;

        var root = new GedcomNode(string.Empty);
        root.Add(BuildHead(document, appVersion, stamp));
        root.Add(BuildSubmitter(document));

        foreach (var person in model.Persons)
        {
            root.Add(BuildIndividual(person, model));
        }

        foreach (var family in model.Families)
        {
            root.Add(BuildFamily(family, model));
        }

        root.Add(new GedcomNode("TRLR"));
        return root;
    }

    private static GedcomNode BuildHead(FamilyDocument document, string appVersion, DateTime stamp)
    {
        var head = new GedcomNode("HEAD");

        var source = new GedcomNode("SOUR", value: "FamilyTree");
        source.Add(new GedcomNode("VERS", value: string.IsNullOrWhiteSpace(appVersion) ? "0.0.0" : appVersion));
        source.Add(new GedcomNode("NAME", value: "Family Tree"));
        head.Add(source);

        var date = new GedcomNode("DATE", value: FormatStampDate(stamp));
        date.Add(new GedcomNode("TIME", value: stamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
        head.Add(date);

        var gedc = new GedcomNode("GEDC");
        gedc.Add(new GedcomNode("VERS", value: "5.5.1"));
        gedc.Add(new GedcomNode("FORM", value: "LINEAGE-LINKED"));
        head.Add(gedc);

        head.Add(new GedcomNode("CHAR", value: "UTF-8"));
        head.Add(new GedcomNode("SUBM", pointer: SubmitterXref));

        if (!string.IsNullOrWhiteSpace(document.Meta.Title))
        {
            head.Add(new GedcomNode("NOTE", value: document.Meta.Title));
        }

        return head;
    }

    /// <summary>
    /// Запис <c>SUBM</c> формально обов'язковий у 5.5.1, і частина програм
    /// (зокрема Gramps) без нього лається. Змістовних даних про подавача ми не
    /// маємо, тож пишемо назву документа або назву застосунку.
    /// </summary>
    private static GedcomNode BuildSubmitter(FamilyDocument document)
    {
        var submitter = new GedcomNode("SUBM", xref: SubmitterXref);
        var name = string.IsNullOrWhiteSpace(document.Meta.Title) ? "FamilyTree" : document.Meta.Title;
        submitter.Add(new GedcomNode("NAME", value: name));
        return submitter;
    }

    private static GedcomNode BuildIndividual(Person person, GedcomModel model)
    {
        var indi = new GedcomNode("INDI", xref: model.XrefOf(person.Id));

        indi.Add(BuildName(person));
        indi.Add(new GedcomNode("SEX", value: person.Gender switch
        {
            Gender.Male => "M",
            Gender.Female => "F",
            _ => "U",
        }));

        var birth = BuildEvent(
            "BIRT", GedcomDateMapper.ToGedcom(person.BirthDate), person.BirthPlace, person.BirthNote);
        if (birth is not null)
        {
            indi.Add(birth);
        }

        var death = BuildEvent(
            "DEAT", GedcomDateMapper.ToGedcom(person.DeathDate), person.DeathPlace, person.DeathNote);
        if (death is not null)
        {
            indi.Add(death);
        }
        else if (person.Deceased)
        {
            // Стан «помер, і про смерть не відомо взагалі нічого» (Person.Deceased).
            // «Y» означає «подія була, подробиць немає» — так само, як у MARR/DIV нижче.
            // Гілка спрацьовує лише коли BuildEvent не дав вузла, тобто порожні ВСІ три
            // підтеги: «1 DEAT Y» з підтегом під ним було б суперечливим записом.
            indi.Add(new GedcomNode("DEAT", value: "Y"));
        }

        // Життєві факти — після подій народження/смерті й до посилань на родини,
        // у порядку, у якому їх тримає особа.
        foreach (var fact in person.Facts)
        {
            if (BuildFact(fact) is { } node)
            {
                indi.Add(node);
            }
        }

        if (!string.IsNullOrWhiteSpace(person.Notes))
        {
            indi.Add(new GedcomNode("NOTE", value: person.Notes));
        }

        foreach (var family in model.ChildFamilies(person.Id))
        {
            var famc = new GedcomNode("FAMC", pointer: family.Xref);

            // PEDI лише для нерідних: «birth» — і так усталений типовий випадок.
            if (family.Role != ParentRole.Biological)
            {
                famc.Add(new GedcomNode("PEDI", value: family.Role == ParentRole.Adoptive ? "adopted" : "foster"));
            }

            indi.Add(famc);
        }

        foreach (var family in model.SpouseFamilies(person.Id))
        {
            indi.Add(new GedcomNode("FAMS", pointer: family.Xref));
        }

        indi.Add(BuildChange(person.UpdatedAt));

        // Власний Guid — щоб зворотний імпорт відновив ідентичність точно,
        // а не вгадував за «ПІБ + дата народження».
        indi.Add(new GedcomNode("_UID", value: person.Id.ToString("D", CultureInfo.InvariantCulture)));

        return indi;
    }

    /// <summary>
    /// Ім'я за конвенцією 5.5.1: прізвище в слешах, по батькові — другим словом
    /// у <c>GIVN</c>. Для жінки з дівочим прізвищем основним стає дівоче
    /// (<c>SURN</c>), а шлюбне їде в <c>_MARNM</c>.
    /// </summary>
    private static GedcomNode BuildName(Person person)
    {
        var surname = string.IsNullOrWhiteSpace(person.MaidenName) ? person.LastName : person.MaidenName;

        var given = string.Join(
            ' ',
            new[] { person.FirstName, person.MiddleName }.Where(part => !string.IsNullOrWhiteSpace(part)));

        var name = new GedcomNode("NAME", value: $"{given} /{surname}/".Trim());

        if (!string.IsNullOrWhiteSpace(given))
        {
            name.Add(new GedcomNode("GIVN", value: given));
        }

        if (!string.IsNullOrWhiteSpace(surname))
        {
            name.Add(new GedcomNode("SURN", value: surname));
        }

        if (!string.IsNullOrWhiteSpace(person.MaidenName))
        {
            name.Add(new GedcomNode("_MARNM", value: person.LastName));
        }

        return name;
    }

    private static GedcomNode BuildFamily(GedcomFamily family, GedcomModel model)
    {
        var fam = new GedcomNode("FAM", xref: family.Xref);

        if (family.HusbandId is { } husband)
        {
            fam.Add(new GedcomNode("HUSB", pointer: model.XrefOf(husband)));
        }

        if (family.WifeId is { } wife)
        {
            fam.Add(new GedcomNode("WIFE", pointer: model.XrefOf(wife)));
        }

        foreach (var childId in family.ChildIds)
        {
            fam.Add(new GedcomNode("CHIL", pointer: model.XrefOf(childId)));
        }

        foreach (var marriage in family.Marriages)
        {
            var marriageDate = GedcomDateMapper.ToGedcom(marriage.MarriageDate);
            var marriagePlace = Trimmed(marriage.MarriagePlace);

            var marr = new GedcomNode("MARR");

            if (marriageDate is not null)
            {
                marr.Add(new GedcomNode("DATE", value: marriageDate));
            }

            if (marriagePlace is not null)
            {
                marr.Add(new GedcomNode("PLAC", value: marriagePlace));
            }

            if (marriageDate is null && marriagePlace is null)
            {
                // MARR без жодного підтегу програми ігнорують; «Y» означає
                // «подія була, подробиць немає».
                marr.Value = "Y";
            }

            fam.Add(marr);

            if (GedcomDateMapper.ToGedcom(marriage.DivorceDate) is { } divorceDate)
            {
                var div = new GedcomNode("DIV");
                div.Add(new GedcomNode("DATE", value: divorceDate));
                fam.Add(div);
            }
            else if (marriage.Divorced)
            {
                // Стан «розлучені, дата невідома» (SpouseLink.Divorced).
                fam.Add(new GedcomNode("DIV", value: "Y"));
            }
        }

        return fam;
    }

    /// <summary>
    /// Життєвий факт у тег 5.5.1. Дзеркало <c>GedcomImporter.ReadFacts</c>, і розподіл
    /// полів той самий: професія йде значенням тега (<c>1 OCCU Коваль</c>), а проживання
    /// значення не має — місце лягає в <c>PLAC</c>, пояснення в <c>NOTE</c>.
    /// </summary>
    /// <remarks>
    /// Проживання, у якого заповнене лише <see cref="PersonFact.Value"/> (для нього це
    /// нетипово — місце має бути в <see cref="PersonFact.Place"/>), пишеться як
    /// <c>PLAC</c>: краще нормалізувати, ніж мовчки загубити.
    /// <para>
    /// <see cref="PersonFactKind.Other"/> відповідника в профілі не має й у файл не йде.
    /// З'явитися такий факт може лише в документі, збереженому новішою збіркою.
    /// </para>
    /// </remarks>
    private static GedcomNode? BuildFact(PersonFact fact)
    {
        var tag = fact.Kind switch
        {
            PersonFactKind.Occupation => "OCCU",
            PersonFactKind.Residence => "RESI",
            _ => null,
        };

        if (tag is null)
        {
            return null;
        }

        var value = Trimmed(fact.Value);
        var place = Trimmed(fact.Place);
        var date = GedcomDateMapper.ToGedcom(fact.Date);

        string? note = null;

        if (fact.Kind == PersonFactKind.Residence)
        {
            // Значення тега RESI стандарт не передбачає, тож усе, що є, розкладаємо
            // по підтегах: місце — у PLAC, решта — у NOTE.
            if (place is null)
            {
                place = value;
            }
            else
            {
                note = value;
            }

            value = null;
        }

        if (value is null && place is null && date is null && note is null)
        {
            return null;
        }

        var node = new GedcomNode(tag, value: value);

        if (date is not null)
        {
            node.Add(new GedcomNode("DATE", value: date));
        }

        if (place is not null)
        {
            node.Add(new GedcomNode("PLAC", value: place));
        }

        if (note is not null)
        {
            node.Add(new GedcomNode("NOTE", value: note));
        }

        return node;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Подія особи: <c>DATE</c>, <c>PLAC</c>, <c>NOTE</c>. Повертає <see langword="null"/>,
    /// коли всі три порожні — тоді викликач вирішує, чи писати сам тег («1 DEAT Y»).
    /// </summary>
    private static GedcomNode? BuildEvent(string tag, string? date, string? place, string? note = null)
    {
        place = Trimmed(place);
        note = Trimmed(note);

        if (date is null && place is null && note is null)
        {
            return null;
        }

        var node = new GedcomNode(tag);

        if (date is not null)
        {
            node.Add(new GedcomNode("DATE", value: date));
        }

        if (place is not null)
        {
            node.Add(new GedcomNode("PLAC", value: place));
        }

        if (note is not null)
        {
            node.Add(new GedcomNode("NOTE", value: note));
        }

        return node;
    }

    private static GedcomNode BuildChange(DateTime updatedAt)
    {
        var stamp = updatedAt.Kind == DateTimeKind.Utc ? updatedAt : updatedAt.ToUniversalTime();

        var chan = new GedcomNode("CHAN");
        var date = new GedcomNode("DATE", value: FormatStampDate(stamp));
        date.Add(new GedcomNode("TIME", value: stamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
        chan.Add(date);
        return chan;
    }

    /// <summary>Дата запису у форматі GEDCOM: <c>3 SEP 2026</c>.</summary>
    private static string FormatStampDate(DateTime stamp) =>
        $"{stamp.Day.ToString(CultureInfo.InvariantCulture)} {MonthAbbreviations[stamp.Month - 1]} {stamp.Year.ToString(CultureInfo.InvariantCulture)}";
}
