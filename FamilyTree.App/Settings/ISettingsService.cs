namespace FamilyTree.App.Settings;

/// <summary>
/// Читання та збереження користувацьких налаштувань застосунку.
/// </summary>
public interface ISettingsService
{
    /// <summary>Поточні налаштування (завантажені при старті).</summary>
    AppSettings Current { get; }

    /// <summary>Завантажує налаштування з диска; за відсутності файлу — значення за замовчуванням.</summary>
    AppSettings Load();

    /// <summary>Атомарно зберігає поточні налаштування на диск.</summary>
    void Save();
}
