using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.App.Localization;
using FamilyTree.App.Settings;
using FamilyTree.App.Theming;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Головна ViewModel. На етапі T-0.2 відповідає за вибір мови й теми інтерфейсу
/// та показ дати за поточною культурою. Наповнюється в Етапі 2
/// (список осіб, пошук, документне меню).
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizationService _localization;
    private readonly IThemeService _theme;
    private readonly ISettingsService _settings;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    public MainViewModel(ILocalizationService localization, IThemeService theme, ISettingsService settings)
    {
        _localization = localization;
        _theme = theme;
        _settings = settings;

        _selectedLanguage = _localization.CurrentLanguage;
        _selectedTheme = _theme.CurrentTheme;

        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Список доступних мов для перемикача в UI.</summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;

    /// <summary>Список доступних тем. Новий список щоразу — щоб перечитались локалізовані назви при зміні мови.</summary>
    public IReadOnlyList<ThemeOption> AvailableThemes => _theme.AvailableThemes.ToList();

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

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (value is null)
        {
            return;
        }

        _theme.SetTheme(value.Code);
        _settings.Current.Theme = value.Code;
        _settings.Save();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Дата залежить від культури; назви тем — від мови. Оновити обидва.
        OnPropertyChanged(nameof(TodayFormatted));
        OnPropertyChanged(nameof(AvailableThemes));
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
