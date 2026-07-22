using FamilyTree.Domain;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Спільна тестова родина для тестів родства (T-3.1, T-3.2, згодом T-3.5).
/// Будується один раз (xUnit class fixture), граф незмінний. Українські та (майбутні)
/// англійські тести форматерів працюють на цих самих даних: «Влас → двоюрідний дядько»
/// відповідатиме «Vlas → first cousin once removed».
///
/// Корінь — Тарас. Схема:
///  G-4  Остап ═ Одарка
///  G-3      Богдан ═ Віра            Гнат (брат Богдана)
///  G-2      Іван ═ Марія   Галя      Петро (син Гната)
///  G-1  Андрій ═ Олена  Софія  Микола  Влас(син Галі)  Роман(син Петра)  Ірина(сестра Олени)
///  G0   ТАРАС ═ Катя  Леся  Марко(від Зої)  Настя(від Степана)  Юрко(син Софії)
///                     Дарина(дочка Ірини)  Ніна(дочка Власа)
///  G+1  Данило, Соломія, Дитя(стать невідома)  Влад(син Лесі)  Зірка(дочка Юрка)
///  G+2  Максим(син Данила)                      Тимко(син Влада)
///  G+3  Остапчик(син Максима)
/// </summary>
public sealed class KinshipTestFamily
{
    private readonly Dictionary<string, Person> _persons = new();
    private readonly List<ParentChildLink> _parentLinks = new();
    private readonly List<SpouseLink> _spouseLinks = new();

    public FamilyGraph Graph { get; }

    public Person this[string name] => _persons[name];

    public KinshipTestFamily()
    {
        Add("Остап", Gender.Male);
        Add("Одарка", Gender.Female);
        Marry("Остап", "Одарка");

        Add("Богдан", Gender.Male, "Остап", "Одарка");
        Add("Гнат", Gender.Male, "Остап", "Одарка");
        Add("Віра", Gender.Female);
        Marry("Богдан", "Віра");

        Add("Іван", Gender.Male, "Богдан", "Віра");
        Add("Галя", Gender.Female, "Богдан", "Віра");
        Add("Петро", Gender.Male, "Гнат");
        Add("Марія", Gender.Female);
        Marry("Іван", "Марія");

        Add("Андрій", Gender.Male, "Іван", "Марія");
        Add("Софія", Gender.Female, "Іван", "Марія");
        Add("Микола", Gender.Male, "Іван", "Марія");
        Add("Влас", Gender.Male, "Галя");
        Add("Роман", Gender.Male, "Петро");

        Add("ДідПоМатері", Gender.Male);
        Add("БабаПоМатері", Gender.Female);
        Add("Олена", Gender.Female, "ДідПоМатері", "БабаПоМатері");
        Add("Ірина", Gender.Female, "ДідПоМатері", "БабаПоМатері");
        Marry("Андрій", "Олена");

        Add("Зоя", Gender.Female);
        Add("Степан", Gender.Male);

        Add("Тарас", Gender.Male, "Андрій", "Олена");   // ROOT
        Add("Леся", Gender.Female, "Андрій", "Олена");
        Add("Марко", Gender.Male, "Андрій", "Зоя");      // спільний лише батько
        Add("Настя", Gender.Female, "Олена", "Степан");  // спільна лише мати
        Add("Юрко", Gender.Male, "Софія");
        Add("Дарина", Gender.Female, "Ірина");
        Add("Ніна", Gender.Female, "Влас");
        Add("Катя", Gender.Female);
        Marry("Тарас", "Катя");

        Add("Данило", Gender.Male, "Тарас", "Катя");
        Add("Соломія", Gender.Female, "Тарас", "Катя");
        Add("Дитя", Gender.Unknown, "Тарас", "Катя");    // стать невідома
        Add("Влад", Gender.Male, "Леся");
        Add("Зірка", Gender.Female, "Юрко");
        Add("Максим", Gender.Male, "Данило");
        Add("Тимко", Gender.Male, "Влад");
        Add("Остапчик", Gender.Male, "Максим");

        Graph = new FamilyGraph(_persons.Values, _parentLinks, _spouseLinks);
    }

    /// <summary>
    /// Ізольований прямий «ланцюжок» глибиною depth з двох боків від спільного предка —
    /// для тестів далеких кузенів (п'ятиюрідні тощо).
    /// </summary>
    public static (FamilyGraph Graph, Person Left, Person Right) DeepCousinChain(int depth)
    {
        var persons = new List<Person>();
        var links = new List<ParentChildLink>();

        Person New(string name)
        {
            var p = new Person { LastName = name, FirstName = name, Gender = Gender.Male };
            persons.Add(p);
            return p;
        }

        var ancestor = New("Корінь");
        var left = ancestor;
        var right = ancestor;
        for (var i = 0; i < depth; i++)
        {
            var nextLeft = New($"L{i}");
            var nextRight = New($"R{i}");
            links.Add(new ParentChildLink { ParentId = left.Id, ChildId = nextLeft.Id });
            links.Add(new ParentChildLink { ParentId = right.Id, ChildId = nextRight.Id });
            left = nextLeft;
            right = nextRight;
        }

        return (new FamilyGraph(persons, links, Array.Empty<SpouseLink>()), left, right);
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
