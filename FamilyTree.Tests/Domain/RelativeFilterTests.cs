using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Режим дерева «Лише родичі»: до нього входять ті, для кого ядро спорідненості дає назву
/// зв'язку, плюс подружжя за налаштуваннями фільтра. Головне, що тут перевіряється, —
/// НЕ входять сторонні, до яких режим «Усі» дотягується через ланцюжок шлюбів.
/// </summary>
public class RelativeFilterTests
{
    private readonly Dictionary<string, Person> _p = new();
    private readonly List<ParentChildLink> _pc = new();
    private readonly List<SpouseLink> _sp = new();
    private readonly RelativeFilter _filter;

    public RelativeFilterTests()
    {
        // Рід кореня
        Person("Іван", Gender.Male);      // корінь
        Person("Степан", Gender.Male);    // батько Івана
        Person("Галина", Gender.Female);  // мати Івана
        Person("Ніна", Gender.Female);    // сестра Івана
        Person("Тарас", Gender.Male);     // чоловік Ніни (свояк)

        // Рід дружини
        Person("Ольга", Gender.Female);   // дружина Івана
        Person("Петро", Gender.Male);     // батько Ольги — тесть
        Person("Богдан", Gender.Male);    // брат Ольги — шурин
        Person("Зоряна", Gender.Female);  // дружина Богдана

        // Стороння гілка: батьки Зоряни. Для Івана вони вже ніхто,
        // але режим «Усі» дотягується до них ланцюжком шлюбів.
        Person("Остап", Gender.Male);
        Person("Леся", Gender.Female);

        // Розлучена пара: колишній чоловік сестри кореня.
        Person("Юрій", Gender.Male);

        // Колишня дружина САМОГО кореня — окремий випадок, її з дерева не викидаємо.
        Person("Оксана", Gender.Female);

        Children("Степан", "Галина", "Іван", "Ніна");
        Children("Петро", "Петро", "Ольга", "Богдан");   // мати не задана — не потрібна
        Children("Остап", "Леся", "Зоряна");

        Marry("Іван", "Ольга");
        Marry("Ніна", "Тарас");
        Marry("Богдан", "Зоряна");
        Divorced("Ніна", "Юрій");
        Divorced("Іван", "Оксана");

        _filter = new RelativeFilter(
            new KinshipCalculator(new CommonAncestorFinder(), new UkrainianKinshipFormatter()));
    }

    private FamilyGraph Graph() => new(_p.Values, _pc, _sp);

    private void Person(string name, Gender g) =>
        _p[name] = new Person { LastName = name, FirstName = name, Gender = g };

    private void Children(string father, string mother, params string[] kids)
    {
        foreach (var kid in kids)
        {
            _pc.Add(new ParentChildLink { ParentId = _p[father].Id, ChildId = _p[kid].Id });
            if (mother != father)
            {
                _pc.Add(new ParentChildLink { ParentId = _p[mother].Id, ChildId = _p[kid].Id });
            }
        }
    }

    private void Marry(string a, string b) =>
        _sp.Add(SpouseLink.Create(_p[a].Id, _p[b].Id, new DateOnly(2000, 1, 1)));

    private void Divorced(string a, string b) =>
        _sp.Add(SpouseLink.Create(_p[a].Id, _p[b].Id, new DateOnly(1990, 1, 1), new DateOnly(1995, 1, 1)));

    private HashSet<Guid> Select(RelativeFilterOptions? options = null) =>
        _filter.Select(Graph(), _p["Іван"].Id, options);

    private bool Has(HashSet<Guid> ids, string name) => ids.Contains(_p[name].Id);

    // ---- Типовий склад ----------------------------------------------------

    [Fact]
    public void Root_and_blood_relatives_are_included()
    {
        var ids = Select();

        Has(ids, "Іван").ShouldBeTrue();
        Has(ids, "Степан").ShouldBeTrue();
        Has(ids, "Галина").ShouldBeTrue();
        Has(ids, "Ніна").ShouldBeTrue();
    }

    [Fact]
    public void Affinity_is_included_by_default()
    {
        var ids = Select();

        Has(ids, "Ольга").ShouldBeTrue();   // дружина
        Has(ids, "Петро").ShouldBeTrue();   // тесть
        Has(ids, "Богдан").ShouldBeTrue();  // шурин
        Has(ids, "Тарас").ShouldBeTrue();   // чоловік сестри
    }

    [Fact]
    public void Spouse_of_a_relative_is_included_even_without_a_name()
    {
        // Зоряна — дружина шурина. Назви для неї ядро не дає, але як чинне
        // подружжя відібраної особи вона в дерево входить.
        Has(Select(), "Зоряна").ShouldBeTrue();
    }

    [Fact]
    public void Strangers_reachable_only_through_marriages_are_excluded()
    {
        // Саме через цей ланцюжок режим «Усі» показував півфайлу:
        // Іван → Ольга → Богдан → Зоряна → її батьки.
        var ids = Select();

        Has(ids, "Остап").ShouldBeFalse();
        Has(ids, "Леся").ShouldBeFalse();
    }

    [Fact]
    public void Former_spouse_of_a_relative_is_excluded_by_default()
    {
        // Ядро дає йому назву («колишній чоловік сестри»), тож самої перевірки «чи є назва»
        // не досить — фільтр окремо відсіює тих, хто тримається лише на розірваному шлюбі.
        Has(Select(), "Юрій").ShouldBeFalse();
    }

    [Fact]
    public void Former_spouse_of_the_root_is_kept()
    {
        // Виняток із попереднього правила: колишнє подружжя самої кореневої особи
        // лишається — це, як правило, другий батько спільних дітей.
        Has(Select(), "Оксана").ShouldBeTrue();
    }

    // ---- Налаштування складу (майбутній екран налаштувань) -----------------

    [Fact]
    public void Former_spouses_can_be_turned_on()
    {
        var ids = Select(RelativeFilterOptions.Default with { IncludeFormerSpouses = true });

        Has(ids, "Юрій").ShouldBeTrue();
    }

    [Fact]
    public void Without_affinity_in_laws_drop_out()
    {
        var ids = Select(new RelativeFilterOptions(IncludeAffinity: false));

        Has(ids, "Ніна").ShouldBeTrue();     // сестра — кровна
        Has(ids, "Ольга").ShouldBeTrue();    // власне подружжя кореня — це не свояцтво
        Has(ids, "Петро").ShouldBeFalse();   // тесть — свояцтво, і ні з ким із набору не в шлюбі

        // Тарас лишається: назви для нього без свояцтва немає, але він чинне подружжя
        // Ніни, яка в наборі. Вимкнення свояцтва саме по собі його не викидає.
        Has(ids, "Тарас").ShouldBeTrue();
    }

    [Fact]
    public void Without_affinity_and_spouses_only_blood_and_own_spouse_remain()
    {
        var ids = Select(new RelativeFilterOptions(IncludeAffinity: false, IncludeCurrentSpouses: false));

        Has(ids, "Ніна").ShouldBeTrue();
        Has(ids, "Ольга").ShouldBeTrue();    // дружина кореня має власну назву, не через прохід подружжям
        Has(ids, "Тарас").ShouldBeFalse();
        Has(ids, "Зоряна").ShouldBeFalse();
    }

    [Fact]
    public void Unknown_root_gives_an_empty_set()
    {
        _filter.Select(Graph(), Guid.NewGuid()).ShouldBeEmpty();
    }
}
