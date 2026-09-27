using System.Globalization;
using System.Windows.Media;
using FamilyTree.App.Localization;
using FamilyTree.App.Services;
using FamilyTree.App.Settings;
using FamilyTree.Domain;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Дані підказки рамки подружжя. Раніше це був один рядок, зібраний у TreeViewModel;
/// окремий тип потрібен, бо зміст підказки тепер налаштовується, а прапорці «показувати
/// фото» й «показувати місце» рядком не виражаються.
///
/// Порожні рядки — <c>null</c>: шаблон ховає їх через NullToCollapsedConverter,
/// рівно як у <see cref="PersonCard"/>.
/// </summary>
public sealed class CoupleCard
{
    /// <summary>
    /// Найменша ширина декодування: за типових налаштувань картка показує 64×78.
    /// Більший розмір у налаштуваннях піднімає це число.
    /// Менша за <see cref="PersonCard.CardPhotoWidth"/>, бо тут фото ДВА й обидва дрібніші.
    /// </summary>
    internal const int CouplePhotoWidth = 160;

    /// <summary>Ідентифікатор зв'язку, за яким зібрано картку (для звірки з рамкою).</summary>
    public Guid LinkId { get; init; }

    public string NameA { get; init; } = string.Empty;

    public string NameB { get; init; } = string.Empty;

    /// <summary>Роки життя першого — null, коли вимкнено або дат немає (рядок ховається).</summary>
    public string? YearsA { get; init; }

    public string? YearsB { get; init; }

    public ImageSource? PhotoA { get; init; }

    public ImageSource? PhotoB { get; init; }

    /// <summary>Чи виділяти місце під фото. Окремо від самих зображень: місце тримається
    /// й тоді, коли фото немає — інакше пара з одним фото виглядала б перекошеною.</summary>
    public bool ShowPhotos { get; init; }

    public string? DetailMarriageDate { get; init; }

    public string? DetailMarriagePlace { get; init; }

    public string? DetailDuration { get; init; }

    public string? DetailChildren { get; init; }

    /// <summary>Кегль обох імен.</summary>
    public double PrimaryFontSize { get; init; } = 13;

    /// <summary>Кегль років життя й рядків про шлюб.</summary>
    public double SecondaryFontSize { get; init; } = 12;

    /// <summary>
    /// Кегль серця між портретами. Похідний від імен, а не власне налаштування:
    /// фіксоване серце поруч із іменами на 28 виглядало б загубленим.
    /// </summary>
    public double HeartFontSize { get; init; } = 18;

    public double PhotoWidth { get; init; } = 64;

    public double PhotoHeight { get; init; } = 78;

    /// <summary>Стеля ширини картки — росте з кеглем імен.</summary>
    public double CardMaxWidth { get; init; } = 360;

    /// <summary>Стеля ширини колонки одного з подружжя.</summary>
    public double ColumnMaxWidth { get; init; } = 130;

    /// <summary>
    /// Підпис угорі спрощеної картки — «Колишнє подружжя». Для чинного шлюбу null:
    /// там про стан говорить сама рамка навколо пари, і підпис був би зайвим.
    /// </summary>
    public string? StatusText { get; init; }

    /// <summary>
    /// Роки в шлюбі одним рядком — «У шлюбі: 1974 – 1989». Для чинного шлюбу
    /// використовується <see cref="DetailMarriageDate"/> («У шлюбі з: …»), бо кінця ще немає.
    /// </summary>
    public string? DetailPeriod { get; init; }
}

/// <summary>
/// Складає <see cref="CoupleCard"/> за налаштуваннями. Окремий клас за зразком
/// <see cref="PersonCardBuilder"/>: тримає форматування подалі від TreeViewModel,
/// яка й так найбільша у проєкті.
/// </summary>
public sealed class CoupleCardBuilder
{
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settings;

    public CoupleCardBuilder(ILocalizationService localization, ISettingsService settings)
    {
        _localization = localization;
        _settings = settings;
    }

    /// <param name="childrenCount">
    /// Кількість СПІЛЬНИХ дітей пари. Передається зовні з тієї ж причини, що й у
    /// <see cref="PersonCardBuilder.Build"/>: викликач уже має граф, а рахувати зв'язки
    /// на кожну рамку було б O(n·m).
    /// </param>
    public CoupleCard Build(Person a, Person b, SpouseLink link, int childrenCount)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(link);

        var o = _settings.Current.Cards.CoupleTooltip;
        var primary = o.SafePrimaryFontSize;
        var secondary = o.SafeSecondaryFontSize;
        var columnMaxWidth = Math.Max(
            Math.Round(primary * 10),
            o.ShowPhotos ? Math.Ceiling(o.SafePhotoWidth) : 0);

