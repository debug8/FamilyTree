using FamilyTree.Domain;
using FamilyTree.Domain.Kinship;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// T-3.4 — свояцтво (зв'язки через шлюб, розд. 4.5). Спільна родина двох родів,
/// об'єднаних шлюбом Івана та Ольги, покриває тесть/теща/свекор/свекруха,
/// зять/невістка, дівер/зовиця, шурин/своячка та описове «дружина дядька».
/// </summary>
public class AffinityTests
{
    private readonly Dictionary<string, Person> _p = new();
    private readonly List<ParentChildLink> _pc = new();
    private readonly List<SpouseLink> _sp = new();
    private readonly KinshipCalculator _uk;
    private readonly KinshipCalculator _en;

    public AffinityTests()
    {
        // Рід Ольги
        Person("Петро", Gender.Male);   // батько Ольги
        Person("Марія", Gender.Female); // мати Ольги
        Person("Ольга", Gender.Female);
        Person("Богдан", Gender.Male);   // брат Ольги
        Person("Зоряна", Gender.Female); // дружина Богдана
        Person("Віра", Gender.Female);   // сестра Ольги

        // Рід Івана
        Person("Степан", Gender.Male);   // батько Івана
        Person("Галина", Gender.Female); // мати Івана
        Person("Іван", Gender.Male);
        Person("Роман", Gender.Male);    // брат Івана
        Person("Надія", Gender.Female);  // дружина Романа
        Person("Ніна", Gender.Female);   // сестра Івана
        Person("Тарас", Gender.Male);    // чоловік Ніни

        // Дитина подружжя Івана та Ольги
        Person("Юрко", Gender.Male);

        Children("Петро", "Марія", "Ольга", "Богдан", "Віра");
        Children("Степан", "Галина", "Іван", "Роман", "Ніна");
        Children2("Іван", "Ольга", "Юрко");

        Marry("Іван", "Ольга");
        Marry("Богдан", "Зоряна");
        Marry("Роман", "Надія");
        Marry("Ніна", "Тарас");

        var finder = new CommonAncestorFinder();
        _uk = new KinshipCalculator(finder, new UkrainianKinshipFormatter());
        _en = new KinshipCalculator(finder, new EnglishKinshipFormatter());
    }

    private FamilyGraph Graph() => new(_p.Values, _pc, _sp);

    private void Person(string name, Gender g)
    {
        if (name.EndsWith("_відсутній", StringComparison.Ordinal))
        {
            return;
        }

        _p[name] = new Person { LastName = name, FirstName = name, Gender = g };
    }

    private void Children(string father, string mother, params string[] kids)
    {
        foreach (var kid in kids)
        {
            _pc.Add(new ParentChildLink { ParentId = _p[father].Id, ChildId = _p[kid].Id });
            _pc.Add(new ParentChildLink { ParentId = _p[mother].Id, ChildId = _p[kid].Id });
        }
    }

    private void Children2(string father, string mother, string kid) => Children(father, mother, kid);

    private void Marry(string a, string b)
    {
        if (b.EndsWith("_відсутній", StringComparison.Ordinal))
        {
            return;
        }

        _sp.Add(SpouseLink.Create(_p[a].Id, _p[b].Id, new DateOnly(2000, 1, 1)));
    }

    private string Uk(string root, string relative) =>
        _uk.Compute(_p[root], _p[relative], Graph(), includeAffinity: true).DisplayName;

    private string En(string root, string relative) =>
        _en.Compute(_p[root], _p[relative], Graph(), includeAffinity: true).DisplayName;

    // Батьки подружжя ------------------------------------------------------

    [Fact]
    public void Wifes_father_is_tesc() => Uk("Іван", "Петро").ShouldBe("тесть");

    [Fact]
    public void Wifes_mother_is_tesca() => Uk("Іван", "Марія").ShouldBe("теща");

    [Fact]
    public void Husbands_father_is_svekor() => Uk("Ольга", "Степан").ShouldBe("свекор");

    [Fact]
    public void Husbands_mother_is_svekrukha() => Uk("Ольга", "Галина").ShouldBe("свекруха");

    // Подружжя дітей / сиблінгів -------------------------------------------

    [Fact]
    public void Daughters_husband_is_ziat() => Uk("Петро", "Іван").ShouldBe("зять");

    [Fact]
    public void Sons_wife_is_nevistka() => Uk("Петро", "Зоряна").ShouldBe("невістка");

    [Fact]
    public void Sisters_husband_is_ziat() => Uk("Іван", "Тарас").ShouldBe("зять");

    [Fact]
    public void Brothers_wife_is_nevistka() => Uk("Іван", "Надія").ShouldBe("невістка");

    // Брати/сестри подружжя ------------------------------------------------

    [Fact]
    public void Husbands_brother_is_diver() => Uk("Ольга", "Роман").ShouldBe("дівер");

    [Fact]
    public void Husbands_sister_is_zovytsia() => Uk("Ольга", "Ніна").ShouldBe("зовиця");

    [Fact]
    public void Wifes_brother_is_shuryn() => Uk("Іван", "Богдан").ShouldBe("шурин");

    [Fact]
    public void Wifes_sister_is_svoiachka() => Uk("Іван", "Віра").ShouldBe("своячка");

    // Описове свояцтво -----------------------------------------------------

    [Fact]
    public void Uncles_wife_is_descriptive() => Uk("Юрко", "Надія").ShouldBe("дружина дядька");

    // Прапорець вимкнено → свояцтво не шукається ----------------------------

    [Fact]
    public void Without_flag_affinity_is_not_found()
    {
        _uk.Compute(_p["Іван"], _p["Петро"], Graph()).Kind.ShouldBe(KinshipKind.None);
    }

    // Англійські «-in-law» --------------------------------------------------

    [Fact]
    public void English_father_in_law() => En("Іван", "Петро").ShouldBe("father-in-law");

    [Fact]
    public void English_son_in_law() => En("Петро", "Іван").ShouldBe("son-in-law");

    [Fact]
    public void English_brother_in_law() => En("Ольга", "Роман").ShouldBe("brother-in-law");

    [Fact]
    public void English_sister_in_law() => En("Іван", "Віра").ShouldBe("sister-in-law");
}
