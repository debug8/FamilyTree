using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Нерідні батьки та діти (вітчим/мачуха, пасинок/пасербиця).
/// До цього фіксу обидва напрямки давали <see cref="KinshipKind.None"/> —
/// «родинний зв'язок не встановлено», бо в MapPatternA не було плеча (1, 0),
/// а в MapPatternB — (0, 1). Це не теоретичний випадок: власний
/// <c>DemoFamilyGenerator.AddHalfSibling</c> створює саме таку конфігурацію,
/// тож у демо-родині були особи з «немає зв'язку».
///
/// Схема:
///   Вітчим ═ Мати ═ Батько ═ Мачуха
///                 │           │
///              Дитина      Пасербиця (від першого шлюбу Мачухи)
/// </summary>
public class StepFamilyTests
{
    private readonly Dictionary<string, Person> _p = new();
    private readonly List<ParentChildLink> _pc = new();
    private readonly List<SpouseLink> _sp = new();
    private readonly KinshipCalculator _uk;
    private readonly KinshipCalculator _en;

    public StepFamilyTests()
    {
        Person("Батько", Gender.Male);
        Person("Мати", Gender.Female);
        Person("Мачуха", Gender.Female);   // друга дружина Батька
        Person("Вітчим", Gender.Male);     // другий чоловік Матері
        Person("Дитина", Gender.Male);     // спільна дитина Батька й Матері
        Person("Пасербиця", Gender.Female); // дочка Мачухи від першого шлюбу
        Person("НевідомоХто", Gender.Unknown); // дитина Мачухи, стать невідома

        Children("Батько", "Мати", "Дитина");
        Parent("Мачуха", "Пасербиця");
        Parent("Мачуха", "НевідомоХто");

        Marry("Батько", "Мати", divorced: true);
        Marry("Батько", "Мачуха");
        Marry("Мати", "Вітчим");

        var finder = new CommonAncestorFinder();
        _uk = new KinshipCalculator(finder, new UkrainianKinshipFormatter());
        _en = new KinshipCalculator(finder, new EnglishKinshipFormatter());
    }

    private FamilyGraph Graph() => new(_p.Values, _pc, _sp);

    private void Person(string name, Gender g) =>
        _p[name] = new Person { LastName = name, FirstName = name, Gender = g };

    private void Children(string father, string mother, params string[] kids)
    {
        foreach (var kid in kids)
        {
            Parent(father, kid);
            Parent(mother, kid);
        }
    }

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

    private KinshipKind Kind(string root, string relative) =>
        _uk.Compute(_p[root], _p[relative], Graph(), includeAffinity: true).Kind;

    // ---- Нерідні батьки --------------------------------------------------

    [Fact]
    public void Fathers_new_wife_is_machukha() => Uk("Дитина", "Мачуха").ShouldBe("мачуха");

    [Fact]
    public void Mothers_new_husband_is_vitchym() => Uk("Дитина", "Вітчим").ShouldBe("вітчим");

    [Fact]
    public void Step_parent_is_affinity_not_none() =>
        Kind("Дитина", "Мачуха").ShouldBe(KinshipKind.Affinity);

    // ---- Нерідні діти ----------------------------------------------------

    [Fact]
    public void Wifes_daughter_is_paserbytsia() => Uk("Батько", "Пасербиця").ShouldBe("пасербиця");

    [Fact]
    public void Step_child_of_unknown_gender_shows_both_variants() =>
        Uk("Батько", "НевідомоХто").ShouldBe("пасинок / пасербиця");

    // ---- Обидва напрямки узгоджені ---------------------------------------

    [Fact]
    public void Step_relation_is_symmetric()
    {
        // Раніше «зв'язок не встановлено» з обох боків: бракувало і (1,0), і (0,1).
        Uk("Дитина", "Мачуха").ShouldBe("мачуха");
        Uk("Мачуха", "Дитина").ShouldBe("пасинок");
    }

