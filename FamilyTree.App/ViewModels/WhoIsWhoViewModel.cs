using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.App.Localization;
using FamilyTree.App.Services;
using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// ViewModel вкладки «Хто кому» (T-4.4): вибір двох осіб → назва зв'язку особи 2
/// відносно особи 1 та пояснення шляху (через <see cref="KinshipPathExplainer"/>).
/// </summary>
public partial class WhoIsWhoViewModel : ObservableObject
{
    private readonly IDocumentSession _session;
    private readonly KinshipPathExplainer _explainer;

    [ObservableProperty]
    private Person? _person1;

    [ObservableProperty]
    private Person? _person2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(NoResult))]
    private string? _resultSummary;

    public WhoIsWhoViewModel(IDocumentSession session, KinshipPathExplainer explainer, ILocalizationService localization)
    {
        _session = session;
        _explainer = explainer;
        _session.DocumentChanged += (_, _) => Reset();
        _session.ContentChanged += (_, _) => RefreshPersons();
        localization.LanguageChanged += (_, _) => Recompute(); // назви зв'язку перекладаються
        RefreshPersons();
    }

    /// <summary>Усі особи документа (відсортовані) для вибору.</summary>
    public ObservableCollection<Person> Persons { get; } = new();

    /// <summary>Кроки шляху родства.</summary>
    public ObservableCollection<string> Steps { get; } = new();

    public bool HasResult => !string.IsNullOrEmpty(ResultSummary);

    public bool NoResult => !HasResult;

    partial void OnPerson1Changed(Person? value) => Recompute();

    partial void OnPerson2Changed(Person? value) => Recompute();

    private void Recompute()
    {
        Steps.Clear();

        if (Person1 is not { } root || Person2 is not { } relative)
        {
            ResultSummary = null;
            return;
        }

        var doc = _session.Current;
        var graph = new FamilyGraph(doc.Persons, doc.ParentChildLinks, doc.SpouseLinks);
        var path = _explainer.Explain(root, relative, graph);

        ResultSummary = path.Summary;
        foreach (var step in path.Steps)
        {
            Steps.Add(step);
        }
    }

    private void RefreshPersons()
    {
        var previous1 = Person1?.Id;
        var previous2 = Person2?.Id;

        Persons.Clear();
        foreach (var person in _session.Current.Persons
                     .OrderBy(p => p.LastName, StringComparer.CurrentCulture)
                     .ThenBy(p => p.FirstName, StringComparer.CurrentCulture))
        {
            Persons.Add(person);
        }

        Person1 = Persons.FirstOrDefault(p => p.Id == previous1);
        Person2 = Persons.FirstOrDefault(p => p.Id == previous2);
    }

    private void Reset()
    {
        Person1 = null;
        Person2 = null;
        ResultSummary = null;
        Steps.Clear();
        RefreshPersons();
    }
}
