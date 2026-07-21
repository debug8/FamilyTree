using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.App.Localization;
using FamilyTree.App.Settings;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Головна ViewModel. На етапі T-0.2 відповідає за вибір мови інтерфейсу
/// та показ дати за поточною культурою. Наповнюється в Етапі 2
/// (список осіб, пошук, документне меню).
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settings;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    public MainViewModel(ILocalizationService localization, ISettingsService settings)
    {
        _localization = localization;
        _settings = settings;

        _selectedLanguage = _localization.CurrentLanguage;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Список доступних мов для перемикача в UI.</summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;

    /// <summary>Сьогоднішня дата, відформатована за поточною культурою.</summary>
    public string TodayFormatted => DateTime.Today.ToString("D", _localization.CurrentCulture);

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value is null)
        {
            return;
        }

        _localization.SetLanguage(value.Code);
        _settings.Current.Language = value.Code;
        _settings.Save();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Дата залежить від культури — оновити її після зміни мови.
        OnPropertyChanged(nameof(TodayFormatted));
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
