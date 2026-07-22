using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.App.Localization;
using FamilyTree.App.Settings;
using FamilyTree.App.Theming;
using FamilyTree.Domain.Kinship;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Головна ViewModel. На етапі T-0.2 відповідає за вибір мови, теми та стилю назв
/// родства, і показ дати за поточною культурою. Наповнюється в Етапі 2
/// (список осіб, пошук, документне меню).
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizationService _localization;
    private readonly IThemeService _theme;
    private readonly IKinshipFormatter _kinshipFormatter;
    private readonly ISettingsService _settings;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private KinshipNamingStyleOption _selectedNamingStyle;

    public MainViewModel(
        ILocalizationService localization,
        IThemeService theme,
        IKinshipFormatter kinshipFormatter,
        ISettingsService settings)
    {
        _localization = localization;
        _theme = theme;
        _kinshipFormatter = kinshipFormatter;
        _settings = settings;

        _selectedLanguage = _localization.CurrentLanguage;
        _selectedTheme = _theme.CurrentTheme;
        _selectedNamingStyle = NamingStyles.First(s => s.Style == _kinshipFormatter.Style);

        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Список доступних мов для перемикача в UI.</summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;

    /// <summary>Список тем. Новий список щоразу — щоб перечитались локалізовані назви при зміні мови.</summary>
    public IReadOnlyList<ThemeOption> AvailableThemes => _theme.AvailableThemes.ToList();

    /// <summary>Список стилів назв родства (локалізовані назви оновлюються при зміні мови).</summary>
    public IReadOnlyList<KinshipNamingStyleOption> AvailableNamingStyles => NamingStyles.ToList();

    /// <summary>Сьогоднішня дата, відформатована за поточною культурою.</summary>
    public string TodayFormatted => DateTime.Today.ToString("D", _localization.CurrentCulture);

    private static IReadOnlyList<KinshipNamingStyleOption> NamingStyles { get; } = new[]
    {
        new KinshipNamingStyleOption(KinshipNamingStyle.Standard, "Naming_Standard"),
        new KinshipNamingStyleOption(KinshipNamingStyle.Detailed, "Naming_Detailed"),
    };

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

    partial void OnSelectedNamingStyleChanged(KinshipNamingStyleOption value)
    {
        if (value is null)
        {
            return;
        }

        _kinshipFormatter.Style = value.Style;
        _settings.Current.KinshipNamingStyle = value.Style == KinshipNamingStyle.Detailed ? "detailed" : "standard";
        _settings.Save();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Дата — від культури; назви тем і стилів — від мови. Оновити все.
        OnPropertyChanged(nameof(TodayFormatted));
        OnPropertyChanged(nameof(AvailableThemes));
        OnPropertyChanged(nameof(AvailableNamingStyles));
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
