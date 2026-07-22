namespace FamilyTree.App.Settings;

/// <summary>
/// Користувацькі налаштування застосунку (зберігаються в settings.json у AppData).
/// На етапі T-0.2 — мова й тема; згодом (T-5.4) сюди додадуться останні файли тощо.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Код мови інтерфейсу (uk, en, ...). За замовчуванням — українська.</summary>
    public string Language { get; set; } = "uk";

    /// <summary>Код теми оформлення (light, dark). За замовчуванням — світла.</summary>
    public string Theme { get; set; } = "light";

    /// <summary>Стиль назв родства (standard, detailed). За замовчуванням — стандартний.</summary>
    public string KinshipNamingStyle { get; set; } = "standard";
}
