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
    /// Ширина декодування фото: картка показує 64×78, запас — на 200% DPI.
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

        return new CoupleCard
        {
            LinkId = link.Id,
            NameA = a.FullName,
            NameB = b.FullName,
            YearsA = o.ShowYears ? NullIfEmpty(PersonCardBuilder.FormatYears(a)) : null,
            YearsB = o.ShowYears ? NullIfEmpty(PersonCardBuilder.FormatYears(b)) : null,
            ShowPhotos = o.ShowPhotos,
            PhotoA = o.ShowPhotos ? PersonPhoto.Load(a, CoupleCard.CouplePhotoWidth) : null,
            PhotoB = o.ShowPhotos ? PersonPhoto.Load(b, CoupleCard.CouplePhotoWidth) : null,
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

    private string? Line(string labelKey, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{_localization.GetString(labelKey)}: {value}";

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
