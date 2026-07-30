using System.Globalization;
using System.IO;
using FamilyTree.App.Localization;
using FamilyTree.Domain;
using FamilyTree.Storage;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Дані великої картки особи (тултіп). Один набір полів для вузла дерева
/// й для рядка родича на вкладці «Особа», тож обидва місця показують
/// ту саму картку з тим самим шаблоном.
///
/// Порожні рядки — <c>null</c>: шаблон ховає їх через NullToCollapsedConverter.
/// </summary>
public sealed class PersonCard
{
    /// <summary>Особа, до якої належить картка (для команд і вибору).</summary>
    public required Person Person { get; init; }

    public string FullName => Person.FullName;

    /// <summary>Роки життя «1980–2021» (порожньо, якщо дат немає).</summary>
    public string Years { get; init; } = string.Empty;

    /// <summary>Родинний зв'язок відносно кореня/вибраної особи (бейдж).</summary>
    public string? RelationBadge { get; init; }

    /// <summary>Абсолютний шлях до фото (поки лише резолвинг — місце під фото).</summary>
    public string? PhotoPath { get; init; }

    public string? DetailMaiden { get; init; }

    public string? DetailGender { get; init; }

    public string? DetailBirth { get; init; }

    public string? DetailDeath { get; init; }

    public string? DetailMarriage { get; init; }

    public string? DetailChildren { get; init; }

    public string? DetailNotes { get; init; }
}

/// <summary>
/// Складає <see cref="PersonCard"/> і форматує його рядки. Виділено з
/// TreeViewModel, щоб вкладка «Особа» не дублювала ту саму логіку —
/// інакше дві картки того самого персонажа поволі роз'їхалися б.
/// </summary>
public sealed class PersonCardBuilder
{
    private readonly ILocalizationService _localization;

    public PersonCardBuilder(ILocalizationService localization) => _localization = localization;

    /// <param name="childrenCount">
    /// Кількість дітей. Передається зовні, бо викликачі вже мають дешеве джерело
    /// (граф або словник), і рахувати links на кожну картку було б O(n·m).
    /// </param>
    public PersonCard Build(
        Person person,
        FamilyDocument doc,
        IReadOnlyDictionary<Guid, Person> persons,
        int childrenCount,
        string? relationBadge = null) =>
        new()
        {
            Person = person,
            Years = FormatYears(person),
            RelationBadge = relationBadge,
            PhotoPath = ResolvePhoto(person.PhotoPath),
            DetailMaiden = Line("Person_MaidenName", person.MaidenName),
            DetailGender = Line("Person_Gender", GenderText(person.Gender)),
            DetailBirth = Line("Person_BirthDate", FormatBirth(person)),
            DetailDeath = person.IsAlive ? null : Line("Person_DeathDate", FormatDate(person.DeathDate)),
            DetailMarriage = Line("Tree_Card_Marriage", FormatMarriages(person, doc, persons)),
            DetailChildren = Line("Tree_Card_Children", childrenCount.ToString(CultureInfo.CurrentCulture)),
            DetailNotes = Line("Person_Notes", person.Notes),
        };

    /// <summary>Рядок картки «Підпис: значення» або null, якщо значення порожнє (рядок ховається).</summary>
    public string? Line(string labelKey, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{_localization.GetString(labelKey)}: {value}";

    public string GenderText(Gender gender) => gender switch
    {
        Gender.Male => _localization.GetString("Gender_Male"),
        Gender.Female => _localization.GetString("Gender_Female"),
        _ => _localization.GetString("Gender_Unknown"),
    };

    /// <summary>
    /// Перший рядок вузла дерева — «Прізвище Ім'я». Окремо від <see cref="Person.FullName"/>,
    /// бо по батькові виводиться наступним рядком.
    /// </summary>
    public static string FormatNamePrimary(Person person) =>
        string.Join(' ', new[] { person.LastName, person.FirstName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>По батькові окремим рядком вузла; null — рядок ховається.</summary>
    public static string? FormatPatronymic(Person person) =>
        string.IsNullOrWhiteSpace(person.MiddleName) ? null : person.MiddleName;

    /// <summary>Роки життя для підпису вузла: «1980–2021», «1980», «–2021» або порожньо.</summary>
    public static string FormatYears(Person person)
    {
        var birth = person.BirthDate?.Year.ToString(CultureInfo.InvariantCulture);
        var death = person.DeathDate?.Year.ToString(CultureInfo.InvariantCulture);
        return (birth, death) switch
        {
            (null, null) => string.Empty,
            (not null, null) => birth!,
            (null, not null) => $"–{death}",
            _ => $"{birth}–{death}",
        };
    }

    public static string FormatDate(DateOnly? date) =>
        date?.ToString("d", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>Дата народження + місце (якщо є): «01.01.1980 · Київ».</summary>
    public static string FormatBirth(Person person)
    {
        var date = FormatDate(person.BirthDate);
        var place = person.BirthPlace;
        return (date, hasPlace: !string.IsNullOrWhiteSpace(place)) switch
        {
            ("", false) => string.Empty,
            ("", true) => place!,
            (_, false) => date,
            _ => $"{date} · {place}",
        };
    }

    /// <summary>Подружжя: «Ім'я (рік шлюбу — рік розлучення)», кілька — через «; ».</summary>
    public static string FormatMarriages(
        Person person, FamilyDocument doc, IReadOnlyDictionary<Guid, Person> persons)
    {
        var parts = new List<string>();
        foreach (var link in doc.SpouseLinks.Where(l => l.Involves(person.Id)))
        {
            if (link.SpouseOf(person.Id) is not { } otherId
                || !persons.TryGetValue(otherId, out var other))
            {
                continue;
            }

            var period = FormatMarriagePeriod(link);
            parts.Add(period.Length > 0 ? $"{other.FullName} ({period})" : other.FullName);
        }

        return string.Join("; ", parts);
    }

    public static string FormatMarriagePeriod(SpouseLink link)
    {
        var from = link.MarriageDate?.Year.ToString(CultureInfo.InvariantCulture);
        var to = link.DivorceDate?.Year.ToString(CultureInfo.InvariantCulture);
        return (from, to) switch
        {
            (null, null) => string.Empty,
            (not null, null) => from!,
            (null, not null) => $"… – {to}",
            _ => $"{from} – {to}",
        };
    }

    /// <summary>Абсолютний шлях до фото у папці даних (поки лише резолвинг; місце під фото).</summary>
    public static string? ResolvePhoto(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        return Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FamilyTree", relativePath);
    }
}
