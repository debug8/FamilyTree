namespace FamilyTree.Gedcom;

/// <summary>Кваліфікатор одиночної дати (точна чи приблизна).</summary>
public enum DateQualifier
{
    Exact,       // 12 MAR 1950
    About,       // ABT
    Calculated,  // CAL
    Estimated,   // EST
}

/// <summary>Календар, у якому записано дату (escape @#D...@).</summary>
public enum GedcomCalendar
{
    Gregorian,
    Julian,
    Hebrew,
    French,
}

/// <summary>Версія стандарту — впливає лише на серіалізацію (BCE vs B.C.).</summary>
public enum GedcomVersion
{
    V551,  // 5.5.1  -> "B.C."
    V70,   // 7.0    -> "BCE"
}

/// <summary>
/// Часткова дата: будь-яка складова (день/місяць/рік) може бути відсутня,
/// що прямо відповідає граматиці GEDCOM [[DAY] MONTH] YEAR.
/// </summary>
public sealed record PartialDate(
    int? Year,
    int? Month = null,   // 1..12
    int? Day = null,     // 1..31
    bool IsBce = false,
    int? DualYear = null,                              // подвійний рік 1750/51 -> 1751
    GedcomCalendar Calendar = GedcomCalendar.Gregorian)
{
    /// <summary>
    /// Репрезентативна точка для сортування/порівняння.
    /// Відсутні складові підставляються мінімальними значеннями.
    /// BCE-роки повертаються від'ємними, щоб коректно сортувалися.
    /// </summary>
    public DateTime? SortKey()
    {
        if (Year is null) return null;
        var y = IsBce ? -Year.Value : Year.Value;
        // DateTime не тримає рік <= 0, тому для BCE віддаємо мінімальну межу,
        // зберігаючи впорядкованість через окремий шлях порівняння.
        if (y <= 0) return DateTime.MinValue;
        try
        {
            return new DateTime(y, Month ?? 1, Day ?? 1);
        }
        catch (ArgumentOutOfRangeException)
        {
            // напр. 31 лютого у брудних даних — падаємо на 1-ше число місяця
            return new DateTime(y, Month ?? 1, 1);
        }
    }
}

/// <summary>
/// DATE_VALUE у вигляді дискримінованого об'єднання.
/// Одиночні (Single) і якісні (Before/After/Between/Period) типи
/// свідомо розділені: вони по-різному впливають на сортування
/// та на обчислення спорідненості.
/// </summary>
public abstract record GedcomDate
{
    /// <summary>Точна або приблизна одиночна дата (Exact/ABT/CAL/EST).</summary>
    public sealed record Single(PartialDate Date, DateQualifier Qualifier = DateQualifier.Exact) : GedcomDate;

    /// <summary>BEF date.</summary>
    public sealed record Before(PartialDate Date) : GedcomDate;

    /// <summary>AFT date.</summary>
    public sealed record After(PartialDate Date) : GedcomDate;

    /// <summary>BET date AND date.</summary>
    public sealed record Between(PartialDate From, PartialDate To) : GedcomDate;

    /// <summary>FROM date [TO date] або TO date — хоч одна межа обов'язкова.</summary>
    public sealed record Period(PartialDate? From, PartialDate? To) : GedcomDate;

    /// <summary>INT date (phrase) — розпарсена дата + оригінальний текст.</summary>
    public sealed record Interpreted(PartialDate Date, string OriginalText) : GedcomDate;

    /// <summary>(free text) — нерозпарсна дата, збережена дослівно.</summary>
    public sealed record Phrase(string Text) : GedcomDate;

    /// <summary>Точка сортування: для діапазонів беремо початок, для періодів — From або To.</summary>
    public DateTime? SortKey() => this switch
    {
        Single s => s.Date.SortKey(),
        Before b => b.Date.SortKey(),
        After a => a.Date.SortKey(),
        Between bt => bt.From.SortKey() ?? bt.To.SortKey(),
        Period p => p.From?.SortKey() ?? p.To?.SortKey(),
        Interpreted i => i.Date.SortKey(),
        Phrase => null,
        _ => null,
    };
}
