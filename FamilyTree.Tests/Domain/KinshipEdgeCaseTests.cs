using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Крайові випадки розрахунку родства, які раніше давали неправильну назву:
/// шлюб між кровними родичами, свояцтво після розлучення та «напіврідність»,
/// стверджена на неповних даних.
/// </summary>
public class KinshipEdgeCaseTests
{
    private readonly Dictionary<string, Person> _p = new();
    private readonly List<ParentChildLink> _pc = new();
    private readonly List<SpouseLink> _sp = new();
    private readonly UkrainianKinshipFormatter _ukFormatter = new();
    private readonly EnglishKinshipFormatter _enFormatter = new();
    private readonly KinshipCalculator _uk;
    private readonly KinshipCalculator _en;

    public KinshipEdgeCaseTests()
    {
        var finder = new CommonAncestorFinder();
        _uk = new KinshipCalculator(finder, _ukFormatter);
        _en = new KinshipCalculator(finder, _enFormatter);
    }

    private FamilyGraph Graph() => new(_p.Values, _pc, _sp);

    private void Person(string name, Gender g) =>
        _p[name] = new Person { LastName = name, FirstName = name, Gender = g };

    private void Parent(string parent, string child) =>
        _pc.Add(new ParentChildLink { ParentId = _p[parent].Id, ChildId = _p[child].Id });

    private void Marry(string a, string b, bool divorced = false) =>
        _sp.Add(SpouseLink.Create(
            _p[a].Id,
            _p[b].Id,
            new DateOnly(2000, 1, 1),
            divorced ? new DateOnly(2010, 1, 1) : null));

    private string Uk(string root, string relative) =>
        _uk.Compute(_p[root], _p[relative], Graph(), includeAffinity: true).DisplayName;

    private string En(string root, string relative) =>
        _en.Compute(_p[root], _p[relative], Graph(), includeAffinity: true).DisplayName;

    private KinshipResult Result(string root, string relative) =>
        _uk.Compute(_p[root], _p[relative], Graph(), includeAffinity: true);

    // =====================================================================
    // Шлюб між кровними родичами: подружжя не має «зникати»
    // =====================================================================

    /// <summary>
    /// Дід ═ Баба → Батько, Дядько; Батько → Він, Дядько → Вона; Він ═ Вона.
    /// Він і Вона — двоюрідні, і при цьому подружжя.
    /// </summary>
    private void BuildCousinMarriage(bool divorced = false)
    {
        Person("Дід", Gender.Male);
        Person("Баба", Gender.Female);
        Person("Батько", Gender.Male);
        Person("Дядько", Gender.Male);
        Person("Він", Gender.Male);
        Person("Вона", Gender.Female);

        Parent("Дід", "Батько");
        Parent("Баба", "Батько");
        Parent("Дід", "Дядько");
        Parent("Баба", "Дядько");
        Parent("Батько", "Він");
        Parent("Дядько", "Вона");

        Marry("Дід", "Баба");
        Marry("Він", "Вона", divorced);
    }

    [Fact]
    public void Cousin_wife_is_spouse_first_blood_relation_second()
    {
        // Раніше перевірка шлюбу стояла лише в гілці «кровного зв'язку немає»,
        // тож власна дружина показувалася як «двоюрідна сестра».
        BuildCousinMarriage();

        Uk("Він", "Вона").ShouldBe("дружина (також двоюрідна сестра)");
        Uk("Вона", "Він").ShouldBe("чоловік (також двоюрідний брат)");
    }

    [Fact]
    public void Cousin_marriage_keeps_spouse_kind()
    {
        // Найважливіше: KinshipKind.Spouse більше не губиться — на нього спираються
        // рамка шлюбу на дереві та інші споживачі.
        BuildCousinMarriage();

        Result("Він", "Вона").Kind.ShouldBe(KinshipKind.Spouse);
    }

    [Fact]
    public void Cousin_marriage_keeps_common_ancestors_for_path()
    {
        BuildCousinMarriage();

        Result("Він", "Вона").CommonAncestorIds
            .ShouldBe(new[] { _p["Дід"].Id, _p["Баба"].Id }, ignoreOrder: true);
    }

    [Fact]
    public void Divorced_cousin_marriage_keeps_both_facts()
    {
        BuildCousinMarriage(divorced: true);

        Uk("Він", "Вона").ShouldBe("колишня дружина (також двоюрідна сестра)");
    }

    [Fact]
    public void English_cousin_marriage()
    {
        BuildCousinMarriage();

        En("Він", "Вона").ShouldBe("wife (also first cousin)");
    }

    [Fact]
    public void Spouse_without_blood_relation_has_no_suffix()
    {
        Person("Він", Gender.Male);
        Person("Вона", Gender.Female);
        Marry("Він", "Вона");

        Uk("Він", "Вона").ShouldBe("дружина");
        En("Він", "Вона").ShouldBe("wife");
    }

    // =====================================================================
    // Свояцтво після розлучення
    // =====================================================================

