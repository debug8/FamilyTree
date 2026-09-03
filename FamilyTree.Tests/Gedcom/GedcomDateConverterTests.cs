using FamilyTree.Gedcom;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Gedcom;

/// <summary>Розбір і серіалізація DATE_VALUE (T-5.2, Частина 1).</summary>
public sealed class GedcomDateConverterTests
{
    // ------------------------------------------------------------ Exact / partial

    [Fact]
    public void Parses_full_exact_date()
    {
        var d = GedcomDateConverter.Parse("12 MAR 1950");
        var single = d.ShouldBeOfType<GedcomDate.Single>();
        single.Qualifier.ShouldBe(DateQualifier.Exact);
        single.Date.Day.ShouldBe(12);
        single.Date.Month.ShouldBe(3);
        single.Date.Year.ShouldBe(1950);
    }

    [Fact]
    public void Parses_month_and_year_only()
    {
        var single = GedcomDateConverter.Parse("MAR 1950").ShouldBeOfType<GedcomDate.Single>();
        single.Date.Day.ShouldBeNull();
        single.Date.Month.ShouldBe(3);
        single.Date.Year.ShouldBe(1950);
    }

    [Fact]
    public void Parses_year_only()
    {
        var single = GedcomDateConverter.Parse("1950").ShouldBeOfType<GedcomDate.Single>();
        single.Date.Day.ShouldBeNull();
        single.Date.Month.ShouldBeNull();
        single.Date.Year.ShouldBe(1950);
    }

    // ------------------------------------------------------------ Approximated

    [Theory]
    [InlineData("ABT 1950", DateQualifier.About)]
    [InlineData("CAL 1950", DateQualifier.Calculated)]
    [InlineData("EST 1950", DateQualifier.Estimated)]
    public void Parses_approximated_qualifiers(string raw, DateQualifier expected)
    {
        var single = GedcomDateConverter.Parse(raw).ShouldBeOfType<GedcomDate.Single>();
        single.Qualifier.ShouldBe(expected);
        single.Date.Year.ShouldBe(1950);
    }

    // ------------------------------------------------------------ Range

    [Fact]
    public void Parses_before()
    {
        var b = GedcomDateConverter.Parse("BEF 1 JAN 1900").ShouldBeOfType<GedcomDate.Before>();
        b.Date.Day.ShouldBe(1);
        b.Date.Month.ShouldBe(1);
        b.Date.Year.ShouldBe(1900);
    }

    [Fact]
    public void Parses_after()
    {
        var a = GedcomDateConverter.Parse("AFT 1918").ShouldBeOfType<GedcomDate.After>();
        a.Date.Year.ShouldBe(1918);
    }

    [Fact]
    public void Parses_between()
    {
        var bt = GedcomDateConverter.Parse("BET 1914 AND 1918").ShouldBeOfType<GedcomDate.Between>();
        bt.From.Year.ShouldBe(1914);
        bt.To.Year.ShouldBe(1918);
    }

    // ------------------------------------------------------------ Period

    [Fact]
    public void Parses_from_to_period()
    {
        var p = GedcomDateConverter.Parse("FROM 1980 TO 1990").ShouldBeOfType<GedcomDate.Period>();
        p.From!.Year.ShouldBe(1980);
        p.To!.Year.ShouldBe(1990);
    }

    [Fact]
    public void Parses_open_ended_periods()
    {
        var from = GedcomDateConverter.Parse("FROM 1980").ShouldBeOfType<GedcomDate.Period>();
        from.From!.Year.ShouldBe(1980);
        from.To.ShouldBeNull();

        var to = GedcomDateConverter.Parse("TO 1990").ShouldBeOfType<GedcomDate.Period>();
        to.From.ShouldBeNull();
        to.To!.Year.ShouldBe(1990);
    }

    // ------------------------------------------------------------ Interpreted / phrase

    [Fact]
    public void Parses_interpreted_date()
    {
        var i = GedcomDateConverter.Parse("INT 1950 (близько середини століття)")
                                   .ShouldBeOfType<GedcomDate.Interpreted>();
        i.Date.Year.ShouldBe(1950);
        i.OriginalText.ShouldBe("близько середини століття");
    }

