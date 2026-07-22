using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyTree.App.Localization;
using FamilyTree.App.Services;
using FamilyTree.App.Settings;
using FamilyTree.App.Theming;
using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Головна ViewModel: список осіб із пошуком (T-2.1), CRUD осіб (T-2.2, T-2.3),
/// перемикачі мови/теми/стилю назв.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int SearchDebounceMs = 300;

    private readonly ILocalizationService _localization;
    private readonly IThemeService _theme;
    private readonly IKinshipFormatter _kinshipFormatter;
    private readonly IDocumentSession _session;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;

    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private KinshipNamingStyleOption _selectedNamingStyle;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditPersonCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeletePersonCommand))]
    private Person? _selectedPerson;

    public MainViewModel(
        ILocalizationService localization,
        IThemeService theme,
        IKinshipFormatter kinshipFormatter,
        IDocumentSession session,
        IDialogService dialogs,
        ISettingsService settings)
    {
        _localization = localization;
        _theme = theme;
        _kinshipFormatter = kinshipFormatter;
        _session = session;
        _dialogs = dialogs;
        _settings = settings;

        _selectedLanguage = _localization.CurrentLanguage;
        _selectedTheme = _theme.CurrentTheme;
        _selectedNamingStyle = NamingStyles.First(s => s.Style == _kinshipFormatter.Style);

        _localization.LanguageChanged += OnLanguageChanged;
        _session.DocumentChanged += OnDocumentChanged;
        _session.ContentChanged += OnContentChanged;

        RefreshPersons();
    }

    /// <summary>Відфільтрований і відсортований список осіб для лівої панелі.</summary>
    public ObservableCollection<Person> Persons { get; } = new();

    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;

    public IReadOnlyList<ThemeOption> AvailableThemes => _theme.AvailableThemes.ToList();

    public IReadOnlyList<KinshipNamingStyleOption> AvailableNamingStyles => NamingStyles.ToList();

    public string TodayFormatted => DateTime.Today.ToString("D", _localization.CurrentCulture);

    /// <summary>Заголовок документа з індикатором незбережених змін.</summary>
    public string DocumentStatus
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(_session.Current.Meta.Title)
                ? _localization.GetString("Doc_Untitled")
                : _session.Current.Meta.Title;
            return _session.Current.IsDirty ? $"{title} *" : title;
        }
    }

    /// <summary>Лічильник осіб, локалізований («Осіб: N»).</summary>
    public string PersonsCountText =>
        string.Format(_localization.GetString("StatusBar_PersonsCount"), _session.Current.Persons.Count);

    private static IReadOnlyList<KinshipNamingStyleOption> NamingStyles { get; } = new[]
    {
        new KinshipNamingStyleOption(KinshipNamingStyle.Standard, "Naming_Standard"),
        new KinshipNamingStyleOption(KinshipNamingStyle.Detailed, "Naming_Detailed"),
    };

    private bool HasSelection => SelectedPerson is not null;

    [RelayCommand]
    private void NewDocument()
    {
        _session.NewDocument(string.Empty);
    }

    [RelayCommand]
    private void AddPerson()
    {
        var editor = new PersonEditorViewModel();
        if (_dialogs.ShowPersonEditor(editor) && editor.Result is { } created)
        {
            _session.Current.Persons.Add(created);
            _session.MarkContentChanged();
            SelectById(created.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void EditPerson()
    {
        if (SelectedPerson is not { } person)
        {
            return;
        }

        var editor = new PersonEditorViewModel(person);
        if (_dialogs.ShowPersonEditor(editor))
        {
            _session.MarkContentChanged();
            SelectById(person.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeletePerson()
    {
        if (SelectedPerson is not { } person)
        {
            return;
        }

        var affectedLinks =
            _session.Current.ParentChildLinks.Count(l => l.Involves(person.Id)) +
            _session.Current.SpouseLinks.Count(l => l.Involves(person.Id));

        var message = string.Format(
            _localization.GetString("Person_Delete_Confirm"), person.FullName, affectedLinks);
        if (!_dialogs.Confirm(message, _localization.GetString("Person_Delete_Title")))
        {
            return;
        }

        _session.Current.ParentChildLinks.RemoveAll(l => l.Involves(person.Id));
        _session.Current.SpouseLinks.RemoveAll(l => l.Involves(person.Id));
        _session.Current.Persons.RemoveAll(p => p.Id == person.Id);
        _session.MarkContentChanged();
    }

    partial void OnSearchTextChanged(string? value) => DebounceSearch();

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

    private async void DebounceSearch()
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        try
        {
            await Task.Delay(SearchDebounceMs, cts.Token).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!cts.IsCancellationRequested)
        {
            RefreshPersons();
        }
    }

    private void RefreshPersons()
    {
        var selectedId = SelectedPerson?.Id;
        var query = _session.Current.Persons.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(p =>
                p.LastName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                p.FirstName.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        }

        var ordered = query
            .OrderBy(p => p.LastName, StringComparer.CurrentCulture)
            .ThenBy(p => p.FirstName, StringComparer.CurrentCulture)
            .ToList();

        Persons.Clear();
        foreach (var person in ordered)
        {
            Persons.Add(person);
        }

        if (selectedId is { } id)
        {
            SelectedPerson = Persons.FirstOrDefault(p => p.Id == id);
        }

        OnPropertyChanged(nameof(PersonsCountText));
        OnPropertyChanged(nameof(DocumentStatus));
    }

    private void SelectById(Guid id)
    {
        RefreshPersons();
        SelectedPerson = Persons.FirstOrDefault(p => p.Id == id);
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        SearchText = null;
        RefreshPersons();
    }

    private void OnContentChanged(object? sender, EventArgs e) => RefreshPersons();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(TodayFormatted));
        OnPropertyChanged(nameof(AvailableThemes));
        OnPropertyChanged(nameof(AvailableNamingStyles));
        OnPropertyChanged(nameof(PersonsCountText));
        OnPropertyChanged(nameof(DocumentStatus));
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _session.DocumentChanged -= OnDocumentChanged;
        _session.ContentChanged -= OnContentChanged;
    }
}
