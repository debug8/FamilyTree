using FamilyTree.Domain;
using FamilyTree.Gedcom;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Gedcom;

/// <summary>Рядок DATE_VALUE → FamilyDate (T-5.2, Частина 3).</summary>
public sealed class GedcomDateMapperImportTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_value_maps_to_null(string? raw) =>
        GedcomDateMapper.FromGedcom(raw).ShouldBeNull();

    [Fact]
    public void Full_exact_date()
    {
        var date = GedcomDateMapper.FromGedcom("12 MAR 1950").ShouldNotBeNull();

        date.Kind.ShouldBe(FamilyDateKind.Exact);
        date.Start!.Year.ShouldBe(1950);
        date.Start.Month.ShouldBe(3);
        date.Start.Day.ShouldBe(12);
        date.OriginalGedcom.ShouldBeNull();
    }

    [Fact]
    public void Partial_dates_keep_their_precision()
    {
        GedcomDateMapper.FromGedcom("MAR 1850")!.Start!.Precision.ShouldBe(DatePrecision.Month);
        GedcomDateMapper.FromGedcom("1850")!.Start!.Precision.ShouldBe(DatePrecision.Year);
    }

    [Theory]
    [InlineData("ABT 1850", DateApproximation.About)]
    [InlineData("CAL 1850", DateApproximation.Calculated)]
    [InlineData("EST 1850", DateApproximation.Estimated)]
    public void Approximate_qualifiers(string raw, DateApproximation expected)
    {
        var date = GedcomDateMapper.FromGedcom(raw).ShouldNotBeNull();

        date.Kind.ShouldBe(FamilyDateKind.Approximate);
        date.Approximation.ShouldBe(expected);
    }

    [Theory]
    [InlineData("BEF 1900", DateRangeKind.Before)]
    [InlineData("AFT 1900", DateRangeKind.After)]
    public void Open_ranges(string raw, DateRangeKind expected)
    {
        var date = GedcomDateMapper.FromGedcom(raw).ShouldNotBeNull();

        date.Kind.ShouldBe(FamilyDateKind.Range);
        date.RangeKind.ShouldBe(expected);
        date.End.ShouldBeNull();
    }

    [Fact]
    public void Between_range()
    {
        var date = GedcomDateMapper.FromGedcom("BET 1980 AND 1985").ShouldNotBeNull();

        date.RangeKind.ShouldBe(DateRangeKind.Between);
        date.Start!.Year.ShouldBe(1980);
        date.End!.Year.ShouldBe(1985);
    }

    [Fact]
    public void Julian_calendar_is_preserved()
    {
        var date = GedcomDateMapper.FromGedcom("@#DJULIAN@ 29 FEB 1700").ShouldNotBeNull();

        date.Start!.Calendar.ShouldBe(DateCalendar.Julian);
        date.Start.Day.ShouldBe(29);
    }

    [Fact]
    public void Parenthesised_phrase_becomes_a_phrase_date()
    {
        var date = GedcomDateMapper.FromGedcom("(за переказами)").ShouldNotBeNull();

        date.Kind.ShouldBe(FamilyDateKind.Phrase);
        date.Phrase.ShouldBe("за переказами");
        date.OriginalGedcom.ShouldBeNull();
    }

    [Theory]
    [InlineData("FROM 1750 TO 1760")]   // період — поза межами моделі
    [InlineData("INT 1750 (десь тоді)")] // інтерпретована
    [InlineData("50 B.C.")]              // до н.е.
    [InlineData("1750/51")]              // подвійний рік
    [InlineData("@#DHEBREW@ 1 TSH 5700")] // чужий календар
    [InlineData("MAR")]                  // без року
    [InlineData("не пам'ятаю")]          // взагалі не дата
    public void Unsupported_forms_keep_the_raw_expression(string raw)
    {
        var date = GedcomDateMapper.FromGedcom(raw).ShouldNotBeNull();

        date.Kind.ShouldBe(FamilyDateKind.Phrase);
        date.OriginalGedcom.ShouldBe(raw);

        // Саме завдяки цьому зворотний експорт віддає вираз дослівно.
        GedcomDateMapper.ToGedcom(date).ShouldBe(raw);
    }

    [Theory]
    [InlineData("12 MAR 1950")]
    [InlineData("MAR 1850")]
    [InlineData("1850")]
    [InlineData("ABT 1850")]
    [InlineData("CAL 1850")]
    [InlineData("EST 1850")]
    [InlineData("BEF 1900")]
    [InlineData("AFT 1900")]
    [InlineData("BET 1980 AND 1985")]
    [InlineData("(за переказами)")]
    [InlineData("@#DJULIAN@ 29 FEB 1700")]
    public void Supported_forms_round_trip_unchanged(string raw) =>
        GedcomDateMapper.ToGedcom(GedcomDateMapper.FromGedcom(raw)).ShouldBe(raw);
}
