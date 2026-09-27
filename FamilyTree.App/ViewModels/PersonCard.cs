using System.Globalization;
using System.Windows.Media;
using FamilyTree.App.Localization;
using FamilyTree.App.Services;
using FamilyTree.App.Settings;
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
    /// <summary>
    /// Ширина декодування фото для картки: сама картка показує 92×112, запас — на 150% DPI.
    /// Не <c>private</c> навмисно: значення читає складальник <see cref="PersonCardBuilder"/>
    /// (окремий клас у цьому ж файлі), а живе воно тут, бо описує саме цю картку.
    /// </summary>
    internal const int CardPhotoWidth = 220;

    /// <summary>Особа, до якої належить картка (для команд і вибору).</summary>
    public required Person Person { get; init; }

    public string FullName => Person.FullName;

    /// <summary>
    /// Id зв'язку подружжя для рядка списку «Подружжя» (<c>Guid.Empty</c> — картка не про шлюб).
    /// Пари осіб для пошуку зв'язку НЕ досить: у пари може бути кілька шлюбів (B-16), і рядок
    /// мусить знати СВІЙ — інакше «Редагувати» відкриває чужий, а «Видалити» зносить обидва (B-67).
    /// </summary>
    public Guid SpouseLinkId { get; init; }

    /// <summary>
    /// Період шлюбу для підпису рядка. Заповнюється лише тоді, коли та сама особа трапляється
    /// в списку «Подружжя» двічі (повторний шлюб): два однакові рядки інакше не розрізнити.
    /// </summary>
    public string? SpousePeriod { get; init; }

    /// <summary>Підпис рядка списку: ім'я, а за потреби — з періодом шлюбу.</summary>
    public string RowTitle => SpousePeriod is null ? FullName : $"{FullName} ({SpousePeriod})";

    /// <summary>Роки життя «1980–2021» (порожньо, якщо дат немає).</summary>
    public string Years { get; init; } = string.Empty;

    /// <summary>Родинний зв'язок відносно кореня/вибраної особи (бейдж).</summary>
    public string? RelationBadge { get; init; }

    /// <summary>Абсолютний шлях до фото у теці даних (null — файлу немає).</summary>
    public string? PhotoPath { get; init; }

    /// <summary>
    /// Чи виділяти в картці місце під фото. Окремо від <see cref="Photo"/>: коли фото
    /// увімкнене, але в конкретної особи його немає, рамка-заглушка лишається — інакше
    /// картки сусідніх людей мали б різну ширину й «стрибали» при наведенні.
    /// </summary>
    public bool ShowPhoto { get; init; } = true;

    /// <summary>
    /// Готове зображення для показу: файл із теки даних, а якщо його немає —
    /// мініатюра з документа. Саме через це відкритий на чужій машині файл показує
    /// людей із обличчями: оригіналів там немає, а мініатюри подорожують разом із ним.
    /// </summary>
    public ImageSource? Photo { get; init; }

    public string? DetailMaiden { get; init; }

    public string? DetailGender { get; init; }

    public string? DetailBirth { get; init; }

    public string? DetailDeath { get; init; }

    public string? DetailMarriage { get; init; }

    public string? DetailChildren { get; init; }

    /// <summary>
    /// Життєві факти одним рядком. Власного підпису не має, бо кожен факт уже
    /// починається з назви свого виду («Професія: коваль»).
    /// </summary>
    public string? DetailFacts { get; init; }

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
    private readonly ISettingsService _settings;

    /// <param name="settings">
    /// Читається на КОЖНУ збірку картки, а не запам'ятовується в полі-знімку: вікно
    /// налаштувань міняє прапорці вживо й одразу просить перебудувати картки, тож
    /// збирач мусить бачити поточний стан, а не той, що був при створенні.
    /// </param>
    public PersonCardBuilder(ILocalizationService localization, ISettingsService settings)
    {
        _localization = localization;
        _settings = settings;
    }

    /// <param name="childrenCount">
    /// Кількість дітей. Передається зовні, бо викликачі вже мають дешеве джерело
    /// (граф або словник), і рахувати links на кожну картку було б O(n·m).
    /// </param>
    /// <param name="spouseLinkId">
    /// Для рядка списку «Подружжя» — Id ЙОГО зв'язку; решта викликів лишають <c>default</c>.
    /// </param>
    /// <param name="spousePeriod">
    /// Період шлюбу для підпису рядка, коли ту саму особу треба показати двічі (повторний шлюб).
    /// </param>
    public PersonCard Build(
        Person person,
        FamilyDocument doc,
        IReadOnlyDictionary<Guid, Person> persons,
        int childrenCount,
        string? relationBadge = null,
        Guid spouseLinkId = default,
        string? spousePeriod = null)
    {
        // Вимкнений рядок стає null — і шаблон ховає його тим самим NullToCollapsedConverter,
        // яким уже ховає порожні. Тому налаштування змісту не додали в XAML жодної гілки:
        // «поля немає» й «поле вимкнули» для картки — та сама ситуація.
        var o = _settings.Current.Cards.PersonTooltip;

        return new PersonCard
        {
            Person = person,
            SpouseLinkId = spouseLinkId,
            SpousePeriod = spousePeriod,
            Years = FormatYears(person),
            RelationBadge = o.ShowRelationBadge ? relationBadge : null,
            PhotoPath = ResolvePhoto(person.PhotoPath),
            ShowPhoto = o.ShowPhoto,

            // Фото не вантажимо взагалі, коли воно вимкнене: декодування — найдорожча
            // частина збірки картки, а на великому дереві їх будують сотнями.
            Photo = o.ShowPhoto ? PersonPhoto.Load(person, PersonCard.CardPhotoWidth) : null,
            DetailMaiden = o.ShowMaidenName ? Line("Person_MaidenName", person.MaidenName) : null,
            DetailGender = o.ShowGender ? Line("Person_Gender", GenderText(person.Gender)) : null,
            DetailBirth = o.ShowBirth ? Line("Person_BirthDate", FormatBirth(person)) : null,
            DetailDeath = o.ShowDeath && !person.IsAlive
                ? Line("Person_DeathDate", FormatDeath(person))
                : null,
            DetailMarriage = o.ShowMarriages
                ? Line("Tree_Card_Marriage", FormatMarriages(person, doc, persons))
                : null,
            DetailChildren = o.ShowChildrenCount
                ? Line("Tree_Card_Children", childrenCount.ToString(CultureInfo.CurrentCulture))
                : null,
            DetailFacts = o.ShowFacts && FormatFacts(person) is { Length: > 0 } facts ? facts : null,
            DetailNotes = o.ShowNotes ? Line("Person_Notes", person.Notes) : null,
        };
    }

    /// <summary>
    /// Локалізована назва виду факту. Для виду з новішої збірки показуємо його власну
    /// назву, а не «Інше»: вона єдине, що про цей факт відомо.
    /// </summary>
    public string FactKindText(PersonFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        return fact.Kind switch
        {
            PersonFactKind.Occupation => _localization.GetString("PersonFactKind_Occupation"),
            PersonFactKind.Residence => _localization.GetString("PersonFactKind_Residence"),
            _ => string.IsNullOrWhiteSpace(fact.Label)
                ? _localization.GetString("PersonFactKind_Other")
                : fact.Label,
        };
    }

    /// <summary>Один факт рядком: «Професія: коваль, Полтава (1970–1985)».</summary>
    public string FormatFact(PersonFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var parts = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(fact.Value))
        {
            parts.Add(fact.Value.Trim());
        }

        if (!string.IsNullOrWhiteSpace(fact.Place))
        {
            parts.Add(fact.Place.Trim());
        }

        var body = string.Join(", ", parts);
        var date = FormatDate(fact.Date);

        if (date.Length > 0)
        {
            body = body.Length > 0 ? $"{body} ({date})" : date;
        }

        var kind = FactKindText(fact);
        return body.Length > 0 ? $"{kind}: {body}" : kind;
    }

    /// <summary>Усі факти особи в один рядок — так само, як подружжя, через «; ».</summary>
    public string FormatFacts(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return string.Join("; ", person.Facts.Select(FormatFact));
    }

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
    /// <param name="surnameFirst">
    /// <c>false</c> дає «Ім'я Прізвище». Порядок налаштовується, бо в дереві, впорядкованому
    /// за родами, зручніше читати прізвище першим, а в невеликій родині — навпаки.
    /// </param>
    public static string FormatNamePrimary(Person person, bool surnameFirst = true)
    {
        ArgumentNullException.ThrowIfNull(person);

        var parts = surnameFirst
            ? new[] { person.LastName, person.FirstName }
            : new[] { person.FirstName, person.LastName };

        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>По батькові окремим рядком вузла; null — рядок ховається.</summary>
    public static string? FormatPatronymic(Person person) =>
        string.IsNullOrWhiteSpace(person.MiddleName) ? null : person.MiddleName;

    /// <summary>
    /// Роки життя для підпису вузла: «1980–2021», «1980», «–2021» або порожньо.
    /// Відкритий кінець «1980–» означає «помер, рік невідомий» (<see cref="Person.Deceased"/>) —
    /// інакше така особа виглядала б у дереві точно як жива.
    /// </summary>
    public static string FormatYears(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);

        var birth = person.Birth?.Date?.EffectiveYear?.ToString(CultureInfo.InvariantCulture);
        var death = person.Death?.Date?.EffectiveYear?.ToString(CultureInfo.InvariantCulture);
        return (birth, death) switch
        {
            (null, null) => string.Empty,
            (not null, null) => person.IsAlive ? birth! : $"{birth}–",
            (null, not null) => $"–{death}",
            _ => $"{birth}–{death}",
        };
    }

    // Показ дати за її типом (T-5.2a): точна/часткова/приблизна/діапазон/фраза.
    // Культура — поточна мова UI (її виставляє LocalizationService).
    public static string FormatDate(FamilyDate? date) =>
        FamilyDateFormatter.Format(date, CultureInfo.CurrentCulture);

    /// <summary>
    /// Дата смерті + місце, дзеркально до <see cref="FormatBirth"/>: «10.03.1861 · Санкт-Петербург».
    /// Порожньою не буває: особа, позначена померлою без дати, мусить показати хоч
    /// «невідома», інакше <see cref="Line"/> віддасть null і рядок про смерть зникне.
    /// </summary>
    public string FormatDeath(Person person)
    {
        var date = FormatDate(person.Death?.Date);

        if (date.Length == 0)
        {
            date = _localization.GetString("Person_DateUnknown");
        }

        var place = person.Death?.Place;
        return string.IsNullOrWhiteSpace(place) ? date : $"{date} · {place}";
    }

    /// <summary>Дата народження + місце (якщо є): «01.01.1980 · Київ».</summary>
    public static string FormatBirth(Person person)
    {
        var date = FormatDate(person.Birth?.Date);
        var place = person.Birth?.Place;
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
        var from = link.MarriageDate?.EffectiveYear?.ToString(CultureInfo.InvariantCulture);
        var to = link.DivorceDate?.EffectiveYear?.ToString(CultureInfo.InvariantCulture);
        return (from, to) switch
        {
            (null, null) => string.Empty,
            (not null, null) => from!,
            (null, not null) => $"… – {to}",
            _ => $"{from} – {to}",
        };
    }

    /// <summary>
    /// Абсолютний шлях до фото у теці даних. Саме правило живе в
    /// <see cref="PersonPhoto.Resolve"/> — тут лише делегат, щоб наявні виклики
    /// (і зовнішній код) не змінювалися.
    /// </summary>
    public static string? ResolvePhoto(string? relativePath) => PersonPhoto.Resolve(relativePath);
}
