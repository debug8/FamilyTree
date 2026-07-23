using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyTree.App.Localization;
using FamilyTree.App.Services;
using FamilyTree.App.Settings;
using FamilyTree.App.Theming;
using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;
using FamilyTree.Domain.Validation;
using FamilyTree.Storage;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Головна ViewModel: файлові операції (T-2.5), список осіб із пошуком (T-2.1),
/// CRUD осіб (T-2.2, T-2.3), керування зв'язками (T-2.4), перемикачі мови/теми/стилю.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int SearchDebounceMs = 300;
    private const int MaxRecentFiles = 8;

    private readonly ILocalizationService _localization;
    private readonly IThemeService _theme;
    private readonly IKinshipFormatter _kinshipFormatter;
    private readonly IDocumentSession _session;
    private readonly IDialogService _dialogs;
    private readonly RelationshipValidator _validator;
    private readonly IFamilyStorage _storage;
    private readonly TreeViewModel _tree;
    private readonly WhoIsWhoViewModel _whoIsWho;
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
    private PersonSortOption _selectedSort = SortOptions[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionGlyph))]
    private bool _sortDescending;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditPersonCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeletePersonCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddParentCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddChildCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSpouseCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPerson))]
    [NotifyPropertyChangedFor(nameof(NoSelection))]
    private Person? _selectedPerson;

    public MainViewModel(
        ILocalizationService localization,
        IThemeService theme,
        IKinshipFormatter kinshipFormatter,
        IDocumentSession session,
        IDialogService dialogs,
        RelationshipValidator validator,
        IFamilyStorage storage,
        TreeViewModel tree,
        WhoIsWhoViewModel whoIsWho,
        ISettingsService settings)
    {
        _localization = localization;
        _theme = theme;
        _kinshipFormatter = kinshipFormatter;
        _session = session;
        _dialogs = dialogs;
        _validator = validator;
        _storage = storage;
        _tree = tree;
        _whoIsWho = whoIsWho;
        _settings = settings;

        _selectedLanguage = _localization.CurrentLanguage;
        _selectedTheme = _theme.CurrentTheme;
        _selectedNamingStyle = NamingStyles.First(s => s.Style == _kinshipFormatter.Style);

        LoadRecentFiles();

        _localization.LanguageChanged += OnLanguageChanged;
        _session.DocumentChanged += OnDocumentChanged;
        _session.ContentChanged += OnContentChanged;

        RefreshPersons();
    }

    public ObservableCollection<Person> Persons { get; } = new();

    public ObservableCollection<Person> Parents { get; } = new();

    public ObservableCollection<Person> Children { get; } = new();

    public ObservableCollection<Person> Spouses { get; } = new();

    public ObservableCollection<string> RecentFiles { get; } = new();

    /// <summary>ViewModel вкладки «Дерево».</summary>
    public TreeViewModel Tree => _tree;

    /// <summary>ViewModel вкладки «Хто кому».</summary>
    public WhoIsWhoViewModel WhoIsWho => _whoIsWho;

    public bool HasSelectedPerson => SelectedPerson is not null;

    public bool NoSelection => SelectedPerson is null;

    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;

    public IReadOnlyList<ThemeOption> AvailableThemes => _theme.AvailableThemes.ToList();

    public IReadOnlyList<KinshipNamingStyleOption> AvailableNamingStyles => NamingStyles.ToList();

    /// <summary>Варіанти сортування списку осіб (локалізовані назви оновлюються при зміні мови).</summary>
    public IReadOnlyList<PersonSortOption> AvailableSortOptions => SortOptions.ToList();

    /// <summary>Стрілка напрямку сортування: ▲ за зростанням, ▼ за спаданням.</summary>
    public string SortDirectionGlyph => SortDescending ? "▼" : "▲";

    public string TodayFormatted => DateTime.Today.ToString("D", _localization.CurrentCulture);

    /// <summary>Заголовок вікна: назва застосунку — документ [*].</summary>
    public string Title =>
        $"{_localization.GetString("MainWindow_Title")} — {DocumentName}{(_session.Current.IsDirty ? " *" : string.Empty)}";

    public string DocumentStatus => _session.Current.IsDirty ? $"{DocumentName} *" : DocumentName;

    public string PersonsCountText =>
        string.Format(_localization.GetString("StatusBar_PersonsCount"), _session.Current.Persons.Count);

    public bool HasUnsavedChanges => _session.Current.IsDirty;

    private string DocumentName
    {
        get
        {
            if (!string.IsNullOrEmpty(_session.FilePath))
            {
                return Path.GetFileNameWithoutExtension(_session.FilePath);
            }

            return string.IsNullOrWhiteSpace(_session.Current.Meta.Title)
                ? _localization.GetString("Doc_Untitled")
                : _session.Current.Meta.Title;
        }
    }

    private string FileFilter => _localization.GetString("File_Filter");

    private static IReadOnlyList<KinshipNamingStyleOption> NamingStyles { get; } = new[]
    {
        new KinshipNamingStyleOption(KinshipNamingStyle.Standard, "Naming_Standard"),
        new KinshipNamingStyleOption(KinshipNamingStyle.Detailed, "Naming_Detailed"),
    };

    private static IReadOnlyList<PersonSortOption> SortOptions { get; } = new[]
    {
        new PersonSortOption(PersonSortField.LastName, "Sort_LastName"),
        new PersonSortOption(PersonSortField.FirstName, "Sort_FirstName"),
        new PersonSortOption(PersonSortField.BirthDate, "Sort_BirthDate"),
    };

    private bool HasSelection => SelectedPerson is not null;

    // ---- Файлові команди (T-2.5) ----------------------------------------

    [RelayCommand]
    private async Task New()
    {
        if (await PromptSaveIfDirtyAsync())
        {
            _session.NewDocument(string.Empty);
        }
    }

    [RelayCommand]
    private async Task Open()
    {
        if (!await PromptSaveIfDirtyAsync())
        {
            return;
        }

        if (_dialogs.AskOpenPath(FileFilter) is { } path)
        {
            await OpenPathAsync(path);
        }
    }

    [RelayCommand]
    private async Task Save() => await SaveInternalAsync();

    [RelayCommand]
    private async Task SaveAs() => await SaveAsInternalAsync();

    [RelayCommand]
    private async Task OpenRecent(string? path)
    {
        if (string.IsNullOrEmpty(path) || !await PromptSaveIfDirtyAsync())
        {
            return;
        }

        await OpenPathAsync(path);
    }

    [RelayCommand]
    private void Exit() => Application.Current.MainWindow?.Close();

    /// <summary>Запит про незбережені зміни. true — можна продовжити (збережено або відкинуто).</summary>
    public async Task<bool> PromptSaveIfDirtyAsync()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return _dialogs.ConfirmSaveChanges(
            _localization.GetString("SaveChanges_Message"),
            _localization.GetString("SaveChanges_Title")) switch
        {
            SaveChangesResult.Save => await SaveInternalAsync(),
            SaveChangesResult.Discard => true,
            _ => false,
        };
    }

    private async Task<bool> SaveInternalAsync() =>
        string.IsNullOrEmpty(_session.FilePath)
            ? await SaveAsInternalAsync()
            : await WriteAsync(_session.FilePath);

    private async Task<bool> SaveAsInternalAsync()
    {
        var suggested = DocumentName + ".familytree";
        if (_dialogs.AskSavePath(FileFilter, suggested) is not { } path)
        {
            return false;
        }

        _session.FilePath = path;
        return await WriteAsync(path);
    }

    private async Task<bool> WriteAsync(string path)
    {
        try
        {
            await _storage.SaveAsync(_session.Current, path);
            AddRecent(path);
            RaiseDocumentInfo();
            return true;
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(ex.Message, _localization.GetString("File_ErrorTitle"));
            return false;
        }
    }

    private async Task OpenPathAsync(string path)
    {
        try
        {
            var document = await _storage.LoadAsync(path);
            _session.SetDocument(document, path);
            AddRecent(path);
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(ex.Message, _localization.GetString("File_ErrorTitle"));
            RemoveRecent(path);
        }
    }

    // ---- CRUD осіб (T-2.1..T-2.3) ---------------------------------------

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

    // ---- Зв'язки (T-2.4) ------------------------------------------------

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddParent()
    {
        if (SelectedPerson is not { } child || PickPerson(RelationshipRole.Parent, child) is not { } parent)
        {
            return;
        }

        var link = new ParentChildLink { ParentId = parent.Id, ChildId = child.Id };
        var result = _validator.ValidateParentChild(link, _session.Current.Persons, _session.Current.ParentChildLinks);
        if (Accept(result))
        {
            _session.Current.ParentChildLinks.Add(link);
            _session.MarkContentChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddChild()
    {
        if (SelectedPerson is not { } parent || PickPerson(RelationshipRole.Child, parent) is not { } child)
        {
            return;
        }

        var link = new ParentChildLink { ParentId = parent.Id, ChildId = child.Id };
        var result = _validator.ValidateParentChild(link, _session.Current.Persons, _session.Current.ParentChildLinks);
        if (Accept(result))
        {
            _session.Current.ParentChildLinks.Add(link);
            _session.MarkContentChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddSpouse()
    {
        if (SelectedPerson is not { } person)
        {
            return;
        }

        var editor = new RelationshipEditorViewModel(RelationshipRole.Spouse, person, _session.Current.Persons);
        if (!_dialogs.ShowRelationshipEditor(editor) || editor.SelectedCandidate is not { } other)
        {
            return;
        }

        var link = SpouseLink.Create(person.Id, other.Id, editor.MarriageDateOnly, editor.DivorceDateOnly);
        var result = _validator.ValidateSpouse(link, _session.Current.SpouseLinks);
        if (Accept(result))
        {
            _session.Current.SpouseLinks.Add(link);
            _session.MarkContentChanged();
        }
    }

    [RelayCommand]
    private void RemoveParent(Person? parent)
    {
        if (SelectedPerson is { } child && parent is not null
            && ConfirmRemoveRelation("Relation_RemoveParent_Confirm", parent.FullName, child.FullName))
        {
            _session.Current.ParentChildLinks.RemoveAll(l => l.ParentId == parent.Id && l.ChildId == child.Id);
            _session.MarkContentChanged();
        }
    }

    [RelayCommand]
    private void RemoveChild(Person? child)
    {
        if (SelectedPerson is { } parent && child is not null
            && ConfirmRemoveRelation("Relation_RemoveChild_Confirm", child.FullName, parent.FullName))
        {
            _session.Current.ParentChildLinks.RemoveAll(l => l.ParentId == parent.Id && l.ChildId == child.Id);
            _session.MarkContentChanged();
        }
    }

    [RelayCommand]
    private void EditSpouse(Person? spouse)
    {
        if (SelectedPerson is not { } person || spouse is null)
        {
            return;
        }

        var link = _session.Current.SpouseLinks.FirstOrDefault(l => l.Involves(person.Id) && l.Involves(spouse.Id));
        if (link is null)
        {
            return;
        }

        var editor = RelationshipEditorViewModel.ForSpouseEdit(person, spouse, link.MarriageDate, link.DivorceDate);
        if (_dialogs.ShowRelationshipEditor(editor))
        {
            link.MarriageDate = editor.MarriageDateOnly;
            link.DivorceDate = editor.DivorceDateOnly;
            _session.MarkContentChanged();
        }
    }

    [RelayCommand]
    private void RemoveSpouse(Person? spouse)
    {
        if (SelectedPerson is { } person && spouse is not null
            && ConfirmRemoveRelation("Relation_RemoveSpouse_Confirm", spouse.FullName, person.FullName))
        {
            _session.Current.SpouseLinks.RemoveAll(l => l.Involves(person.Id) && l.Involves(spouse.Id));
            _session.MarkContentChanged();
        }
    }

    private bool ConfirmRemoveRelation(string messageKey, string relativeName, string personName)
    {
        var message = string.Format(_localization.GetString(messageKey), relativeName, personName);
        return _dialogs.Confirm(message, _localization.GetString("Relation_Remove_Title"));
    }

    private Person? PickPerson(RelationshipRole role, Person basePerson)
    {
        var editor = new RelationshipEditorViewModel(role, basePerson, _session.Current.Persons);
        return _dialogs.ShowRelationshipEditor(editor) ? editor.SelectedCandidate : null;
    }

    private bool Accept(ValidationResult result)
    {
        if (!result.IsValid)
        {
            _dialogs.ShowMessage(Describe(result.Errors), _localization.GetString("Validation_Title"));
            return false;
        }

        if (result.HasWarnings)
        {
            return _dialogs.Confirm(
                Describe(result.Warnings) + Environment.NewLine + _localization.GetString("Validation_Continue"),
                _localization.GetString("Validation_Title"));
        }

        return true;
    }

    private string Describe(IReadOnlyList<ValidationMessage> messages) =>
        string.Join(Environment.NewLine, messages.Select(m =>
            string.Format(_localization.GetString(m.Key), m.Arguments.ToArray())));

    // ---- Перемикачі (мова/тема/стиль) -----------------------------------

    partial void OnSelectedPersonChanged(Person? value)
    {
        RefreshRelations();
        _tree.SetRoot(value?.Id);
    }

    partial void OnSearchTextChanged(string? value) => DebounceSearch();

    partial void OnSelectedSortChanged(PersonSortOption value) => RefreshPersons();

    partial void OnSortDescendingChanged(bool value) => RefreshPersons();

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

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
        _tree.Refresh(); // оновити бейджі родства на дереві
    }

    // ---- Внутрішнє -------------------------------------------------------

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

        var descending = SortDescending;
        var ordered = (SelectedSort?.Field switch
        {
            PersonSortField.FirstName => Direction(query, p => p.FirstName, StringComparer.CurrentCulture, descending)
                .ThenBy(p => p.LastName, StringComparer.CurrentCulture),
            PersonSortField.BirthDate => Direction(
                    query,
                    p => p.BirthDate ?? (descending ? DateOnly.MinValue : DateOnly.MaxValue),
                    Comparer<DateOnly>.Default,
                    descending)
                .ThenBy(p => p.LastName, StringComparer.CurrentCulture),
            _ => Direction(query, p => p.LastName, StringComparer.CurrentCulture, descending)
                .ThenBy(p => p.FirstName, StringComparer.CurrentCulture),
        }).ToList();

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
        RaiseDocumentInfo();
    }

    private static IOrderedEnumerable<Person> Direction<TKey>(
        IEnumerable<Person> source, Func<Person, TKey> key, IComparer<TKey> comparer, bool descending) =>
        descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);

    private void SelectById(Guid id)
    {
        RefreshPersons();
        SelectedPerson = Persons.FirstOrDefault(p => p.Id == id);
    }

    private void RefreshRelations()
    {
        Parents.Clear();
        Children.Clear();
        Spouses.Clear();

        if (SelectedPerson is not { } person)
        {
            return;
        }

        var doc = _session.Current;
        var byId = doc.Persons.ToDictionary(p => p.Id);

        foreach (var link in doc.ParentChildLinks.Where(l => l.ChildId == person.Id))
        {
            if (byId.TryGetValue(link.ParentId, out var parent))
            {
                Parents.Add(parent);
            }
        }

        foreach (var link in doc.ParentChildLinks.Where(l => l.ParentId == person.Id))
        {
            if (byId.TryGetValue(link.ChildId, out var child))
            {
                Children.Add(child);
            }
        }

        foreach (var link in doc.SpouseLinks.Where(l => l.Involves(person.Id)))
        {
            if (link.SpouseOf(person.Id) is { } spouseId && byId.TryGetValue(spouseId, out var spouse))
            {
                Spouses.Add(spouse);
            }
        }
    }

    private void LoadRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var path in _settings.Current.RecentFiles.Where(File.Exists))
        {
            RecentFiles.Add(path);
        }
    }

    private void AddRecent(string path)
    {
        var full = Path.GetFullPath(path);
        _settings.Current.RecentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _settings.Current.RecentFiles.Insert(0, full);
        if (_settings.Current.RecentFiles.Count > MaxRecentFiles)
        {
            _settings.Current.RecentFiles.RemoveRange(MaxRecentFiles, _settings.Current.RecentFiles.Count - MaxRecentFiles);
        }

        _settings.Save();
        LoadRecentFiles();
    }

    private void RemoveRecent(string path)
    {
        _settings.Current.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        LoadRecentFiles();
    }

    private void RaiseDocumentInfo()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DocumentStatus));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        SearchText = null;
        RefreshPersons();
    }

    private void OnContentChanged(object? sender, EventArgs e)
    {
        RefreshPersons();
        RefreshRelations();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(TodayFormatted));
        OnPropertyChanged(nameof(AvailableThemes));
        OnPropertyChanged(nameof(AvailableNamingStyles));
        OnPropertyChanged(nameof(AvailableSortOptions));
        OnPropertyChanged(nameof(PersonsCountText));
        RaiseDocumentInfo();
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _session.DocumentChanged -= OnDocumentChanged;
        _session.ContentChanged -= OnContentChanged;
    }
}