    [Fact]
    public void Parses_bare_phrase()
    {
        var ph = GedcomDateConverter.Parse("(перед війною)").ShouldBeOfType<GedcomDate.Phrase>();
        ph.Text.ShouldBe("перед війною");
    }

    // ------------------------------------------------------------ Calendars / era / dual year

    [Fact]
    public void Parses_julian_calendar_escape()
    {
        var single = GedcomDateConverter.Parse("@#DJULIAN@ 14 SEP 1752").ShouldBeOfType<GedcomDate.Single>();
        single.Date.Calendar.ShouldBe(GedcomCalendar.Julian);
        single.Date.Day.ShouldBe(14);
        single.Date.Month.ShouldBe(9);
        single.Date.Year.ShouldBe(1752);
    }

    [Fact]
    public void Parses_bce_both_spellings()
    {
        GedcomDateConverter.Parse("44 B.C.").ShouldBeOfType<GedcomDate.Single>().Date.IsBce.ShouldBeTrue();
        GedcomDateConverter.Parse("44 BCE").ShouldBeOfType<GedcomDate.Single>().Date.IsBce.ShouldBeTrue();
    }

    [Fact]
    public void Parses_dual_year()
    {
        var single = GedcomDateConverter.Parse("1750/51").ShouldBeOfType<GedcomDate.Single>();
        single.Date.Year.ShouldBe(1750);
        single.Date.DualYear.ShouldBe(1751);
    }

    // ------------------------------------------------------------ Format

    [Fact]
    public void Formats_exact_date()
    {
        var d = new GedcomDate.Single(new PartialDate(1950, 3, 12));
        GedcomDateConverter.Format(d).ShouldBe("12 MAR 1950");
    }

    [Fact]
    public void Formats_bce_per_version()
    {
        var d = new GedcomDate.Single(new PartialDate(44, IsBce: true));
        GedcomDateConverter.Format(d, GedcomVersion.V551).ShouldBe("44 B.C.");
        GedcomDateConverter.Format(d, GedcomVersion.V70).ShouldBe("44 BCE");
    }

    [Fact]
    public void Formats_dual_year()
    {
        var d = new GedcomDate.Single(new PartialDate(1750, DualYear: 1751));
        GedcomDateConverter.Format(d).ShouldBe("1750/51");
    }

    // ------------------------------------------------------------ Round-trip

    [Theory]
    [InlineData("12 MAR 1950")]
    [InlineData("MAR 1950")]
    [InlineData("1950")]
    [InlineData("ABT 1950")]
    [InlineData("CAL 1950")]
    [InlineData("EST 1950")]
    [InlineData("BEF 1 JAN 1900")]
    [InlineData("AFT 1918")]
    [InlineData("BET 1914 AND 1918")]
    [InlineData("FROM 1980 TO 1990")]
    [InlineData("FROM 1980")]
    [InlineData("TO 1990")]
    [InlineData("INT 1950 (guess)")]
    [InlineData("(unparseable)")]
    [InlineData("@#DJULIAN@ 14 SEP 1752")]
    [InlineData("1750/51")]
    public void Round_trip_preserves_value(string raw)
    {
        var parsed = GedcomDateConverter.Parse(raw);
        var formatted = GedcomDateConverter.Format(parsed, GedcomVersion.V70);
        // Нормалізуємо BCE-написання для стабільного порівняння
        GedcomDateConverter.Parse(formatted).ShouldBe(parsed);
    }

    // ------------------------------------------------------------ Sorting

    [Fact]
    public void SortKey_orders_chronologically()
    {
        var dates = new[]
        {
            GedcomDateConverter.Parse("1990"),
            GedcomDateConverter.Parse("BET 1914 AND 1918"),
            GedcomDateConverter.Parse("12 MAR 1950"),
        };

        var sorted = dates.OrderBy(d => d.SortKey()).ToArray();
        sorted[0].SortKey()!.Value.Year.ShouldBe(1914);
        sorted[1].SortKey()!.Value.Year.ShouldBe(1950);
        sorted[2].SortKey()!.Value.Year.ShouldBe(1990);
    }

    [Fact]
    public void TryParse_returns_false_on_garbage()
    {
        GedcomDateConverter.TryParse("BET only", out var result).ShouldBeFalse();
        result.ShouldBeNull();
    }
}
