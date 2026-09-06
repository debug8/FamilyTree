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
using FamilyTree.Gedcom;
using FamilyTree.Storage;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Головна ViewModel: файлові операції (T-2.5), список осіб із пошуком (T-2.1),
/// CRUD осіб (T-2.2, T-2.3), керування зв'язками (T-2.4), перемикачі мови/теми/стилю.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int SearchDebounceMs = 300;

    // Коротка затримка коалесціює швидкі зміни виділення (затиснута стрілка у списку),
    // щоб не робити RefreshRelations + перебудову дерева на кожен проміжний запис (B-07).
    private const int SelectionDebounceMs = 120;
    private const int MaxRecentFiles = 8;

    // Розширення власного формату й формату обміну. Обидва потрібні в двох місцях
    // (ім'я-підказка + DefaultExt діалогу), тож винесені в константи.
    private const string FamilyExtension = ".familytree";
    private const string GedcomExtension = ".ged";

    private readonly ILocalizationService _localization;
    private readonly IThemeService _theme;
    private readonly IKinshipFormatter _kinshipFormatter;
    private readonly IDocumentSession _session;
    private readonly IPhotoStore _photos;
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
    private CancellationTokenSource? _selectionCts;

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
        ISettingsService settings,
        IPhotoStore photos)
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
        _photos = photos;
        _cards = new PersonCardBuilder(localization);

        _selectedLanguage = _localization.CurrentLanguage;
        _selectedTheme = _theme.CurrentTheme;
        _selectedNamingStyle = NamingStyles.First(s => s.Style == _kinshipFormatter.Style);

        LoadRecentFiles();

        _localization.LanguageChanged += OnLanguageChanged;
        _session.DocumentChanged += OnDocumentChanged;
        _session.ContentChanged += OnContentChanged;
        _tree.RootChanged += OnTreeRootChanged;

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

    private string GedcomFilter => _localization.GetString("File_FilterGedcom");

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
            // B-25: локалізований опис (як у відкритті/збереженні), а не сирий ex.Message.
            _dialogs.ShowMessage(DescribeFileError(ex), _localization.GetString("File_ErrorTitle"));
            return;
        }

        var plan = _merger.Plan(_session.Current, source);
        var report = plan.ToReport();

        var confirm = string.Format(
            _localization.GetString("Import_Confirm"),
            report.AddedPersons,
            report.DuplicatePersons,
            report.AddedParentLinks + report.AddedSpouseLinks);
        confirm += MergeExtras(report);
        if (!_dialogs.Confirm(confirm, _localization.GetString("Import_Title")))
        {
            return;
        }

        _merger.Apply(_session.Current, plan);
        _session.MarkContentChanged();

        var done = string.Format(
            _localization.GetString("Import_Done"), report.AddedPersons, report.DuplicatePersons);
        done += MergeExtras(report);
        _dialogs.ShowMessage(done, _localization.GetString("Import_Title"));
    }

    /// <summary>
    /// Додаткові рядки звіту злиття (доповнені особи, конфлікти полів, відхилені зв'язки).
    /// Порожньо, якщо нічого з цього немає — щоб типовий імпорт не обростав зайвим текстом.
    /// </summary>
    private string MergeExtras(MergeReport report)
    {
        var lines = new List<string>();
        if (report.UpdatedPersons > 0)
        {
            lines.Add(string.Format(_localization.GetString("Import_Updated"), report.UpdatedPersons));
        }

        if (report.Conflicts > 0)
        {
            lines.Add(string.Format(_localization.GetString("Import_Conflicts"), report.Conflicts));
        }

        if (report.RejectedLinks > 0)
        {
            lines.Add(string.Format(_localization.GetString("Import_Rejected"), report.RejectedLinks));
        }

        return lines.Count == 0
            ? string.Empty
            : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// «Зберегти копію з фото» — окремий файл, у який додатково вкладено мініатюри
    /// фотографій (~100 px, JPEG). Саме він призначений для надсилання родичам: у
    /// звичайному файлі мініатюр немає, бо base64 роздув би документ у рази й позбавив би
    /// його головної переваги — бути читаним JSON, придатним до diff і grep.
    /// <para>
    /// Як і експорт GEDCOM, це ОБМІН, а не збереження: шлях документа, список недавніх
    /// файлів і прапорець незбережених змін лишаються як були. Тому й пишемо не сам
    /// документ, а його копію — інакше <c>SaveAsync</c> проставив би їй час збереження
    /// й зняв «є незбережені зміни» з того, що користувач ще не зберіг.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task SaveCopyWithPhotos()
    {
        var suggested = DocumentName + " " + _localization.GetString("Photos_CopySuffix") + FamilyExtension;
        if (_dialogs.AskSavePath(FileFilter, suggested, FamilyExtension) is not { } path)
        {
            return;
        }

        try
        {
            var (copy, photos) = BuildCopyWithThumbnails();
            await _storage.SaveAsync(copy, path);

            _dialogs.ShowMessage(
                string.Format(
                    _localization.GetString("Photos_CopySaved"),
                    Path.GetFileName(path),
                    photos),
                _localization.GetString("Photos_CopyTitle"));
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(DescribeFileError(ex), _localization.GetString("File_ErrorTitle"));
        }
    }

    /// <summary>
    /// Копія документа з мініатюрами. Особи копіюються (<c>Person.Copy</c>), бо
    /// проставляти мініатюри у відкритий документ означало б змінювати те, що зараз
    /// редагує користувач. Зв'язки переносяться як є: копія живе лише до кінця запису.
    /// </summary>
    private (FamilyDocument Copy, int Photos) BuildCopyWithThumbnails()
    {
        var document = _session.Current;
        var copy = FamilyDocument.CreateNew(document.Meta.Title);
        copy.Meta.CreatedAt = document.Meta.CreatedAt;
        copy.Meta.UpdatedAt = document.Meta.UpdatedAt;

        var photos = 0;
        foreach (var person in document.Persons)
        {
            var clone = person.Copy();
            clone.PhotoThumbnail = _photos.CreateThumbnail(person.PhotoPath);
            if (clone.PhotoThumbnail is not null)
            {
                photos++;
            }

            copy.Persons.Add(clone);
        }

        copy.ParentChildLinks.AddRange(document.ParentChildLinks);
        copy.SpouseLinks.AddRange(document.SpouseLinks);

        return (copy, photos);
    }
    /// <summary>
    /// Експорт у GEDCOM 5.5.1 (T-5.2, Частина 4a). Це обмін, а не збереження:
    /// шлях документа, список недавніх файлів і прапорець змін лишаються як були,
    /// інакше «експортував — і документ став збереженим» вводило б в оману.
    /// </summary>
    [RelayCommand]
    private async Task ExportGedcom()
    {
        if (_dialogs.AskSavePath(GedcomFilter, DocumentName + GedcomExtension, GedcomExtension)
            is not { } path)
        {
            return;
        }

        try
        {
            // Дерево записів будуємо окремо від запису байтів: так лічильники
            // INDI/FAM для звіту беруться з готового результату, а не з другого
            // проходу по документу (де «родина» рахувалася б за іншим правилом).
            var tree = GedcomExporter.BuildTree(_session.Current, AppInfo.Version);
            await File.WriteAllBytesAsync(path, GedcomWriter.WriteBytes(tree));

            var done = string.Format(
                _localization.GetString("Gedcom_ExportDone"),
                tree.ChildrenOf("INDI").Count(),
                tree.ChildrenOf("FAM").Count());
            _dialogs.ShowMessage(done, _localization.GetString("Gedcom_Title"));
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(DescribeFileError(ex), _localization.GetString("File_ErrorTitle"));
        }
    }

    /// <summary>
    /// Імпорт GEDCOM (T-5.2, Частина 4b). На відміну від <see cref="Import"/> це не злиття,
    /// а ЗАМІЩЕННЯ документа (рішення до кодування: чужий файл приносить власні
    /// ідентифікатори й свою чистку), тож поводимося як «Відкрити».
    /// </summary>
    [RelayCommand]
    private async Task ImportGedcom()
    {
        if (!await PromptSaveIfDirtyAsync())
        {
            return;
        }

        if (_dialogs.AskOpenPath(GedcomFilter) is not { } path)
        {
            return;
        }

        FamilyDocument document;
        GedcomImportReport report;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            document = GedcomImporter.Import(bytes, out report);
        }
        catch (GedcomException ex)
        {
            // Власний тип шару обміну: несе ключ + аргументи, як FamilyFileException.
            _dialogs.ShowMessage(
                SafeFormat(ex.MessageKey, ex.Arguments), _localization.GetString("File_ErrorTitle"));
            return;
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(DescribeFileError(ex), _localization.GetString("File_ErrorTitle"));
            return;
        }

        // Шлях навмисно null: .ged — формат обміну, і «Зберегти» має вести у
        // «Зберегти як» на .familytree, а не переписувати вихідний файл.
        _session.SetDocument(document, null);

        // SetDocument скидає IsDirty у false, тож прапорець імпортера доводиться
        // ставити вручну — інакше нічим не збережений результат виглядав би збереженим.
        _session.MarkContentChanged();

        var done = string.Format(
            _localization.GetString("Gedcom_ImportDone"),
            report.Persons,
            report.Families,
            report.ParentChildLinks + report.SpouseLinks);
        done += GedcomExtras(report);
        _dialogs.ShowMessage(done, _localization.GetString("Gedcom_Title"));
    }

    /// <summary>
    /// Додаткові рядки звіту імпорту GEDCOM — за зразком <see cref="MergeExtras"/>:
    /// рядок з'являється лише тоді, коли є про що казати, тож чистий імпорт
    /// не обростає текстом. Замість невикористаного <c>GedcomWarn_RejectedLinks</c>
    /// показуємо <c>RepairedIssues</c> — імпортер чистить граф через
    /// <c>DocumentIntegrity</c>, а не через валідатор кожного ребра.
    /// </summary>
    private string GedcomExtras(GedcomImportReport report)
    {
        if (!report.HasWarnings)
        {
            return string.Empty;
        }

        var lines = new List<string>();

        if (report.EncodingFallbackFrom is { } declared)
        {
            lines.Add(SafeFormat(
                GedcomKeys.EncodingFallback, new object?[] { declared, report.EncodingName }));
        }

        if (report.MalformedLines > 0)
        {
            lines.Add(SafeFormat(GedcomKeys.MalformedLines, new object?[] { report.MalformedLines }));
        }

        if (report.SkippedRecords > 0)
        {
            lines.Add(SafeFormat(GedcomKeys.SkippedRecords, new object?[] { report.SkippedRecords }));
        }

        if (report.UnnamedPersons > 0)
        {
            lines.Add(SafeFormat(GedcomKeys.UnnamedPersons, new object?[] { report.UnnamedPersons }));
        }

        if (report.DeathsWithoutDate > 0)
        {
            lines.Add(SafeFormat(GedcomKeys.DeathWithoutDate, new object?[] { report.DeathsWithoutDate }));
        }

        if (report.TextOnlyDates > 0)
        {
            lines.Add(SafeFormat(GedcomKeys.TextOnlyDates, new object?[] { report.TextOnlyDates }));
        }

        if (report.SkippedTags.Count > 0)
        {
            var tags = string.Join(", ", report.TopSkippedTags().Select(pair => $"{pair.Key} ({pair.Value})"));
            lines.Add(SafeFormat(GedcomKeys.SkippedTags, new object?[] { report.SkippedTags.Count, tags }));
        }

        // Полагоджені дефекти графа — тими самими рядками, що й при відкритті
        // чужого .familytree (ключі FileRepair_*), щоб користувач бачив звичний текст.
        foreach (var issue in report.RepairedIssues)
        {
            lines.Add(SafeFormat(issue.MessageKey, new object?[] { issue.Count }));
        }

        return Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

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

    /// <summary>
    /// Синхронний варіант запиту про збереження — для <c>Application.SessionEnding</c> (B-05):
    /// завершення/вихід із сеансу Windows не проходить через <c>OnClosing</c> і не дає чекати
    /// на await, тож рішення й запис виконуються блокуюче (Windows дає лише кілька секунд).
    /// Повертає true, якщо можна завершувати (збережено або відкинуто), false — скасувати.
    /// </summary>
    public bool PromptSaveIfDirtyBlocking()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        switch (_dialogs.ConfirmSaveChanges(
            _localization.GetString("SaveChanges_Message"),
            _localization.GetString("SaveChanges_Title")))
        {
            case SaveChangesResult.Save:
                // Шлях визначаємо на UI-потоці (може відкритися «Зберегти як»),
                // а сам запис — блокуюче на пулі потоків, щоб уникнути дедлоку від
                // захопленого контексту синхронізації.
                var path = _session.FilePath;
                if (string.IsNullOrEmpty(path))
                {
                    if (_dialogs.AskSavePath(FileFilter, DocumentName + FamilyExtension, FamilyExtension)
                        is not { } chosen)
                    {
                        return false;
                    }

                    path = chosen;
                    _session.FilePath = path;
                }

                return WriteBlocking(path);

            case SaveChangesResult.Discard:
                return true;

            default:
                return false;
        }
    }

    private bool WriteBlocking(string path)
    {
        try
        {
            Task.Run(() => _storage.SaveAsync(_session.Current, path)).GetAwaiter().GetResult();
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

    private async Task<bool> SaveInternalAsync() =>
        string.IsNullOrEmpty(_session.FilePath)
            ? await SaveAsInternalAsync()
            : await WriteAsync(_session.FilePath);

    private async Task<bool> SaveAsInternalAsync()
    {
        var suggested = DocumentName + FamilyExtension;
        if (_dialogs.AskSavePath(FileFilter, suggested, FamilyExtension) is not { } path)
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

    /// <summary>
    /// Редактор особи з підключеними сервісами: діалоги й сховище потрібні для вибору
    /// фото, локалізація — для фільтра діалогу й повідомлення про помилку. Зібрано в
    /// одному місці, щоб три точки виклику не розійшлися складом залежностей.
    /// </summary>
    private PersonEditorViewModel NewPersonEditor(Person? existing = null) =>
        new(existing, _dialogs, _photos, _localization);

    [RelayCommand]
    private void AddPerson()
    {
        var editor = NewPersonEditor();
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

        var editor = NewPersonEditor(person);
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
            // B-19: змінені дати проганяємо через валідатор (як AddSpouse), а не пишемо наосліп.
            // Перевіряємо проти інших зв'язків (сам себе виключаємо, щоб не було хибного дубля).
            var candidate = SpouseLink.Create(
                link.Person1Id, link.Person2Id, editor.MarriageDate, editor.DivorceDate, editor.Divorced);
            var others = _session.Current.SpouseLinks.Where(l => l != link).ToList();
            if (!Accept(_validator.ValidateSpouse(candidate, others)))
            {
                return;
            }

            link.MarriageDate = editor.MarriageDate;
            link.DivorceDate = editor.DivorceDate;
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
        FamilyDate? MarriageDate,
        FamilyDate? DivorceDate,
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
            editor.MarriageDate,
            editor.DivorceDate,
            editor.Divorced);
    }

    /// <summary>
    /// Створює особу з діалогу зв'язку: відкриває редактор особи й додає результат
    /// у документ. Позначення документа зміненим робить викликач — після того, як
    /// вирішиться доля самого зв'язку (щоб не оновлювати список двічі).
    /// </summary>
    private Person? CreatePersonForRelationship()
    {
        var editor = NewPersonEditor();
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

        DebounceSelection();
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

    /// <summary>
    /// Дерево змінило корінь напряму (подвійний клік по вузлу) — робимо ту саму особу
    /// вибраною в застосунку, щоб список, вкладка «Особа» й наступний RefreshPersons()
    /// не «відкочували» корінь на попередню виділену особу (B-06). Якщо вибір уже той —
    /// нічого не робимо (напр. корінь щойно виставлено через ApplySelection зі списку).
    /// </summary>
    private void OnTreeRootChanged(object? sender, Guid rootId)
    {
        if (SelectedPerson?.Id == rootId)
        {
            return;
        }

        if (_session.Current.Persons.FirstOrDefault(p => p.Id == rootId) is { } person)
        {
            SelectedPerson = person;
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

    // Застосовує ОСТАННІЙ вибір після короткої паузи: проміжні значення (гортання
    // стрілками) скасовуються, тож важка робота (RefreshRelations + дерево) виконується
    // один раз для фінальної особи (B-07).
    private async void DebounceSelection()
    {
        _selectionCts?.Cancel();
        var cts = new CancellationTokenSource();
        _selectionCts = cts;
        try
        {
            await Task.Delay(SelectionDebounceMs, cts.Token).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!cts.IsCancellationRequested)
        {
            ApplySelection(SelectedPerson);
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

        // Фінальний вибір застосовуємо синхронно тут; скасовуємо відкладений дебаунс,
        // щоб він не спрацював повторно з тим самим вибором.
        _selectionCts?.Cancel();

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
        _tree.RootChanged -= OnTreeRootChanged;
        _searchCts?.Cancel();
        _selectionCts?.Cancel();
    }
}
