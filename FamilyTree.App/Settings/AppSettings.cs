namespace FamilyTree.App.Settings;

/// <summary>
/// Користувацькі налаштування застосунку (зберігаються в settings.json у AppData).
/// На етапі T-0.2 — мова й тема; згодом (T-5.4) сюди додадуться останні файли тощо.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Код мови інтерфейсу (uk, en, ...). За замовчуванням — українська.</summary>
    public string Language { get; set; } = "uk";

    /// <summary>Код теми оформлення (brand, light, dark). За замовчуванням — фірмова.</summary>
    public string Theme { get; set; } = "brand";

    /// <summary>Стиль назв родства (standard, detailed). За замовчуванням — стандартний.</summary>
    public string KinshipNamingStyle { get; set; } = "standard";

    /// <summary>Глибина дерева за замовчуванням (кількість поколінь; 0 — усі). За замовчуванням — 3.</summary>
    public int DefaultTreeDepth { get; set; } = 3;

    /// <summary>Останні відкриті файли (найновіші — першими).</summary>
    public List<string> RecentFiles { get; set; } = new();
}
