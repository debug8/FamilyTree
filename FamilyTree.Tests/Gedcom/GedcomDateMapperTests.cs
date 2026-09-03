using FamilyTree.Domain;
using FamilyTree.Gedcom;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Gedcom;

/// <summary>FamilyDate → рядок DATE_VALUE (T-5.2, Частина 2).</summary>
public sealed class GedcomDateMapperTests
{
    private static DatePoint Point(int year, int? month = null, int? day = null, DateCalendar calendar = DateCalendar.Gregorian)
        => new() { Year = year, Month = month, Day = day, Calendar = calendar };

    [Fact]
    public void Null_date_maps_to_null() =>
        GedcomDateMapper.ToGedcom(null).ShouldBeNull();

    [Fact]
    public void Exact_full_date() =>
        GedcomDateMapper.ToGedcom(FamilyDate.Exact(new DateOnly(1950, 3, 12))).ShouldBe("12 MAR 1950");

    [Fact]
    public void Partial_month_and_year() =>
        GedcomDateMapper.ToGedcom(FamilyDate.Exact(Point(1850, 3))).ShouldBe("MAR 1850");

    [Fact]
    public void Partial_year_only() =>
        GedcomDateMapper.ToGedcom(FamilyDate.Exact(Point(1850))).ShouldBe("1850");

    [Theory]
    [InlineData(DateApproximation.About, "ABT 1850")]
    [InlineData(DateApproximation.Calculated, "CAL 1850")]
    [InlineData(DateApproximation.Estimated, "EST 1850")]
    public void Approximate_qualifiers(DateApproximation approximation, string expected) =>
        GedcomDateMapper.ToGedcom(FamilyDate.Approximate(approximation, Point(1850))).ShouldBe(expected);

    [Fact]
    public void Before_range() =>
        GedcomDateMapper.ToGedcom(FamilyDate.Before(Point(1900))).ShouldBe("BEF 1900");

    [Fact]
    public void After_range() =>
        GedcomDateMapper.ToGedcom(FamilyDate.After(Point(1900))).ShouldBe("AFT 1900");

    [Fact]
    public void Between_range() =>
        GedcomDateMapper.ToGedcom(FamilyDate.Between(Point(1980), Point(1985))).ShouldBe("BET 1980 AND 1985");

    [Fact]
    public void Phrase_goes_in_parentheses() =>
        GedcomDateMapper.ToGedcom(FamilyDate.FromPhrase("за переказами")).ShouldBe("(за переказами)");

    [Fact]
    public void Julian_calendar_gets_an_escape() =>
        GedcomDateMapper.ToGedcom(FamilyDate.Exact(Point(1700, 2, 29, DateCalendar.Julian)))
            .ShouldBe("@#DJULIAN@ 29 FEB 1700");

    [Fact]
    public void Original_expression_wins_over_generation()
    {
        // Форми, яких доменна модель не тримає, повертаються назовні дослівно.
        var date = FamilyDate.FromPhrase("1750–1760").WithOriginalGedcom("FROM 1750 TO 1760");

        GedcomDateMapper.ToGedcom(date).ShouldBe("FROM 1750 TO 1760");
    }

    [Fact]
    public void Every_generated_value_is_parsable_back()
    {
        // Те, що ми пишемо, мусить читатися нашим же розбирачем.
        FamilyDate[] dates =
        {
            FamilyDate.Exact(new DateOnly(1950, 3, 12)),
            FamilyDate.Exact(Point(1850, 3)),
            FamilyDate.Exact(Point(1850)),
            FamilyDate.Approximate(DateApproximation.About, Point(1850)),
            FamilyDate.Before(Point(1900)),
            FamilyDate.After(Point(1900)),
            FamilyDate.Between(Point(1980), Point(1985)),
            FamilyDate.FromPhrase("за переказами"),
            FamilyDate.Exact(Point(1700, 2, 29, DateCalendar.Julian)),
        };

        foreach (var date in dates)
        {
            var text = GedcomDateMapper.ToGedcom(date).ShouldNotBeNull();
            GedcomDateConverter.TryParse(text, out var parsed).ShouldBeTrue($"не розібралось: «{text}»");
            parsed.ShouldNotBeNull();
        }
    }
}
