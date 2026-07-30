using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyTree.App.Localization;
using FamilyTree.Domain;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// ViewModel діалогу додавання зв'язку (розд. 6.3): вибір другої особи
/// (пошук + фільтри + сортування), створення нової особи «на місці»
/// та, для подружжя, дати шлюбу/розлучення.
/// </summary>
public partial class RelationshipEditorViewModel : ObservableObject
{
    private readonly List<Person> _candidates;

    // Особи, вже пов'язані з базовою в цій самій ролі. Валідатор усе одно
    // відхилив би дубль, тому за замовчуванням просто не показуємо їх.
    private readonly HashSet<Guid> _relatedIds;

    // Створення нової особи делегується власнику (MainViewModel): він відкриває
    // редактор і додає результат у документ. Діалог не знає ні про сесію, ні про сховище.
    private readonly Func<Person?>? _createPerson;

    // Захист від каскаду перефільтрувань, коли кілька властивостей змінюються разом.
    private bool _suppressRefresh;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private Person? _selectedCandidate;

    [ObservableProperty]
    private GenderFilterOption _selectedGender = PersonFilterOptions.Genders[0];

    [ObservableProperty]
    private LifeStatusFilterOption _selectedLifeStatus = PersonFilterOptions.LifeStatuses[0];

    [ObservableProperty]
    private PersonSortOption _selectedSort = PersonFilterOptions.Sorts[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionGlyph))]
    private bool _sortDescending;

    /// <summary>Приховувати осіб, які вже в цьому зв'язку (типово — так).</summary>
    [ObservableProperty]
    private bool _hideRelated = true;

    [ObservableProperty]
    private DateTime? _marriageDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDivorceDate))]
    private bool _isMarried = true;

    [ObservableProperty]
    private DateTime? _divorceDate;

    public RelationshipEditorViewModel(
        RelationshipRole role,
        Person basePerson,
        IEnumerable<Person> allPersons,
        IEnumerable<Guid>? relatedIds = null,
        Func<Person?>? createPerson = null,
        bool isEditMode = false)
    {
        Role = role;
        BasePerson = basePerson;
        _candidates = allPersons.Where(p => p.Id != basePerson.Id).ToList();
        _relatedIds = relatedIds is null ? new HashSet<Guid>() : new HashSet<Guid>(relatedIds);
        _createPerson = createPerson;

        // IsEditMode виставляємо ДО першої фільтрації: інакше зафіксований
        // контрагент проходив би через фільтри й міг би зникнути зі списку.
        IsEditMode = isEditMode;

        RefreshCandidates();
    }

    public RelationshipRole Role { get; }

    public Person BasePerson { get; }

    /// <summary>Режим редагування наявного зв'язку (особу-контрагента вибрано й зафіксовано).</summary>
    public bool IsEditMode { get; }

    /// <summary>Чи можна змінювати особу-контрагента (ні — у режимі редагування).</summary>
    public bool CanPickCandidate => !IsEditMode;

    /// <summary>Чи показувати панель пошуку/фільтрів (у режимі редагування вона зайва).</summary>
    public bool ShowFilters => !IsEditMode;

    /// <summary>Чи доступне створення нової особи прямо з діалогу.</summary>
    public bool CanCreatePerson => !IsEditMode && _createPerson is not null;

    /// <summary>Чи було створено хоч одну особу (власник має позначити документ зміненим).</summary>
    public bool HasCreatedPersons { get; private set; }

    /// <summary>Ключ підпису кнопки підтвердження.</summary>
    public string ConfirmKey => IsEditMode ? "Common_Save" : "Common_Add";

    public bool IsSpouse => Role == RelationshipRole.Spouse;

    public IReadOnlyList<GenderFilterOption> GenderFilters => PersonFilterOptions.Genders;

    public IReadOnlyList<LifeStatusFilterOption> LifeStatusFilters => PersonFilterOptions.LifeStatuses;

    public IReadOnlyList<PersonSortOption> SortOptions => PersonFilterOptions.Sorts;

    /// <summary>Стрілка напрямку сортування: ▲ за зростанням, ▼ за спаданням.</summary>
    public string SortDirectionGlyph => SortDescending ? "▼" : "▲";

    /// <summary>Кількість знайдених кандидатів (підпис під списком).</summary>
    public string CandidatesSummary
    {
        get
        {
            try
            {
                return string.Format(
                    LocalizationSource.Instance["Rel_CandidatesFound"], Candidates.Count, _candidates.Count);
            }
            catch (FormatException)
            {
                // Описка в плейсхолдері користувацького перекладу (напр. «{0» замість «{0}»)
                // не має ламати діалог — показуємо самі числа.
                return $"{Candidates.Count} / {_candidates.Count}";
            }
        }
    }

    /// <summary>Створює VM для редагування дат наявного подружжя.</summary>
    public static RelationshipEditorViewModel ForSpouseEdit(
        Person basePerson, Person spouse, DateOnly? marriageDate, DateOnly? divorceDate)
    {
        var vm = new RelationshipEditorViewModel(
            RelationshipRole.Spouse, basePerson, new[] { spouse }, isEditMode: true);

        vm.SelectedCandidate = spouse;
        vm.IsMarried = divorceDate is null;
        vm.MarriageDate = marriageDate is { } m ? m.ToDateTime(TimeOnly.MinValue) : null;
        vm.DivorceDate = divorceDate is { } d ? d.ToDateTime(TimeOnly.MinValue) : null;
        return vm;
    }

    /// <summary>Показувати дату розлучення (лише для подружжя, коли шлюб не чинний).</summary>
    public bool ShowDivorceDate => IsSpouse && !IsMarried;

    public string TitleKey => IsEditMode
        ? "Rel_EditSpouse"
        : Role switch
        {
            RelationshipRole.Parent => "Rel_AddParent",
            RelationshipRole.Child => "Rel_AddChild",
            _ => "Rel_AddSpouse",
        };

    public ObservableCollection<Person> Candidates { get; } = new();

    public bool CanConfirm => SelectedCandidate is not null;

    public DateOnly? MarriageDateOnly => MarriageDate is { } d ? DateOnly.FromDateTime(d) : null;

    public DateOnly? DivorceDateOnly =>
        !IsMarried && DivorceDate is { } d ? DateOnly.FromDateTime(d) : null;

    /// <summary>
    /// Скидає пошук і фільтри до типових значень (одним перефільтруванням).
    /// Сортування не чіпаємо — це не фільтр, і користувач обрав його свідомо.
    /// </summary>
    [RelayCommand]
    private void ResetFilters()
    {
        _suppressRefresh = true;
        try
        {
            SearchText = null;
            SelectedGender = PersonFilterOptions.Genders[0];
            SelectedLifeStatus = PersonFilterOptions.LifeStatuses[0];
            HideRelated = true;
        }
        finally
        {
            _suppressRefresh = false;
        }

        RefreshCandidates();
    }

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

    /// <summary>
    /// Створює нову особу й одразу робить її вибраним кандидатом: залишається
    /// натиснути «Додати» — родинний зв'язок буде додано автоматично.
    /// </summary>
    [RelayCommand]
    private void CreatePerson()
    {
        if (_createPerson?.Invoke() is not { } created)
        {
            return;
        }

        _candidates.Add(created);
        HasCreatedPersons = true;

        // Фільтри скидаємо, щоб нова особа гарантовано була у списку
        // (інакше вона могла б не пройти активний фільтр статі чи пошук).
        ResetFilters();
        SelectedCandidate = Candidates.FirstOrDefault(p => p.Id == created.Id);
    }

    partial void OnIsMarriedChanged(bool value)
    {
        if (value)
        {
            DivorceDate = null;
        }
    }

    partial void OnSearchTextChanged(string? value) => RefreshCandidates();

    partial void OnSelectedGenderChanged(GenderFilterOption value) => RefreshCandidates();

    partial void OnSelectedLifeStatusChanged(LifeStatusFilterOption value) => RefreshCandidates();

    partial void OnSelectedSortChanged(PersonSortOption value) => RefreshCandidates();

    partial void OnSortDescendingChanged(bool value) => RefreshCandidates();

    partial void OnHideRelatedChanged(bool value) => RefreshCandidates();

    partial void OnSelectedCandidateChanged(Person? value) => OnPropertyChanged(nameof(CanConfirm));

    private void RefreshCandidates()
    {
        if (_suppressRefresh)
        {
            return;
        }

        // У режимі редагування дат контрагент зафіксований — фільтри його не торкаються.
        var query = IsEditMode ? _candidates.AsEnumerable() : Filter(_candidates);
        var ordered = PersonQuery.Sort(query, SelectedSort?.Field ?? PersonSortField.LastName, SortDescending);

        // Вибір зберігаємо, якщо особа лишилась у списку, інакше знімаємо —
        // щоб не можна було підтвердити невидимого кандидата.
        var previousId = SelectedCandidate?.Id;

        Candidates.Clear();
        foreach (var person in ordered)
        {
            Candidates.Add(person);
        }

        SelectedCandidate = previousId is { } id ? Candidates.FirstOrDefault(p => p.Id == id) : null;
        OnPropertyChanged(nameof(CandidatesSummary));
    }

    private IEnumerable<Person> Filter(IEnumerable<Person> source)
    {
        var query = source;

        if (HideRelated && _relatedIds.Count > 0)
        {
            query = query.Where(p => !_relatedIds.Contains(p.Id));
        }

        if (SelectedGender?.Value is { } gender)
        {
            query = query.Where(p => p.Gender == gender);
        }

        query = SelectedLifeStatus?.Value switch
        {
            LifeStatus.Alive => query.Where(p => p.IsAlive),
            LifeStatus.Deceased => query.Where(p => !p.IsAlive),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(p => PersonQuery.Matches(p, term));
        }

        return query;
    }
}