    [Fact]
    public void Former_wifes_mother_is_former_mother_in_law()
    {
        // Раніше IsFormerSpouse для свояцтва було зашито в false, тож мати колишньої
        // дружини лишалася просто «тещею» — попри те, що свояцтво припинилося.
        Person("Я", Gender.Male);
        Person("Дружина", Gender.Female);
        Person("Теща", Gender.Female);
        Parent("Теща", "Дружина");
        Marry("Я", "Дружина", divorced: true);

        Uk("Я", "Теща").ShouldBe("колишня теща");
        En("Я", "Теща").ShouldBe("former mother-in-law");
    }

    [Fact]
    public void Current_wifes_mother_has_no_former_prefix()
    {
        Person("Я", Gender.Male);
        Person("Дружина", Gender.Female);
        Person("Теща", Gender.Female);
        Parent("Теща", "Дружина");
        Marry("Я", "Дружина");

        Uk("Я", "Теща").ShouldBe("теща");
        En("Я", "Теща").ShouldBe("mother-in-law");
    }

    [Fact]
    public void Active_marriage_wins_over_dissolved_one()
    {
        // Послідовний шлюб із двома сестрами: Теща — мати обох. Свояцтво через чинний
        // шлюб має перемагати свояцтво через розірваний, хоч відстань однакова.
        Person("Я", Gender.Male);
        Person("Перша", Gender.Female);
        Person("Друга", Gender.Female);
        Person("Теща", Gender.Female);
        Parent("Теща", "Перша");
        Parent("Теща", "Друга");
        Marry("Я", "Перша", divorced: true);
        Marry("Я", "Друга");

        Uk("Я", "Теща").ShouldBe("теща");
    }

    [Fact]
    public void Former_step_parent_is_marked_as_former()
    {
        // Батько розлучився з мачухою → «колишня мачуха».
        Person("Батько", Gender.Male);
        Person("Мачуха", Gender.Female);
        Person("Дитина", Gender.Male);
        Parent("Батько", "Дитина");
        Marry("Батько", "Мачуха", divorced: true);

        Uk("Дитина", "Мачуха").ShouldBe("колишня мачуха");
        En("Дитина", "Мачуха").ShouldBe("former stepmother");
    }

    // =====================================================================
    // Напіврідність не стверджується на неповних даних
    // =====================================================================

    [Fact]
    public void Shared_single_known_parent_does_not_claim_half_sibling()
    {
        // Обидва мають лише батька — про матерів у файлі нічого немає. Раніше це
        // давало «єдинокровний брат», тобто застосунок стверджував, що матері різні.
        Person("Батько", Gender.Male);
        Person("СинА", Gender.Male);
        Person("СинБ", Gender.Male);
        Parent("Батько", "СинА");
        Parent("Батько", "СинБ");

        Result("СинА", "СинБ").SiblingKind.ShouldBe(SiblingKind.PossiblyHalf);
        Uk("СинА", "СинБ").ShouldBe("брат");
        En("СинА", "СинБ").ShouldBe("brother");
    }

    [Fact]
    public void Detailed_style_admits_the_uncertainty()
    {
        Person("Батько", Gender.Male);
        Person("СинА", Gender.Male);
        Person("Сестра", Gender.Female);
        Parent("Батько", "СинА");
        Parent("Батько", "Сестра");

        _ukFormatter.Style = KinshipNamingStyle.Detailed;
        _enFormatter.Style = KinshipNamingStyle.Detailed;

        Uk("СинА", "Сестра").ShouldBe("сестра (можливо неповнорідна)");
        En("СинА", "Сестра").ShouldBe("sister (possibly half)");
    }

    [Fact]
    public void Known_different_second_parents_still_give_half_sibling()
    {
        // Регресія: коли обидві матері відомі й різні, напіврідність — факт.
        Person("Батько", Gender.Male);
        Person("Мати1", Gender.Female);
        Person("Мати2", Gender.Female);
        Person("СинА", Gender.Male);
        Person("СинБ", Gender.Male);
        Parent("Батько", "СинА");
        Parent("Мати1", "СинА");
        Parent("Батько", "СинБ");
        Parent("Мати2", "СинБ");

        Result("СинА", "СинБ").SiblingKind.ShouldBe(SiblingKind.HalfPaternal);
        Uk("СинА", "СинБ").ShouldBe("єдинокровний брат");
        En("СинА", "СинБ").ShouldBe("half-brother");
    }

    [Fact]
    public void Full_siblings_are_unaffected()
    {
        Person("Батько", Gender.Male);
        Person("Мати", Gender.Female);
        Person("СинА", Gender.Male);
        Person("СинБ", Gender.Male);
        Parent("Батько", "СинА");
        Parent("Мати", "СинА");
        Parent("Батько", "СинБ");
        Parent("Мати", "СинБ");

        Result("СинА", "СинБ").SiblingKind.ShouldBe(SiblingKind.Full);
        Uk("СинА", "СинБ").ShouldBe("брат");
    }
}
