namespace FamilyTree.App.Settings;

/// <summary>
/// Користувацькі налаштування застосунку (зберігаються в settings.json у AppData).
/// На етапі T-0.2 — лише мова; згодом (T-5.4) сюди додадуться останні файли, тема тощо.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Код мови інтерфейсу (uk, en). За замовчуванням — українська.</summary>
    public string Language { get; set; } = "uk";
}
