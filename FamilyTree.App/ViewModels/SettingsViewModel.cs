using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyTree.App.Localization;
using FamilyTree.App.Settings;
using FamilyTree.App.Theming;
using FamilyTree.Domain.Kinship;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Повноцінний екран налаштувань (T-5.4): мова, тема, стиль назв родства,
/// глибина дерева за замовчуванням та керування списком останніх файлів.
/// Зміни застосовуються вживу (через ті самі сервіси, що й тулбар) і одразу
/// зберігаються в settings.json.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private static readonly IReadOnlyList<KinshipNamingStyleOption> NamingStyles = new[]
    {
        new KinshipNamingStyleOption(KinshipNamingStyle.Standard, "Naming_Standard"),
        new KinshipNamingStyleOption(KinshipNamingStyle.Detailed, "Naming_Detailed"),
    };

    private readonly ILocalizationService _localization;
    private readonly IThemeService _theme;
    private readonly IKinshipFormatter _kinshipFormatter;
    private readonly ISettingsService _settings;
    private readonly TreeViewModel _tree;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private KinshipNamingStyleOption _selectedNamingStyle;

    [ObservableProperty]
    private int _defaultDepth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentFiles))]
    [NotifyCanExecuteChangedFor(nameof(ClearRecentFilesCommand))]
    private int _recentFilesCount;

    public SettingsViewModel(
        ILocalizationService localization,
        IThemeService theme,
        IKinshipFormatter kinshipFormatter,
        ISettingsService settings,
        TreeViewModel tree)
    {
        _localization = localization;
        _theme = theme;
        _kinshipFormatter = kinshipFormatter;
        _settings = settings;
        _tree = tree;

        _selectedLanguage = _localization.CurrentLanguage;
        _selectedTheme = _theme.CurrentTheme;
        _selectedNamingStyle = NamingStyles.First(s => s.Style == _kinshipFormatter.Style);
        _defaultDepth = _settings.Current.DefaultTreeDepth;
        _recentFilesCount = _settings.Current.RecentFiles.Count;

        _localization.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;

    public IReadOnlyList<ThemeOption> AvailableThemes => _theme.AvailableThemes.ToList();

    public IReadOnlyList<KinshipNamingStyleOption> AvailableNamingStyles => NamingStyles.ToList();

    /// <summary>Варіанти глибини (0 — усі покоління) — ті самі, що на вкладці «Дерево».</summary>
    public IReadOnlyList<int> DepthOptions => _tree.DepthOptions;

    public bool HasRecentFiles => RecentFilesCount > 0;

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
        _tree.Refresh();
    }

    partial void OnDefaultDepthChanged(int value)
    {
        _settings.Current.DefaultTreeDepth = value;
        _settings.Save();
        _tree.Depth = value; // застосувати одразу до поточного дерева
    }

    [RelayCommand(CanExecute = nameof(HasRecentFiles))]
    private void ClearRecentFiles()
    {
        _settings.Current.RecentFiles.Clear();
        _settings.Save();
        RecentFilesCount = 0;
    }

    /// <summary>Викликати при закритті діалогу — відписатися від подій.</summary>
    public void Detach() => _localization.LanguageChanged -= OnLanguageChanged;

    // Мова могла змінитися прямо в цьому діалозі — оновлюємо локалізовані списки,
    // щоб їхні підписи перечиталися новою мовою.
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AvailableThemes));
        OnPropertyChanged(nameof(AvailableNamingStyles));
    }
}
