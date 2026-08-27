using System.Globalization;
using FamilyTree.Domain;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>Показ неточних дат (T-5.2a). Асерти уникають залежності від точного шаблону
/// короткої дати культури — перевіряють мовозалежні слова, назви місяців і роки.</summary>
public sealed class FamilyDateFormatterTests
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Null_is_empty()
    {
        FamilyDateFormatter.Format(null, Uk).ShouldBe(string.Empty);
    }

    [Fact]
    public void Exact_full_date_uses_culture_short_date()
    {
        var date = FamilyDate.Exact(new DateOnly(1980, 3, 14));
        FamilyDateFormatter.Format(date, Uk).ShouldBe(new DateOnly(1980, 3, 14).ToString("d", Uk));
        FamilyDateFormatter.Format(date, En).ShouldBe(new DateOnly(1980, 3, 14).ToString("d", En));
    }

    [Fact]
    public void Partial_year_is_just_the_year()
    {
        var date = FamilyDate.Exact(new DatePoint { Year = 1850 });
        FamilyDateFormatter.Format(date, Uk).ShouldBe("1850");
        FamilyDateFormatter.Format(date, En).ShouldBe("1850");
    }

    [Fact]
    public void Partial_month_uses_month_name_and_year()
    {
        var date = FamilyDate.Exact(new DatePoint { Year = 1850, Month = 3 });

        var uk = FamilyDateFormatter.Format(date, Uk);
        uk.ShouldContain(Uk.DateTimeFormat.MonthNames[2]); // березень
        uk.ShouldContain("1850");

        var en = FamilyDateFormatter.Format(date, En);
        en.ShouldContain("March");
        en.ShouldContain("1850");
    }

    [Fact]
    public void Approximate_about_prefixes_tilde()
    {
        var date = FamilyDate.Approximate(DateApproximation.About, new DatePoint { Year = 1850 });
        FamilyDateFormatter.Format(date, Uk).ShouldBe("≈1850");
        FamilyDateFormatter.Format(date, En).ShouldBe("≈1850");
    }

    [Fact]
    public void Approximate_estimated_is_language_specific()
    {
        var date = FamilyDate.Approximate(DateApproximation.Estimated, new DatePoint { Year = 1850 });
        FamilyDateFormatter.Format(date, Uk).ShouldBe("оц. 1850");
        FamilyDateFormatter.Format(date, En).ShouldBe("est. 1850");
    }

    [Fact]
    public void Before_and_after_use_language_words()
    {
        var before = FamilyDate.Before(new DatePoint { Year = 1900 });
        FamilyDateFormatter.Format(before, Uk).ShouldBe("до 1900");
        FamilyDateFormatter.Format(before, En).ShouldBe("before 1900");

        var after = FamilyDate.After(new DatePoint { Year = 1800 });
        FamilyDateFormatter.Format(after, Uk).ShouldBe("після 1800");
        FamilyDateFormatter.Format(after, En).ShouldBe("after 1800");
    }

    [Fact]
    public void Between_joins_with_en_dash()
    {
        var date = FamilyDate.Between(new DatePoint { Year = 1980 }, new DatePoint { Year = 1985 });
        FamilyDateFormatter.Format(date, Uk).ShouldBe("1980–1985");
    }

    [Fact]
    public void Phrase_is_returned_as_is()
    {
        var date = FamilyDate.FromPhrase("близько Різдва 1900");
        FamilyDateFormatter.Format(date, Uk).ShouldBe("близько Різдва 1900");
    }

    [Fact]
    public void Julian_year_gets_marker()
    {
        var date = FamilyDate.Exact(new DatePoint { Year = 1700, Calendar = DateCalendar.Julian });
        FamilyDateFormatter.Format(date, Uk).ShouldBe("1700 (юл.)");
        FamilyDateFormatter.Format(date, En).ShouldBe("1700 (Jul.)");
    }
}
