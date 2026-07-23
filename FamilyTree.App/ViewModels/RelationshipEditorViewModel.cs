using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.Domain;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// ViewModel діалогу додавання зв'язку (розд. 6.3): вибір другої особи (з пошуком)
/// та, для подружжя, дати шлюбу/розлучення.
/// </summary>
public partial class RelationshipEditorViewModel : ObservableObject
{
    private readonly List<Person> _candidates;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private Person? _selectedCandidate;

    [ObservableProperty]
    private DateTime? _marriageDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDivorceDate))]
    private bool _isMarried = true;

    [ObservableProperty]
    private DateTime? _divorceDate;

    public RelationshipEditorViewModel(RelationshipRole role, Person basePerson, IEnumerable<Person> allPersons)
    {
        Role = role;
        BasePerson = basePerson;
        _candidates = allPersons.Where(p => p.Id != basePerson.Id).ToList();
        RefreshCandidates();
    }

    public RelationshipRole Role { get; }

    public Person BasePerson { get; }

    /// <summary>Режим редагування наявного зв'язку (особу-контрагента вибрано й зафіксовано).</summary>
    public bool IsEditMode { get; private init; }

    /// <summary>Чи можна змінювати особу-контрагента (ні — у режимі редагування).</summary>
    public bool CanPickCandidate => !IsEditMode;

    /// <summary>Ключ підпису кнопки підтвердження.</summary>
    public string ConfirmKey => IsEditMode ? "Common_Save" : "Common_Add";

    public bool IsSpouse => Role == RelationshipRole.Spouse;

    /// <summary>Створює VM для редагування дат наявного подружжя.</summary>
    public static RelationshipEditorViewModel ForSpouseEdit(
        Person basePerson, Person spouse, DateOnly? marriageDate, DateOnly? divorceDate)
    {
        var vm = new RelationshipEditorViewModel(RelationshipRole.Spouse, basePerson, new[] { spouse })
        {
            IsEditMode = true,
        };
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

    partial void OnIsMarriedChanged(bool value)
    {
        if (value)
        {
            DivorceDate = null;
        }
    }

    partial void OnSearchTextChanged(string? value) => RefreshCandidates();

    partial void OnSelectedCandidateChanged(Person? value) => OnPropertyChanged(nameof(CanConfirm));

    private void RefreshCandidates()
    {
        IEnumerable<Person> query = _candidates;

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

        Candidates.Clear();
        foreach (var person in ordered)
        {
            Candidates.Add(person);
        }
    }
}
