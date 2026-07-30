using System.Windows.Media;
using FamilyTree.App.Localization;

namespace FamilyTree.App.Theming;

/// <summary>
/// Опис однієї теми оформлення. <c>Code</c> задає й ім'я файлу
/// (<c>Styles\Theme.{Code}.xaml</c>), <c>NameKey</c> — ключ локалізованої назви.
/// </summary>
public sealed class ThemeOption : LocalizedOption
{
    public ThemeOption(
        string code,
        string nameKey,
        bool isDark = false,
        Color? captionColor = null,
        Color? captionTextColor = null)
        : base(nameKey)
    {
        Code = code;
        IsDark = isDark;
        CaptionColor = captionColor;
        CaptionTextColor = captionTextColor;
    }

    public string Code { get; }

    /// <summary>
    /// Чи темна тема. Потрібно для заголовка вікна (DWM), який фарбується
    /// не ресурсами, а окремим системним прапорцем.
    /// </summary>
    public bool IsDark { get; }

    /// <summary>
    /// Бажаний колір системного заголовка вікна. Працює лише на Windows 11
    /// (build 22000+); на старіших системах виклик ігнорується і заголовок
    /// фарбується за <see cref="IsDark"/>. <c>null</c> — колір не задаємо.
    /// </summary>
    public Color? CaptionColor { get; }

    /// <summary>Бажаний колір тексту заголовка (ті самі обмеження, що й <see cref="CaptionColor"/>).</summary>
    public Color? CaptionTextColor { get; }
}

/// <summary>
/// Сервіс тем оформлення: живе перемикання теми підміною
/// тематичного ResourceDictionary у ресурсах застосунку.
/// </summary>
public interface IThemeService
{
    /// <summary>Поточна активна тема.</summary>
    ThemeOption CurrentTheme { get; }

    /// <summary>Список доступних тем.</summary>
    IReadOnlyList<ThemeOption> AvailableThemes { get; }

    /// <summary>
    /// Застосовує тему за кодом. Невідомий код — тихий відкат на основну.
    /// Піднімає <see cref="ThemeChanged"/>.
    /// </summary>
    void SetTheme(string code);

    /// <summary>Спрацьовує після зміни теми.</summary>
    event EventHandler? ThemeChanged;
}