    // ---- Кровна спорідненість має пріоритет ------------------------------

    [Fact]
    public void Own_child_is_still_blood_relation() =>
        Uk("Мачуха", "Пасербиця").ShouldBe("дочка");

    [Fact]
    public void Own_parent_is_still_blood_relation() =>
        Uk("Дитина", "Батько").ShouldBe("батько");

    // ---- Прапорець вимкнено → свояцтво не шукається -----------------------

    [Fact]
    public void Without_flag_step_relation_is_not_found() =>
        _uk.Compute(_p["Дитина"], _p["Мачуха"], Graph()).Kind.ShouldBe(KinshipKind.None);

    // ---- Англійські назви -------------------------------------------------

    [Fact]
    public void English_stepmother() => En("Дитина", "Мачуха").ShouldBe("stepmother");

    [Fact]
    public void English_stepfather() => En("Дитина", "Вітчим").ShouldBe("stepfather");

    [Fact]
    public void English_stepdaughter() => En("Батько", "Пасербиця").ShouldBe("stepdaughter");

    [Fact]
    public void English_stepson() => En("Мачуха", "Дитина").ShouldBe("stepson");

    [Fact]
    public void English_step_child_of_unknown_gender_shows_both_variants() =>
        En("Батько", "НевідомоХто").ShouldBe("stepson / stepdaughter");

    // ---- Невідома стать у подружжі та свояцтві -----------------------------

    [Fact]
    public void Spouse_of_unknown_gender_shows_both_variants()
    {
        // Раніше KinshipKind.Spouse кликав Pick напряму, минаючи ByGender,
        // тож особа з Gender.Unknown тихо ставала «чоловіком».
        Person("Партнер", Gender.Unknown);
        Marry("Дитина", "Партнер");

        Uk("Дитина", "Партнер").ShouldBe("чоловік / дружина");
        En("Дитина", "Партнер").ShouldBe("husband / wife");
    }

    [Fact]
    public void Spouse_parent_with_unknown_pivot_gender_is_descriptive()
    {
        // Стать подружжя невідома → «тесть» чи «свекор» вибрати неможливо.
        // Раніше код тихо падав у «свекор» (ніби подружжя — чоловік), а сусідній
        // SpouseSibling — у «шурин» (ніби подружжя — жінка). Тепер описова назва.
        Person("Партнер", Gender.Unknown);
        Person("БатькоПартнера", Gender.Male);
        Marry("Дитина", "Партнер");
        Parent("БатькоПартнера", "Партнер");

        Uk("Дитина", "БатькоПартнера").ShouldBe("батько подружжя");
    }

    // ---- Термінологія: «зведений» ≠ «неповнорідний» ------------------------

    [Fact]
    public void Half_sibling_via_unknown_gender_parent_is_nepovnorodnyi()
    {
        // Українською «зведений» — це дитина мачухи/вітчима, тобто БЕЗ спільної крові.
        // Для спільного одного з батьків правильний термін — «неповнорідний».
        // Раніше SiblingKind.HalfUnknown давав саме «зведений брат».
        Person("Спільний", Gender.Unknown);
        Person("СинА", Gender.Male);
        Person("СинБ", Gender.Male);
        Parent("Спільний", "СинА");
        Parent("Спільний", "СинБ");

        Uk("СинА", "СинБ").ShouldBe("неповнорідний брат");
    }

    // ---- Відома межа -------------------------------------------------------

    [Fact]
    public void Known_limitation_step_siblings_are_not_recognized_yet()
    {
        // Зведені сиблінги (діти мачухи/вітчима) — це шлях у ТРИ ребра:
        // Дитина —(кров)— Батько —(шлюб)— Мачуха —(кров)— Пасербиця.
        // Патерни A і B покривають лише два ребра, тому такий зв'язок поки не
        // визначається. Тест фіксує поточну поведінку, щоб зміна була помітною.
        Kind("Дитина", "Пасербиця").ShouldBe(KinshipKind.None);
    }
}
