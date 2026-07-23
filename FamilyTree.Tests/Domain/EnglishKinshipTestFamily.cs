using FamilyTree.Domain;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Дзеркало <see cref="KinshipTestFamily"/> з англійськими іменами — для тестів
/// англійського форматера (T-3.5). Структура (ролі й покоління) ідентична.
/// Корінь — Taras.
/// </summary>
public sealed class EnglishKinshipTestFamily
{
    private readonly Dictionary<string, Person> _persons = new();
    private readonly List<ParentChildLink> _parentLinks = new();
    private readonly List<SpouseLink> _spouseLinks = new();

    public FamilyGraph Graph { get; }

    public Person this[string name] => _persons[name];

    public EnglishKinshipTestFamily()
    {
        Add("Ostap", Gender.Male);
        Add("Odarka", Gender.Female);
        Marry("Ostap", "Odarka");

        Add("Bohdan", Gender.Male, "Ostap", "Odarka");
        Add("Hnat", Gender.Male, "Ostap", "Odarka");
        Add("Vira", Gender.Female);
        Marry("Bohdan", "Vira");

        Add("Ivan", Gender.Male, "Bohdan", "Vira");
        Add("Halyna", Gender.Female, "Bohdan", "Vira");
        Add("Petro", Gender.Male, "Hnat");
        Add("Maria", Gender.Female);
        Marry("Ivan", "Maria");

        Add("Andriy", Gender.Male, "Ivan", "Maria");
        Add("Sofia", Gender.Female, "Ivan", "Maria");
        Add("Mykola", Gender.Male, "Ivan", "Maria");
        Add("Vlas", Gender.Male, "Halyna");
        Add("Roman", Gender.Male, "Petro");

        Add("GrandpaM", Gender.Male);
        Add("GrandmaM", Gender.Female);
        Add("Olena", Gender.Female, "GrandpaM", "GrandmaM");
        Add("Iryna", Gender.Female, "GrandpaM", "GrandmaM");
        Marry("Andriy", "Olena");

        Add("Zoia", Gender.Female);
        Add("Stepan", Gender.Male);

        Add("Taras", Gender.Male, "Andriy", "Olena");   // ROOT
        Add("Lesia", Gender.Female, "Andriy", "Olena");
        Add("Marko", Gender.Male, "Andriy", "Zoia");    // спільний лише батько
        Add("Nastia", Gender.Female, "Olena", "Stepan"); // спільна лише мати
        Add("Yurko", Gender.Male, "Sofia");
        Add("Daryna", Gender.Female, "Iryna");
        Add("Nina", Gender.Female, "Vlas");
        Add("Katya", Gender.Female);
        Marry("Taras", "Katya");

        Add("Danylo", Gender.Male, "Taras", "Katya");
        Add("Solomia", Gender.Female, "Taras", "Katya");
        Add("Child", Gender.Unknown, "Taras", "Katya");
        Add("Vlad", Gender.Male, "Lesia");
        Add("Zirka", Gender.Female, "Yurko");
        Add("Maksym", Gender.Male, "Danylo");
        Add("Tymko", Gender.Male, "Vlad");
        Add("Ostapchyk", Gender.Male, "Maksym");

        Graph = new FamilyGraph(_persons.Values, _parentLinks, _spouseLinks);
    }

    private void Add(string name, Gender gender, params string[] parents)
    {
        var person = new Person { LastName = name, FirstName = name, Gender = gender };
        _persons[name] = person;
        foreach (var parent in parents)
        {
            _parentLinks.Add(new ParentChildLink { ParentId = _persons[parent].Id, ChildId = person.Id });
        }
    }

    private void Marry(string first, string second) =>
        _spouseLinks.Add(SpouseLink.Create(_persons[first].Id, _persons[second].Id));
}
