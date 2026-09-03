using FamilyTree.Domain;

namespace FamilyTree.Gedcom;

/// <summary>
/// Міст між доменною моделлю неточних дат (<see cref="FamilyDate"/>, T-5.2a) і
/// моделлю дат GEDCOM (T-5.2). Тут — напрямок назовні; зворотний додасть Частина 3.
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
}
