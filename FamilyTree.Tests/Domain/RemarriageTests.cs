using FamilyTree.Domain;
using FamilyTree.Domain.Validation;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Повторний шлюб тієї самої пари (B-16): одружились → розлучились → одружились знову.
/// Це кілька <see cref="SpouseLink"/> на одну пару з роздільними періодами.
/// </summary>
public class RemarriageTests
{
    private static Person Make(string name, Gender g) => new() { LastName = name, FirstName = name, Gender = g };

    // --- FamilyGraph: статус не залежить від порядку зв'язків --------------

    [Fact]
    public void Pair_is_active_when_any_marriage_is_active_regardless_of_order()
    {
        var a = Make("Ч", Gender.Male);
        var b = Make("Ж", Gender.Female);

        var divorced = SpouseLink.Create(a.Id, b.Id, new DateOnly(1990, 1, 1), new DateOnly(1995, 1, 1));
        var active = SpouseLink.Create(a.Id, b.Id, new DateOnly(2000, 1, 1));

        // Активний зв'язок першим, розірваний другим — раніше «останній перемагав» → false.
        var graph1 = new FamilyGraph(new[] { a, b }, Array.Empty<ParentChildLink>(), new[] { active, divorced });
        graph1.IsSpouseActive(a.Id, b.Id).ShouldBeTrue();

        // Зворотний порядок зв'язків — результат має бути той самий.
        var graph2 = new FamilyGraph(new[] { a, b }, Array.Empty<ParentChildLink>(), new[] { divorced, active });
        graph2.IsSpouseActive(a.Id, b.Id).ShouldBeTrue();
    }

    [Fact]
    public void Pair_is_inactive_when_all_marriages_are_divorced()
    {
        var a = Make("Ч", Gender.Male);
        var b = Make("Ж", Gender.Female);

        var first = SpouseLink.Create(a.Id, b.Id, new DateOnly(1990, 1, 1), new DateOnly(1995, 1, 1));
        var second = SpouseLink.Create(a.Id, b.Id, new DateOnly(2000, 1, 1), new DateOnly(2005, 1, 1));

        var graph = new FamilyGraph(new[] { a, b }, Array.Empty<ParentChildLink>(), new[] { first, second });

        graph.IsSpouseActive(a.Id, b.Id).ShouldBeFalse();
    }

    // --- Валідатор: дубль лише за перетином періодів ----------------------

    [Fact]
    public void Remarriage_with_separate_periods_is_allowed()
    {
        var a = Make("A", Gender.Male);
        var b = Make("B", Gender.Female);
        var existing = new[] { SpouseLink.Create(a.Id, b.Id, new DateOnly(1990, 1, 1), new DateOnly(1995, 1, 1)) };
        var candidate = SpouseLink.Create(a.Id, b.Id, new DateOnly(2000, 1, 1));

        var result = new RelationshipValidator().ValidateSpouse(candidate, existing);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Overlapping_marriage_periods_are_still_a_duplicate()
    {
        var a = Make("A", Gender.Male);
        var b = Make("B", Gender.Female);
        // Перший шлюб іще чинний (без дати розлучення); другий починається всередині нього.
        var existing = new[] { SpouseLink.Create(a.Id, b.Id, new DateOnly(1990, 1, 1)) };
        var candidate = SpouseLink.Create(a.Id, b.Id, new DateOnly(1992, 1, 1));

        var result = new RelationshipValidator().ValidateSpouse(candidate, existing);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(m => m.Key == ValidationKeys.DuplicateSpouse);
    }

    /// <summary>
    /// B-68: найчастіший реальний випадок — перший шлюб завершено галочкою «В шлюбі», а дати
    /// розлучення ніхто не знає. До фікса такий шлюб тягнувся у +∞ і блокував будь-який наступний.
    /// </summary>
    [Fact]
    public void Remarriage_is_allowed_when_previous_marriage_ended_without_a_divorce_date()
    {
        var a = Make("A", Gender.Male);
        var b = Make("B", Gender.Female);
        var ended = SpouseLink.Create(a.Id, b.Id, new DateOnly(1990, 1, 1), divorceDate: null, divorced: true);
        var candidate = SpouseLink.Create(a.Id, b.Id, new DateOnly(2000, 1, 1));

        var result = new RelationshipValidator().ValidateSpouse(candidate, new[] { ended });

        result.IsValid.ShouldBeTrue();
    }

    /// <summary>
    /// B-68: дат немає взагалі, а попередній шлюб завершено — перетин «доводиться» лише невідомими
    /// межами. Це попередження (користувач підтверджує), а не жорстка помилка.
    /// </summary>
    [Fact]
    public void Remarriage_without_any_dates_is_a_warning_not_an_error()
    {
        var a = Make("A", Gender.Male);
        var b = Make("B", Gender.Female);
        var ended = SpouseLink.Create(a.Id, b.Id, new DateOnly(1990, 1, 1), new DateOnly(1995, 1, 1));
        var candidate = SpouseLink.Create(a.Id, b.Id);

        var result = new RelationshipValidator().ValidateSpouse(candidate, new[] { ended });

        result.IsValid.ShouldBeTrue();
        result.Warnings.ShouldContain(m => m.Key == ValidationKeys.DuplicateSpouseUnknownDates);
    }

    /// <summary>
    /// Зворотний бік попереднього: поки наявний шлюб ЧИННИЙ, другий без дат лишається помилкою —
    /// двох одночасних шлюбів однієї пари не буває.
    /// </summary>
    [Fact]
    public void Second_marriage_without_dates_is_an_error_while_the_first_is_active()
    {
        var a = Make("A", Gender.Male);
        var b = Make("B", Gender.Female);
        var active = SpouseLink.Create(a.Id, b.Id, new DateOnly(1990, 1, 1));
        var candidate = SpouseLink.Create(a.Id, b.Id);

        var result = new RelationshipValidator().ValidateSpouse(candidate, new[] { active });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(m => m.Key == ValidationKeys.DuplicateSpouse);
    }

    // --- PeriodOverlaps: межі та відкриті інтервали -----------------------

    [Fact]
    public void PeriodOverlaps_closes_the_period_of_a_marriage_ended_without_a_date()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // Завершений без дати розлучення: період стискається до самої дати шлюбу.
        var ended = SpouseLink.Create(a, b, new DateOnly(1990, 1, 1), divorceDate: null, divorced: true);
        var later = SpouseLink.Create(a, b, new DateOnly(1995, 1, 1));
        var covering = SpouseLink.Create(a, b, new DateOnly(1985, 1, 1), new DateOnly(1992, 1, 1));

        ended.PeriodOverlaps(later).ShouldBeFalse();
        later.PeriodOverlaps(ended).ShouldBeFalse();   // симетрично
        ended.PeriodOverlaps(covering).ShouldBeTrue(); // 1990 всередині [1985, 1992]
    }

    [Fact]
    public void PeriodOverlaps_treats_missing_bounds_as_open()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var openEnded = SpouseLink.Create(a, b, new DateOnly(1990, 1, 1));                 // [1990, +∞)
        var earlier = SpouseLink.Create(a, b, new DateOnly(1970, 1, 1), new DateOnly(1980, 1, 1)); // [1970, 1980]
        var later = SpouseLink.Create(a, b, new DateOnly(1995, 1, 1), new DateOnly(2000, 1, 1));    // [1995, 2000]

        openEnded.PeriodOverlaps(earlier).ShouldBeFalse();
        openEnded.PeriodOverlaps(later).ShouldBeTrue();
    }
}
