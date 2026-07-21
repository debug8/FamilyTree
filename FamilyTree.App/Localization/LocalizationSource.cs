using System.ComponentModel;
using System.Windows.Data;

namespace FamilyTree.App.Localization;

/// <summary>
/// XAML-проксі для живого оновлення локалізованих рядків.
/// Прив'язки з <see cref="LocalizeExtension"/> звертаються до індексатора [key];
/// при зміні мови піднімається PropertyChanged для індексатора — і всі
/// прив'язані написи перечитуються без перезапуску вікна.
/// </summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    private static LocalizationSource? _instance;

    private readonly ILocalizationService _localization;

    private LocalizationSource(ILocalizationService localization)
    {
        _localization = localization;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Єдиний екземпляр. Ініціалізується при старті через <see cref="Initialize"/>.</summary>
    public static LocalizationSource Instance =>
        _instance ?? throw new InvalidOperationException(
            $"{nameof(LocalizationSource)} не ініціалізовано. Виклич {nameof(Initialize)} під час старту застосунку.");

    /// <summary>Індексатор для XAML-прив'язки: {Binding [Key], Source={x:Static ...Instance}}.</summary>
    public string this[string key] => _localization.GetString(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Створює єдиний екземпляр на основі сервісу локалізації.</summary>
    public static void Initialize(ILocalizationService localization)
    {
        _instance = new LocalizationSource(localization);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Binding.IndexerName == "Item[]" — сигнал оновити всі індексаторні прив'язки.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }
}
