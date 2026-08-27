using System.Globalization;

namespace FamilyTree.Domain;

/// <summary>
/// Форматує <see cref="FamilyDate"/> у рядок для показу (T-5.2a): «14.03.1980», «березень 1850»,
/// «1850», «≈1850», «до 1900», «1980–1985», текст фрази. Числа й назви місяців беруться з
/// переданої культури; мовозалежні слова (до/після, обч./оц., позначка юліанського) — за мовою
/// культури (uk/en). Без залежностей від WPF — так само, як форматери спорідненості в Domain.
/// </summary>
public static class FamilyDateFormatter
{
    /// <summary>Рядок для показу; порожньо для <c>null</c>.</summary>
    public static string Format(FamilyDate? date, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (date is null)
        {
            return string.Empty;
        }

        var uk = IsUkrainian(culture);
        return date.Kind switch
        {
            FamilyDateKind.Exact => FormatPoint(date.Start, culture),
            FamilyDateKind.Approximate => ApproximationPrefix(date.Approximation, uk) + FormatPoint(date.Start, culture),
            FamilyDateKind.Range => FormatRange(date, culture, uk),
            FamilyDateKind.Phrase => date.Phrase ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static bool IsUkrainian(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase);

    private static string FormatPoint(DatePoint? point, CultureInfo culture)
    {
        if (point is null)
        {
            return string.Empty;
        }

        var core = point.Precision switch
        {
            DatePrecision.Day => point.ToDateOnly()?.ToString("d", culture)
                ?? point.Year.ToString(CultureInfo.InvariantCulture),
            DatePrecision.Month => FormatMonthYear(point, culture),
            _ => point.Year.ToString(CultureInfo.InvariantCulture),
        };

        return point.Calendar == DateCalendar.Julian ? core + JulianSuffix(culture) : core;
    }

    private static string FormatMonthYear(DatePoint point, CultureInfo culture)
    {
        var month = point.Month ?? 1;
        var name = month is >= 1 and <= 12
            ? culture.DateTimeFormat.MonthNames[month - 1]
            : month.ToString(CultureInfo.InvariantCulture);
        return $"{name} {point.Year.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string JulianSuffix(CultureInfo culture) => IsUkrainian(culture) ? " (юл.)" : " (Jul.)";

    private static string ApproximationPrefix(DateApproximation? approximation, bool uk) => approximation switch
    {
        DateApproximation.About => "≈",
        DateApproximation.Calculated => uk ? "обч. " : "calc. ",
        DateApproximation.Estimated => uk ? "оц. " : "est. ",
        _ => string.Empty,
    };

    private static string FormatRange(FamilyDate date, CultureInfo culture, bool uk) => date.RangeKind switch
    {
        DateRangeKind.Before => (uk ? "до " : "before ") + FormatPoint(date.Start, culture),
        DateRangeKind.After => (uk ? "після " : "after ") + FormatPoint(date.Start, culture),
        DateRangeKind.Between => $"{FormatPoint(date.Start, culture)}–{FormatPoint(date.End, culture)}",
        _ => string.Empty,
    };
}
