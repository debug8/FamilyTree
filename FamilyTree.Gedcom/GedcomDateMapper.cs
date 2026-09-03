using FamilyTree.Domain;

namespace FamilyTree.Gedcom;

/// <summary>
/// Міст між доменною моделлю неточних дат (<see cref="FamilyDate"/>, T-5.2a) і
/// моделлю дат GEDCOM (T-5.2) — в обидва боки.
/// </summary>
public static class GedcomDateMapper
{
    /// <summary>
    /// Доменна дата → рядок <c>DATE_VALUE</c>, або null для «дата невідома».
    /// <para>
    /// Якщо дата прийшла імпортом і зберегла сирий вираз у
    /// <see cref="FamilyDate.OriginalGedcom"/>, віддається саме він — це і є точний
    /// round-trip для форм, яких доменна модель не тримає (періоди <c>FROM…TO</c>,
    /// <c>INT</c>, роки до н.е., подвійні роки, єврейський і французький календарі).
    /// </para>
    /// </summary>
    public static string? ToGedcom(FamilyDate? date)
    {
        if (date is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(date.OriginalGedcom))
        {
            return date.OriginalGedcom.Trim();
        }

        var value = ToGedcomDate(date);
        return value is null ? null : GedcomDateConverter.Format(value);
    }

    /// <summary>Доменна дата → структура GEDCOM. Null, якщо дата структурно порожня.</summary>
    public static GedcomDate? ToGedcomDate(FamilyDate date)
    {
        ArgumentNullException.ThrowIfNull(date);

        if (date.Kind == FamilyDateKind.Phrase)
        {
            return string.IsNullOrWhiteSpace(date.Phrase)
                ? null
                : new GedcomDate.Phrase(date.Phrase.Trim());
        }

        if (date.Start is not { } start)
        {
            return null;
        }

        return date.Kind switch
        {
            FamilyDateKind.Exact => new GedcomDate.Single(ToPartial(start), DateQualifier.Exact),

            FamilyDateKind.Approximate => new GedcomDate.Single(
                ToPartial(start),
                date.Approximation switch
                {
                    DateApproximation.Calculated => DateQualifier.Calculated,
                    DateApproximation.Estimated => DateQualifier.Estimated,
                    _ => DateQualifier.About,
                }),

            FamilyDateKind.Range => ToRange(date, start),

            _ => null,
        };
    }

    private static GedcomDate ToRange(FamilyDate date, DatePoint start) => date.RangeKind switch
    {
        DateRangeKind.Before => new GedcomDate.Before(ToPartial(start)),
        DateRangeKind.After => new GedcomDate.After(ToPartial(start)),

        // Between без верхньої межі структурно неможливий (його відкидає DocumentIntegrity),
        // але як захист від битого файлу віддаємо хоча б нижню межу, а не виняток.
        DateRangeKind.Between when date.End is { } end =>
            new GedcomDate.Between(ToPartial(start), ToPartial(end)),

        _ => new GedcomDate.Single(ToPartial(start), DateQualifier.Exact),
    };

    private static PartialDate ToPartial(DatePoint point) => new(
        point.Year,
        point.Month,
        point.Day,
        IsBce: false,
        DualYear: null,
        point.Calendar == DateCalendar.Julian ? GedcomCalendar.Julian : GedcomCalendar.Gregorian);

    // ------------------------------------------------------------ Усередину

    /// <summary>
    /// Рядок <c>DATE_VALUE</c> → доменна дата, або null для порожнього значення.
    /// <para>
    /// Форми, яких модель не тримає (періоди <c>FROM…TO</c>, <c>INT</c>, роки до н.е.,
    /// подвійні роки, єврейський і французький календарі, дата без року), зводяться до
    /// <see cref="FamilyDateKind.Phrase"/> і зберігають сирий вираз у
    /// <see cref="FamilyDate.OriginalGedcom"/> — при зворотному експорті він віддається
    /// дослівно, тож round-trip лишається точним попри втрату структури.
    /// </para>
    /// </summary>
    public static FamilyDate? FromGedcom(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();

        return GedcomDateConverter.TryParse(text, out var parsed) && parsed is not null
            ? FromGedcomDate(parsed, text)
            : KeepAsText(text);
    }

    /// <summary>Структура GEDCOM → доменна дата. <paramref name="raw"/> потрібен для round-trip.</summary>
    public static FamilyDate FromGedcomDate(GedcomDate value, string raw)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            GedcomDate.Single s when ToPoint(s.Date) is { } point =>
                s.Qualifier == DateQualifier.Exact
                    ? FamilyDate.Exact(point)
                    : FamilyDate.Approximate(ToApproximation(s.Qualifier), point),

            GedcomDate.Before b when ToPoint(b.Date) is { } point => FamilyDate.Before(point),

            GedcomDate.After a when ToPoint(a.Date) is { } point => FamilyDate.After(point),

            GedcomDate.Between bt when ToPoint(bt.From) is { } from && ToPoint(bt.To) is { } to =>
                FamilyDate.Between(from, to),

            GedcomDate.Phrase p when !string.IsNullOrWhiteSpace(p.Text) => FamilyDate.FromPhrase(p.Text),

            // Period, Interpreted і будь-яка точка, що не лягла в DatePoint.
            _ => KeepAsText(raw),
        };
    }

    private static FamilyDate KeepAsText(string raw) =>
        FamilyDate.FromPhrase(raw).WithOriginalGedcom(raw);

    private static DateApproximation ToApproximation(DateQualifier qualifier) => qualifier switch
    {
        DateQualifier.Calculated => DateApproximation.Calculated,
        DateQualifier.Estimated => DateApproximation.Estimated,
        _ => DateApproximation.About,
    };

    /// <summary>
    /// <see cref="PartialDate"/> → <see cref="DatePoint"/>, або null, якщо форма поза
    /// межами моделі: немає року, рік до н.е., подвійний рік, неґрегоріанський і
    /// неюліанський календар, структурно неможлива дата.
    /// </summary>
    private static DatePoint? ToPoint(PartialDate partial)
    {
        if (partial.Year is not { } year
            || partial.IsBce
            || partial.DualYear is not null
            || partial.Calendar is GedcomCalendar.Hebrew or GedcomCalendar.French)
        {
            return null;
        }

        var point = new DatePoint
        {
            Year = year,
            Month = partial.Month,
            Day = partial.Day,
            Calendar = partial.Calendar == GedcomCalendar.Julian ? DateCalendar.Julian : DateCalendar.Gregorian,
        };

        return point.IsValid ? point : null;
    }
}
