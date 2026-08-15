using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Markup;

namespace FamilyTree.App.Localization;

/// <summary>
/// Тримає WPF-властивість <see cref="FrameworkElement.Language"/> (xml:lang) синхронізованою
/// з мовою інтерфейсу.
/// <para>
/// Навіщо (B-01): <c>DatePicker</c>/<c>Calendar</c> форматують і — головне — ПАРСЯТЬ дати саме
/// за <c>Language</c>, а не за <c>Thread.CurrentCulture</c>. Метадані цієї властивості за
/// замовчуванням = <c>en-US</c>, тож при українській мові дата, введена як «03.04.1980»
/// (3 квітня), парсилась як en-US → 4 березня, і в документ мовчки йшла інша дата.
/// </para>
/// <para>
/// <c>OverrideMetadata</c> можна викликати лише раз на властивість/тип, тому для живого
/// перемикання мови (та для вікон, відкритих ПІСЛЯ перемикання) значення проставляється
/// на вікна напряму: <see cref="Apply"/> — на всі відкриті, <see cref="ApplyTo"/> — на нове.
/// </para>
/// </summary>
internal static class UiLanguage
{
    private static bool _metadataOverridden;

    /// <summary>Поточна мова у форматі WPF; нею позначаємо кожне нове вікно.</summary>
    public static XmlLanguage Current { get; private set; } =
        ToXmlLanguage(CultureInfo.CurrentCulture);

    /// <summary>
    /// Одноразово перевизначає стандартну мову всіх <see cref="FrameworkElement"/>.
    /// Викликати ДО створення будь-якого вікна (метадані перевизначаються лише раз).
    /// </summary>
    public static void Initialize(CultureInfo culture)
    {
        Current = ToXmlLanguage(culture);
        if (_metadataOverridden)
        {
            return;
        }

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(Current));
        _metadataOverridden = true;
    }

    /// <summary>
    /// Живе перемикання: оновлює поточну мову й проставляє її на всі вже відкриті вікна
    /// (звідки вона успадковується дочірніми елементами, зокрема DatePicker).
    /// </summary>
    public static void Apply(CultureInfo culture)
    {
        Current = ToXmlLanguage(culture);
        foreach (var window in Application.Current.Windows.OfType<Window>())
        {
            window.Language = Current;
        }
    }

    /// <summary>
    /// Позначає окреме вікно поточною мовою. Потрібне для вікон, створених ПІСЛЯ перемикання:
    /// нове вікно — корінь успадкування й інакше взяло б заморожені метадані (мову старту),
    /// а не поточну (Owner на успадкування властивостей не впливає).
    /// </summary>
    public static void ApplyTo(Window window) => window.Language = Current;

    /// <summary>
    /// Зводить культуру до XmlLanguage через СПЕЦИФІЧНУ культуру: мови застосунку (uk/en) —
    /// нейтральні, а DatePicker потребує специфічну (uk-UA/en-US), інакше регіон дат
    /// визначається непередбачувано.
    /// </summary>
    private static XmlLanguage ToXmlLanguage(CultureInfo culture)
    {
        var specific = culture.IsNeutralCulture
            ? CultureInfo.CreateSpecificCulture(culture.Name)
            : culture;
        return XmlLanguage.GetLanguage(specific.IetfLanguageTag);
    }
}