        return new CoupleCard
        {
            PrimaryFontSize = primary,
            SecondaryFontSize = secondary,
            HeartFontSize = Math.Round(primary * 1.4),
            PhotoWidth = o.SafePhotoWidth,
            PhotoHeight = o.SafePhotoHeight,

            // Колонка мусить умістити портрет: при фото, ширшому за стелю з кегля,
            // воно вилазило б за межі своєї колонки. Типовий кегль 13 дає ті самі 130,
            // що були зашиті в шаблоні.
            ColumnMaxWidth = columnMaxWidth,

            // Стеля картки — сума двох колонок, серця з його полями (12+12) і
            // Padding 14×2. Рахується, а не береться з кегля: інакше більший портрет
            // упирався б у неї й обрізався замість того, щоб розсунути картку.
            CardMaxWidth = Math.Round(28 + (2 * columnMaxWidth) + (primary * 1.4 * 1.2) + 24),
            LinkId = link.Id,
            NameA = a.FullName,
            NameB = b.FullName,
            YearsA = o.ShowYears ? NullIfEmpty(PersonCardBuilder.FormatYears(a)) : null,
            YearsB = o.ShowYears ? NullIfEmpty(PersonCardBuilder.FormatYears(b)) : null,
            ShowPhotos = o.ShowPhotos,
            PhotoA = o.ShowPhotos ? PersonPhoto.Load(a, o.PhotoDecodeWidth(CoupleCard.CouplePhotoWidth)) : null,
            PhotoB = o.ShowPhotos ? PersonPhoto.Load(b, o.PhotoDecodeWidth(CoupleCard.CouplePhotoWidth)) : null,
            DetailMarriageDate = o.ShowMarriageDate ? FormatMarriageDate(link) : null,
            DetailMarriagePlace = o.ShowMarriagePlace
                ? Line("Couple_Place", link.MarriagePlace)
                : null,
            DetailDuration = o.ShowDuration ? FormatDuration(link) : null,
            DetailChildren = o.ShowChildrenCount
                ? Line("Couple_Children", childrenCount.ToString(CultureInfo.CurrentCulture))
                : null,
        };
    }

    /// <summary>«У шлюбі з: 01.01.2005» — або null, коли дати немає (рядок ховається).</summary>
    private string? FormatMarriageDate(SpouseLink link) =>
        Line("Couple_MarriedSince", PersonCardBuilder.FormatDate(link.MarriageDate));

    /// <summary>
    /// «Тривалість: 19 р.» — від дати шлюбу до дати розлучення, а для чинного шлюбу
    /// до сьогодні. Null, коли дата шлюбу невідома або нерезолвна (фраза, «до 1900»):
    /// рахувати тривалість від невідомого початку — це вигадувати число.
    /// </summary>
    private string? FormatDuration(SpouseLink link)
    {
        if (link.MarriageDate?.ToComparable() is not { } from)
        {
            return null;
        }

        var to = link.DivorceDate?.ToComparable() ?? DateOnly.FromDateTime(DateTime.Today);
        if (to < from)
        {
            return null;
        }

        var years = to.Year - from.Year;
        if (to < from.AddYears(years))
        {
            years--;
        }

        return Line("Couple_Duration", string.Format(
            CultureInfo.CurrentCulture, _localization.GetString("Couple_DurationYears"), years));
    }

    /// <summary>
    /// Спрощена картка для РОЗЛУЧЕНОЇ пари: без фото й без років життя — лише хто з ким,
    /// скільки були в шлюбі та скільки спільних дітей. Висить на пунктирній лінії між
    /// колишнім подружжям, де рамки немає, тож підпис «Колишнє подружжя» бере на себе те,
    /// що для чинного шлюбу каже сама рамка.
    /// </summary>
    public CoupleCard BuildFormer(Person a, Person b, SpouseLink link, int childrenCount)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(link);

        var o = _settings.Current.Cards.CoupleTooltip;
        var primary = o.SafePrimaryFontSize;
        var secondary = o.SafeSecondaryFontSize;
        var columnMaxWidth = Math.Round(primary * 10);

        return new CoupleCard
        {
            LinkId = link.Id,
            NameA = a.FullName,
            NameB = b.FullName,
            StatusText = _localization.GetString("Couple_Former"),
            ShowPhotos = false,

            PrimaryFontSize = primary,
            SecondaryFontSize = secondary,
            ColumnMaxWidth = columnMaxWidth,

            // Фото тут немає, тож ширина визначається самими іменами: дві колонки,
            // роздільник із полями (12+12) і Padding 14×2.
            CardMaxWidth = Math.Round(28 + (2 * columnMaxWidth) + 28),

            // Період шлюбу — головне, заради чого цю підказку й відкривають:
            // «коли саме вони були разом». Прапорець той самий, що керує датою
            // в картці чинного шлюбу.
            DetailPeriod = o.ShowMarriageDate
                ? Line("Couple_Period", PersonCardBuilder.FormatMarriagePeriod(link))
                : null,
            DetailDuration = o.ShowDuration ? FormatDuration(link) : null,
            DetailChildren = o.ShowChildrenCount
                ? Line("Couple_Children", childrenCount.ToString(CultureInfo.CurrentCulture))
                : null,
        };
    }

    private string? Line(string labelKey, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{_localization.GetString(labelKey)}: {value}";

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
