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

    public bool IsSpouse => Role == RelationshipRole.Spouse;

    public string TitleKey => Role switch
    {
        RelationshipRole.Parent => "Rel_AddParent",
        RelationshipRole.Child => "Rel_AddChild",
        _ => "Rel_AddSpouse",
    };

    public ObservableCollection<Person> Candidates { get; } = new();

    public bool CanConfirm => SelectedCandidate is not null;

    public DateOnly? MarriageDateOnly => MarriageDate is { } d ? DateOnly.FromDateTime(d) : null;

    public DateOnly? DivorceDateOnly => DivorceDate is { } d ? DateOnly.FromDateTime(d) : null;

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
