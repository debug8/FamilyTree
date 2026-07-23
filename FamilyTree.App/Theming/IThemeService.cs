using FamilyTree.App.Localization;

namespace FamilyTree.App.Theming;

/// <summary>Опис однієї теми оформлення (Code — "light"/"dark", NameKey — ключ назви).</summary>
public sealed class ThemeOption : LocalizedOption
{
    public ThemeOption(string code, string nameKey)
        : base(nameKey) => Code = code;

    public string Code { get; }
}

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
