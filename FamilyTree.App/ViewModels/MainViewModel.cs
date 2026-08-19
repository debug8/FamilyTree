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
using FamilyTree.Domain.Seeding;
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
    private readonly FamilyMerger _merger;
    private readonly TreeViewModel _tree;
    private readonly WhoIsWhoViewModel _whoIsWho;
    private readonly ISettingsService _settings;

    // Складання картки-тултіпа спільне з деревом (див. PersonCardBuilder).
    private readonly PersonCardBuilder _cards;

    private CancellationTokenSource? _searchCts;

    // Глушник round-trip'у виділення під час перезаповнення списку осіб.
    // Persons.Clear() змушує ListBox синхронно записати null у SelectedPerson
    // (Selector.SelectedItem прив'язаний TwoWay за замовчуванням), і без цього прапорця
    // кожна зміна списку давала SetRoot(null) → Rebuild(), а після відновлення
    // виділення — ще один Rebuild(): ДВА повні перерахунки дерева на кожну дію.
    private bool _suppressSelectionSync;

    // Особа, яку треба виділити після найближчого RefreshPersons(). Дозволяє додати
    // особу й виділити її ОДНИМ оновленням списку замість двох.
    private Guid? _pendingSelectionId;

    // Остання осмислена вибрана особа. Тримається окремо від SelectedPerson, щоб
    // фільтрація пошуком не втрачала виділення й не гасила побудоване дерево.
    private Guid? _lastSelectedId;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private KinshipNamingStyleOption _selectedNamingStyle;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private PersonSortOption _selectedSort = PersonFilterOptions.Sorts[0];

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
        FamilyMerger merger,
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
        _merger = merger;
        _tree = tree;
        _whoIsWho = whoIsWho;
        _settings = settings;
        _cards = new PersonCardBuilder(localization);

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

    // Родичі вибраної особи як картки: рядок показує FullName, а тултіп —
    // ту саму велику картку, що й вузол дерева (див. PersonCard).
    public ObservableCollection<PersonCard> Parents { get; } = new();

    public ObservableCollection<PersonCard> Children { get; } = new();

    public ObservableCollection<PersonCard> Spouses { get; } = new();

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
    public IReadOnlyList<PersonSortOption> AvailableSortOptions => PersonFilterOptions.Sorts;

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
    private async Task Import()
    {
        if (_dialogs.AskOpenPath(FileFilter) is not { } path)
        {
            return;
        }

        FamilyDocument source;
        try
        {
            source = await _storage.LoadAsync(path);
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(ex.Message, _localization.GetString("File_ErrorTitle"));
            return;
        }

        var plan = _merger.Plan(_session.Current, source);
        var report = plan.ToReport();

        var confirm = string.Format(
            _localization.GetString("Import_Confirm"),
            report.AddedPersons,
            report.DuplicatePersons,
            report.AddedParentLinks + report.AddedSpouseLinks);
        confirm += RejectedSuffix(report.RejectedLinks);
        if (!_dialogs.Confirm(confirm, _localization.GetString("Import_Title")))
        {
            return;
        }

        _merger.Apply(_session.Current, plan);
        _session.MarkContentChanged();

        var done = string.Format(
            _localization.GetString("Import_Done"), report.AddedPersons, report.DuplicatePersons);
        done += RejectedSuffix(report.RejectedLinks);
        _dialogs.ShowMessage(done, _localization.GetString("Import_Title"));
    }

    /// <summary>Локалізований рядок про відхилені при злитті зв'язки (порожній, якщо їх немає).</summary>
    private string RejectedSuffix(int rejected) =>
        rejected > 0
            ? Environment.NewLine + Environment.NewLine
                + string.Format(_localization.GetString("Import_Rejected"), rejected)
            : string.Empty;

    [RelayCommand]
    private async Task CreateDemoFamily()
    {
        // 1. Налаштування (поколінь, осіб, складність тощо).
        var config = new DemoFamilyViewModel();
        if (!_dialogs.ShowDemoFamilyEditor(config))
        {
            return;
        }

        // 2. Демо-родина заміняє поточний документ — спершу зберегти незбережене.
        if (!await PromptSaveIfDirtyAsync())
        {
            return;
        }

        // 3. Згенерувати доменні сутності та зібрати з них новий документ.
        var result = DemoFamilyGenerator.Generate(config.ToOptions());

        var document = FamilyDocument.CreateNew(_localization.GetString("Demo_DocTitle"));
        document.Persons.AddRange(result.Persons);
        document.ParentChildLinks.AddRange(result.ParentChildLinks);
        document.SpouseLinks.AddRange(result.SpouseLinks);

        _session.SetDocument(document, null);

        // 4. Кореневу особу з найбагатшим оточенням плануємо ДО сповіщення про зміни,
        // щоб список і дерево оновилися один раз, а не двічі.
        _pendingSelectionId = result.SuggestedRootId;
        _session.MarkContentChanged(); // демо-родина ще не збережена → позначити зміни

        var done = string.Format(_localization.GetString("Demo_Done"), result.Persons.Count);
        _dialogs.ShowMessage(done, _localization.GetString("Demo_Title"));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(_localization, _theme, _kinshipFormatter, _settings, _tree);
        _dialogs.ShowSettings(vm);
        vm.Detach();

        // Діалог застосовує зміни вживу; тут лише синхронізуємо тулбар і список останніх файлів.
        SelectedLanguage = _localization.CurrentLanguage;
        SelectedTheme = _theme.CurrentTheme;
        SelectedNamingStyle = NamingStyles.First(s => s.Style == _kinshipFormatter.Style);
        LoadRecentFiles();
    }

    [RelayCommand]
    private void OpenAbout() => _dialogs.ShowAbout(new AboutViewModel());

    [RelayCommand]
    private void Exit() => Application.Current.MainWindow?.Close();

    /// <summary>
    /// Відкриває файл за шляхом (напр. переданий у командному рядку через асоціацію .familytree).
    /// Спершу пропонує зберегти незбережені зміни.
    /// </summary>
    public async Task OpenFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !await PromptSaveIfDirtyAsync())
        {
            return;
        }

        await OpenPathAsync(path);
    }

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
            _dialogs.ShowMessage(DescribeFileError(ex), _localization.GetString("File_ErrorTitle"));
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
            ReportRepairs(document);
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(DescribeFileError(ex), _localization.GetString("File_ErrorTitle"));

            // Зі списку недавніх викидаємо лише те, чого справді немає:
            // при тимчасовій помилці (файл заблокований, мережа відпала) запис лишається.
            if (ex is FamilyFileException { MessageKey: FileErrorKeys.NotFound })
            {
                RemoveRecent(path);
            }
        }
    }

    /// <summary>
    /// Показує локалізований текст помилки роботи з файлом. Для
    /// <see cref="FamilyFileException"/> резолвить ключ; для решти винятків
    /// лишається технічне повідомлення .NET як остання лінія.
    /// </summary>
    private string DescribeFileError(Exception ex) => ex switch
    {
        FamilyFileException file => SafeFormat(file.MessageKey, file.Arguments),
        _ => ex.Message,
    };

    /// <summary>
    /// Попереджає користувача, що частину записів файлу пропущено. Без цього
    /// «зникнення» зв'язків виглядало б як безпричинна втрата даних.
    /// Документ помічається зміненим, щоб виправлення можна було зафіксувати збереженням.
    /// </summary>
    private void ReportRepairs(FamilyDocument document)
    {
        if (document.RepairedIssues.Count == 0)
        {
            return;
        }

        var lines = document.RepairedIssues
            .Select(issue => SafeFormat(issue.MessageKey, new object?[] { issue.Count }));

        var blocks = new[]
        {
            _localization.GetString("FileRepair_Intro"),
            string.Empty,
            string.Join(Environment.NewLine, lines),
            string.Empty,
            _localization.GetString("FileRepair_Outro"),
        };

        var text = string.Join(Environment.NewLine, blocks);

        _dialogs.ShowMessage(text, _localization.GetString("FileRepair_Title"));
        _session.MarkContentChanged();
    }

    /// <summary>
    /// Форматує локалізований шаблон, не падаючи на битому користувацькому перекладі:
    /// рядок може прийти з %AppData%\FamilyTree\languages\*.json, де описка в
    /// плейсхолдері («{0» замість «{0}») давала FormatException і «Неочікувану помилку».
    /// </summary>
    private string SafeFormat(string key, IReadOnlyList<object?> arguments)
    {
        var template = _localization.GetString(key);
        if (arguments.Count == 0)
        {
            return template;
        }

        try
        {
            return string.Format(template, arguments.ToArray());
        }
        catch (FormatException)
        {
            // Резервний вигляд: шаблон як є + аргументи, щоб інформація не зникла.
            return $"{template} ({string.Join(", ", arguments)})";
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

            // Плануємо вибір ДО сповіщення: RefreshPersons() з обробника ContentChanged
            // одразу виділить нову особу. Раніше тут був окремий виклик після
            // MarkContentChanged() — тобто список і дерево оновлювалися двічі.
            _pendingSelectionId = created.Id;
            _session.MarkContentChanged();
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
            // Особа вже виділена; RefreshPersons() з ContentChanged збереже вибір за Id
            // навіть якщо зміна імені перемістила її в сортуванні.
            _session.MarkContentChanged();
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
        if (SelectedPerson is not { } child)
        {
            return;
        }

        var pick = PickRelative(RelationshipRole.Parent, child);

        // Нову особу, створену просто з діалогу, треба зберегти навіть якщо
        // зв'язок у підсумку не додали (скасування або невдала валідація).
        var changed = pick.HasCreatedPersons;

        if (pick.Confirmed && pick.Candidate is { } parent && TryLinkParentChild(parent, child))
        {
            changed = true;
        }

        if (changed)
        {
            _session.MarkContentChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddChild()
    {
        if (SelectedPerson is not { } parent)
        {
            return;
        }

        var pick = PickRelative(RelationshipRole.Child, parent);
        var changed = pick.HasCreatedPersons;

        if (pick.Confirmed && pick.Candidate is { } child && TryLinkParentChild(parent, child))
        {
            changed = true;

            // Дитина майже завжди спільна з подружжям — пропонуємо додати
            // другого з батьків одразу, щоб не робити це окремим кроком.
            OfferSpouseAsSecondParent(parent, child);
        }

        if (changed)
        {
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

        var pick = PickRelative(RelationshipRole.Spouse, person);
        var changed = pick.HasCreatedPersons;

        if (pick.Confirmed && pick.Candidate is { } other)
        {
            var link = SpouseLink.Create(person.Id, other.Id, pick.MarriageDate, pick.DivorceDate, pick.Divorced);
            var result = _validator.ValidateSpouse(link, _session.Current.SpouseLinks);
            if (Accept(result))
            {
                _session.Current.SpouseLinks.Add(link);
                changed = true;
            }
        }

        if (changed)
        {
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

        var editor = RelationshipEditorViewModel.ForSpouseEdit(
            person, spouse, link.MarriageDate, link.DivorceDate, link.IsActive);
        if (_dialogs.ShowRelationshipEditor(editor))
        {
            link.MarriageDate = editor.MarriageDateOnly;
            link.DivorceDate = editor.DivorceDateOnly;
            link.Divorced = editor.Divorced;
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

    /// <summary>Результат діалогу вибору родича (щоб ViewModel діалогу не «протікала» далі).</summary>
    private readonly record struct RelativePick(
        bool Confirmed,
        Person? Candidate,
        bool HasCreatedPersons,
        DateOnly? MarriageDate,
        DateOnly? DivorceDate,
        bool Divorced);

    /// <summary>
    /// Відкриває діалог вибору родича. Усі вже прямі родичі базової особи (батьки,
    /// діти, подружжя) типово приховані: жоден із них не може взяти нову роль —
    /// подружжя не буває власною дитиною, а батько не буває власним сином.
    /// Знайти їх усе одно можна, знявши галочку «Приховати вже пов'язаних».
    /// </summary>
    private RelativePick PickRelative(RelationshipRole role, Person basePerson)
    {
        var editor = new RelationshipEditorViewModel(
            role,
            basePerson,
            _session.Current.Persons,
            DirectRelativeIds(basePerson),
            CreatePersonForRelationship);

        var confirmed = _dialogs.ShowRelationshipEditor(editor);

        return new RelativePick(
            confirmed,
            editor.SelectedCandidate,
            editor.HasCreatedPersons,
            editor.MarriageDateOnly,
            editor.DivorceDateOnly,
            editor.Divorced);
    }

    /// <summary>
    /// Створює особу з діалогу зв'язку: відкриває редактор особи й додає результат
    /// у документ. Позначення документа зміненим робить викликач — після того, як
    /// вирішиться доля самого зв'язку (щоб не оновлювати список двічі).
    /// </summary>
    private Person? CreatePersonForRelationship()
    {
        var editor = new PersonEditorViewModel();
        if (!_dialogs.ShowPersonEditor(editor) || editor.Result is not { } created)
        {
            return null;
        }

        _session.Current.Persons.Add(created);
        return created;
    }

    /// <summary>Id усіх прямих родичів особи: батьки, діти та подружжя.</summary>
    private List<Guid> DirectRelativeIds(Person person)
    {
        var doc = _session.Current;
        var ids = new List<Guid>();

        foreach (var link in doc.ParentChildLinks)
        {
            if (link.ChildId == person.Id)
            {
                ids.Add(link.ParentId);
            }
            else if (link.ParentId == person.Id)
            {
                ids.Add(link.ChildId);
            }
        }

        foreach (var link in doc.SpouseLinks)
        {
            if (link.SpouseOf(person.Id) is { } spouseId)
            {
                ids.Add(spouseId);
            }
        }

        return ids;
    }

    /// <summary>
    /// Додає зв'язок «батько/мати — дитина» з валідацією.
    /// Повертає true, якщо зв'язок реально додано.
    /// </summary>
    private bool TryLinkParentChild(Person parent, Person child)
    {
        var link = new ParentChildLink { ParentId = parent.Id, ChildId = child.Id };
        var result = _validator.ValidateParentChild(link, _session.Current.Persons, _session.Current.ParentChildLinks);
        if (!Accept(result))
        {
            return false;
        }

        _session.Current.ParentChildLinks.Add(link);
        return true;
    }

    /// <summary>
    /// Питає, чи є подружжя другим із батьків щойно доданої дитини, і за згодою
    /// додає другий зв'язок. Подружжя перебирається по черзі: «Ні» — питаємо про
    /// наступне, «Пізніше» — припиняємо опитування, «Так» — зв'язок додано й
    /// далі питати нема про що (більше двох батьків не буває).
    /// </summary>
    private void OfferSpouseAsSecondParent(Person parent, Person child)
    {
        var doc = _session.Current;

        // Двох батьків дитині досить — не пропонуємо третього.
        if (doc.ParentChildLinks.Count(l => l.ChildId == child.Id) >= 2)
        {
            return;
        }

        var byId = doc.Persons.DistinctBy(p => p.Id).ToDictionary(p => p.Id);

        // Чинний шлюб — першим: спільна дитина найімовірніше саме з ним.
        var spouses = doc.SpouseLinks
            .Where(l => l.Involves(parent.Id))
            .OrderByDescending(l => l.IsActive)
            .Select(l => l.SpouseOf(parent.Id))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .Where(id => byId.ContainsKey(id))
            .Select(id => byId[id])
            .Where(spouse => CanBeParentOf(spouse, child))
            .ToList();

        foreach (var spouse in spouses)
        {
            var question = SafeFormat(SecondParentKey(spouse), new object?[] { spouse.FullName, child.FullName });
            var choice = _dialogs.AskYesNoLater(question, _localization.GetString("Rel_SecondParent_Title"));

            if (choice == ThreeWayChoice.Later)
            {
                return;
            }

            if (choice == ThreeWayChoice.Yes)
            {
                TryLinkParentChild(spouse, child);
                return;
            }
        }
    }

    /// <summary>
    /// Чи може особа стати батьком/матір'ю дитини без помилки валідації.
    /// Перевіряємо заздалегідь, щоб не пропонувати варіант, який гарантовано
    /// впаде: дубль зв'язку, зайнятий слот батька/матері або цикл у дереві.
    /// </summary>
    private bool CanBeParentOf(Person candidate, Person child)
    {
        if (candidate.Id == child.Id)
        {
            return false;
        }

        var probe = new ParentChildLink { ParentId = candidate.Id, ChildId = child.Id };
        return _validator
            .ValidateParentChild(probe, _session.Current.Persons, _session.Current.ParentChildLinks)
            .IsValid;
    }

    /// <summary>Ключ питання про другого з батьків — за статтю подружжя.</summary>
    private static string SecondParentKey(Person spouse) => spouse.Gender switch
    {
        Gender.Female => "Rel_SecondParent_Mother",
        Gender.Male => "Rel_SecondParent_Father",
        _ => "Rel_SecondParent_Unknown",
    };

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
        string.Join(Environment.NewLine, messages.Select(m => SafeFormat(m.Key, m.Arguments)));

    // ---- Перемикачі (мова/тема/стиль) -----------------------------------

    partial void OnSelectedPersonChanged(Person? value)
    {
        // Під час перезаповнення списку виділення «блимає» через null — реагувати на це
        // не треба: RefreshPersons() застосує підсумковий вибір один раз.
        if (_suppressSelectionSync)
        {
            return;
        }

        ApplySelection(value);
    }

    /// <summary>
    /// Застосовує вибір особи: панель зв'язків + корінь дерева.
    /// Корінь НЕ скидається при <c>null</c>: особа могла лише не пройти фільтр пошуку,
    /// і гасити через це побудоване дерево — гірше, ніж лишити його на місці.
    /// Видалену особу дерево відкине саме (у <c>Rebuild</c> є перевірка graph.Contains).
    /// </summary>
    private void ApplySelection(Person? value)
    {
        RefreshRelations();

        if (value is not null)
        {
            _lastSelectedId = value.Id;
            _tree.SetRoot(value.Id);
        }
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
        // Пріоритет: явно запланований вибір → поточний → останній осмислений
        // (останній потрібен, щоб очищення пошуку повертало виділення, а не губило його).
        var targetId = _pendingSelectionId ?? SelectedPerson?.Id ?? _lastSelectedId;
        _pendingSelectionId = null;

        var query = _session.Current.Persons.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(p => PersonQuery.Matches(p, term));
        }

        var ordered = PersonQuery.Sort(query, SelectedSort?.Field ?? PersonSortField.LastName, SortDescending);

        // Перезаповнення списку з заглушеним round-trip'ом виділення: усі проміжні
        // значення SelectedPerson (у т.ч. null від ListBox на Clear) ігноруються,
        // а підсумковий вибір застосовується рівно один раз — після циклу.
        _suppressSelectionSync = true;
        try
        {
            Persons.Clear();
            foreach (var person in ordered)
            {
                Persons.Add(person);
            }

            SelectedPerson = targetId is { } id ? Persons.FirstOrDefault(p => p.Id == id) : null;
        }
        finally
        {
            _suppressSelectionSync = false;
        }

        // SetRoot усередині сам відкидає повторний вибір того самого кореня,
        // тож коли виділення не змінилося (сортування, пошук), дерево не перебудовується.
        ApplySelection(SelectedPerson);

        OnPropertyChanged(nameof(PersonsCountText));
        RaiseDocumentInfo();
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
        var byId = doc.Persons.DistinctBy(p => p.Id).ToDictionary(p => p.Id);

        // Скільки в кого дітей — один прохід по зв'язках замість перебору на кожну картку.
        var childCounts = doc.ParentChildLinks
            .GroupBy(l => l.ParentId)
            .ToDictionary(g => g.Key, g => g.Count());

        PersonCard Card(Person relative) =>
            _cards.Build(relative, doc, byId, childCounts.GetValueOrDefault(relative.Id));

        foreach (var link in doc.ParentChildLinks.Where(l => l.ChildId == person.Id))
        {
            if (byId.TryGetValue(link.ParentId, out var parent))
            {
                Parents.Add(Card(parent));
            }
        }

        foreach (var link in doc.ParentChildLinks.Where(l => l.ParentId == person.Id))
        {
            if (byId.TryGetValue(link.ChildId, out var child))
            {
                Children.Add(Card(child));
            }
        }

        foreach (var link in doc.SpouseLinks.Where(l => l.Involves(person.Id)))
        {
            if (link.SpouseOf(person.Id) is { } spouseId && byId.TryGetValue(spouseId, out var spouse))
            {
                Spouses.Add(Card(spouse));
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
        _pendingSelectionId = null;
        _lastSelectedId = null;
        _tree.SetRoot(null); // інший документ — старий корінь більше не має сенсу
        RefreshPersons();
    }

    // RefreshPersons() тепер сам застосовує вибір (а отже й RefreshRelations),
    // тож окремий виклик тут лише дублював роботу.
    private void OnContentChanged(object? sender, EventArgs e) => RefreshPersons();

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
