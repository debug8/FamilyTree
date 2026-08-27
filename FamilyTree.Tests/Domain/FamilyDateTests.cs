using FamilyTree.Domain;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>Доменна модель неточних дат (T-5.7, Частина 1a).</summary>
public sealed class FamilyDateTests
{
    // ---- DatePoint -------------------------------------------------------

    [Fact]
    public void DatePoint_precision_follows_present_parts()
    {
        new DatePoint { Year = 1850 }.Precision.ShouldBe(DatePrecision.Year);
        new DatePoint { Year = 1850, Month = 3 }.Precision.ShouldBe(DatePrecision.Month);
        new DatePoint { Year = 1850, Month = 3, Day = 14 }.Precision.ShouldBe(DatePrecision.Day);
    }

    [Fact]
    public void DatePoint_from_dateonly_round_trips()
    {
        var d = new DateOnly(1980, 3, 14);
        var point = DatePoint.FromDateOnly(d);

        point.Calendar.ShouldBe(DateCalendar.Gregorian);
        point.ToDateOnly().ShouldBe(d);
    }

    [Fact]
    public void DatePoint_partial_year_comparable_fills_january_first()
    {
        new DatePoint { Year = 1850 }.ToDateOnly().ShouldBe(new DateOnly(1850, 1, 1));
    }

    [Fact]
    public void DatePoint_invalid_month_is_not_comparable()
    {
        new DatePoint { Year = 1850, Month = 13 }.ToDateOnly().ShouldBeNull();
        new DatePoint { Year = 1850, Month = 13 }.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void DatePoint_day_without_month_is_invalid()
    {
        new DatePoint { Year = 1850, Day = 5 }.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void DatePoint_keeps_julian_calendar()
    {
        var point = new DatePoint { Year = 1700, Month = 2, Day = 10, Calendar = DateCalendar.Julian };
        point.Calendar.ShouldBe(DateCalendar.Julian);
        point.IsValid.ShouldBeTrue();
    }

    // ---- FamilyDate: форми ----------------------------------------------

    [Fact]
    public void Exact_from_dateonly()
    {
        var date = FamilyDate.Exact(new DateOnly(1980, 3, 14));

        date.Kind.ShouldBe(FamilyDateKind.Exact);
        date.Start!.Precision.ShouldBe(DatePrecision.Day);
        date.EffectiveYear.ShouldBe(1980);
        date.ToComparable().ShouldBe(new DateOnly(1980, 3, 14));
        date.IsStructurallyValid().ShouldBeTrue();
    }

    [Fact]
    public void Partial_year_is_exact_kind_with_year_precision()
    {
        var date = FamilyDate.Exact(new DatePoint { Year = 1850 });

        date.Kind.ShouldBe(FamilyDateKind.Exact);
        date.Start!.Precision.ShouldBe(DatePrecision.Year);
        date.EffectiveYear.ShouldBe(1850);
        date.ToComparable().ShouldBe(new DateOnly(1850, 1, 1));
    }

    [Fact]
    public void Approximate_carries_qualifier()
    {
        var date = FamilyDate.Approximate(DateApproximation.About, new DatePoint { Year = 1850 });

        date.Kind.ShouldBe(FamilyDateKind.Approximate);
        date.Approximation.ShouldBe(DateApproximation.About);
        date.EffectiveYear.ShouldBe(1850);
        date.IsStructurallyValid().ShouldBeTrue();
    }

    [Fact]
    public void Before_and_after_have_single_bound()
    {
        var before = FamilyDate.Before(new DatePoint { Year = 1900 });
        before.Kind.ShouldBe(FamilyDateKind.Range);
        before.RangeKind.ShouldBe(DateRangeKind.Before);
        before.Start!.Year.ShouldBe(1900);
        before.End.ShouldBeNull();
        before.EffectiveYear.ShouldBe(1900);
        before.IsStructurallyValid().ShouldBeTrue();

        FamilyDate.After(new DatePoint { Year = 1800 }).RangeKind.ShouldBe(DateRangeKind.After);
    }

    [Fact]
    public void Between_uses_from_as_representative()
    {
        var date = FamilyDate.Between(new DatePoint { Year = 1980 }, new DatePoint { Year = 1985 });

        date.Kind.ShouldBe(FamilyDateKind.Range);
        date.RangeKind.ShouldBe(DateRangeKind.Between);
        date.Start!.Year.ShouldBe(1980);
        date.End!.Year.ShouldBe(1985);
        date.EffectiveYear.ShouldBe(1980);
        date.ToComparable().ShouldBe(new DateOnly(1980, 1, 1));
        date.IsStructurallyValid().ShouldBeTrue();
    }

    [Fact]
    public void Phrase_has_no_comparable_value()
    {
        var date = FamilyDate.FromPhrase("близько Різдва 1900");

        date.Kind.ShouldBe(FamilyDateKind.Phrase);
        date.Phrase.ShouldBe("близько Різдва 1900");
        date.EffectiveYear.ShouldBeNull();
        date.ToComparable().ShouldBeNull();
        date.IsStructurallyValid().ShouldBeTrue();
    }

    // ---- Структурна валідність / round-trip -----------------------------

    [Fact]
    public void Between_without_end_is_structurally_invalid()
    {
        var broken = new FamilyDate
        {
            Kind = FamilyDateKind.Range,
            RangeKind = DateRangeKind.Between,
            Start = new DatePoint { Year = 1980 },
        };

        broken.IsStructurallyValid().ShouldBeFalse();
    }

    [Fact]
    public void Empty_phrase_is_structurally_invalid()
    {
        new FamilyDate { Kind = FamilyDateKind.Phrase, Phrase = "  " }.IsStructurallyValid().ShouldBeFalse();
    }

    [Fact]
    public void Original_gedcom_is_carried_on_copy_without_changing_value()
    {
        var date = FamilyDate.Exact(new DatePoint { Year = 1850 });
        var tagged = date.WithOriginalGedcom("ABT 1850");

        tagged.OriginalGedcom.ShouldBe("ABT 1850");
        tagged.Start.ShouldBe(date.Start);
        tagged.Kind.ShouldBe(date.Kind);
    }

    // ---- Рівність за значенням ------------------------------------------

    [Fact]
    public void Value_equality_holds_for_equal_dates()
    {
        FamilyDate.Exact(new DateOnly(1980, 3, 14))
            .ShouldBe(FamilyDate.Exact(new DateOnly(1980, 3, 14)));

        FamilyDate.Exact(new DateOnly(1980, 3, 14))
            .ShouldNotBe(FamilyDate.Exact(new DateOnly(1980, 3, 15)));

        FamilyDate.Approximate(DateApproximation.About, new DatePoint { Year = 1850 })
            .ShouldNotBe(FamilyDate.Exact(new DatePoint { Year = 1850 }));
    }
}
