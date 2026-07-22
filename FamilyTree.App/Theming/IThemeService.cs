namespace FamilyTree.App.Theming;

/// <summary>
/// Опис однієї теми оформлення.
/// </summary>
/// <param name="Code">Ідентифікатор теми ("light", "dark"). Зберігається в налаштуваннях.</param>
/// <param name="NameKey">Ключ ресурсу з локалізованою назвою (напр. Theme_Light).</param>
public sealed record ThemeOption(string Code, string NameKey);

/// <summary>
/// Сервіс тем оформлення: живе перемикання світлої/темної теми
/// підміною тематичного ResourceDictionary у ресурсах застосунку.
/// </summary>
public interface IThemeService
{
    /// <summary>Поточна активна тема.</summary>
    ThemeOption CurrentTheme { get; }

    /// <summary>Список доступних тем.</summary>
    IReadOnlyList<ThemeOption> AvailableThemes { get; }

    /// <summary>
    /// Застосовує тему за кодом. Невідомий код — тихий відкат на світлу.
    /// Піднімає <see cref="ThemeChanged"/>.
    /// </summary>
    void SetTheme(string code);

    /// <summary>Спрацьовує після зміни теми.</summary>
    event EventHandler? ThemeChanged;
}
