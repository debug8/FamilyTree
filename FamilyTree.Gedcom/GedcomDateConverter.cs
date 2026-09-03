using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyTree.Gedcom;

/// <summary>
/// Розбір (string → GedcomDate) та серіалізація (GedcomDate → string)
/// значень DATE_VALUE за граматикою GEDCOM 5.5.1 / 7.0.
///
/// Обмеження: назви місяців підтримуються для григоріанського та юліанського
/// календарів (спільний набір JAN..DEC). Escape єврейського/французького
/// календарів розпізнається й зберігається у PartialDate.Calendar, але їхні
/// власні назви місяців у цьому конвертері не мапляться.
/// </summary>
public static partial class GedcomDateConverter
{
    private static readonly string[] MonthNames =
        { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    private static readonly Dictionary<string, int> MonthLookup =
        MonthNames.Select((name, i) => (name, i))
                  .ToDictionary(t => t.name, t => t.i + 1, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^@#D([A-Z ]+)@\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CalendarEscapeRegex();

    [GeneratedRegex(@"\s*(B\.C\.|BCE)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BceRegex();

    [GeneratedRegex(@"^FROM\s+(.+?)\s+TO\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FromToRegex();

    [GeneratedRegex(@"^BET\s+(.+?)\s+AND\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BetweenRegex();

    [GeneratedRegex(@"\(([^)]*)\)\s*$")]
    private static partial Regex TrailingPhraseRegex();

    // ---------------------------------------------------------------- Parse

    /// <summary>Розбирає рядок DATE_VALUE у структуру GedcomDate.</summary>
    public static GedcomDate Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new FormatException("Порожнє значення дати.");

        var s = raw.Trim();
        var upper = s.ToUpperInvariant();

        // 6. Фраза цілком у дужках
        if (s.StartsWith('(') && s.EndsWith(')'))
            return new GedcomDate.Phrase(s[1..^1].Trim());

        // 5. INT date (phrase)
        if (upper.StartsWith("INT "))
        {
            var rest = s[4..].Trim();
            var phrase = string.Empty;
            var m = TrailingPhraseRegex().Match(rest);
            if (m.Success)
            {
                phrase = m.Groups[1].Value.Trim();
                rest = rest[..m.Index].Trim();
            }
            return new GedcomDate.Interpreted(ParsePartialDate(rest), phrase);
        }

        // 4. Період: FROM x TO y | FROM x | TO y
        var fromTo = FromToRegex().Match(s);
        if (fromTo.Success)
            return new GedcomDate.Period(
                ParsePartialDate(fromTo.Groups[1].Value),
                ParsePartialDate(fromTo.Groups[2].Value));
        if (upper.StartsWith("FROM "))
            return new GedcomDate.Period(ParsePartialDate(s[5..].Trim()), null);
        if (upper.StartsWith("TO "))
            return new GedcomDate.Period(null, ParsePartialDate(s[3..].Trim()));

        // 3. Діапазон: BET x AND y | BEF x | AFT x
        var bet = BetweenRegex().Match(s);
        if (bet.Success)
            return new GedcomDate.Between(
                ParsePartialDate(bet.Groups[1].Value),
                ParsePartialDate(bet.Groups[2].Value));
        if (upper.StartsWith("BEF "))
            return new GedcomDate.Before(ParsePartialDate(s[4..].Trim()));
        if (upper.StartsWith("AFT "))
            return new GedcomDate.After(ParsePartialDate(s[4..].Trim()));

        // 2. Приблизні: ABT | CAL | EST
        if (upper.StartsWith("ABT "))
            return new GedcomDate.Single(ParsePartialDate(s[4..].Trim()), DateQualifier.About);
        if (upper.StartsWith("CAL "))
            return new GedcomDate.Single(ParsePartialDate(s[4..].Trim()), DateQualifier.Calculated);
        if (upper.StartsWith("EST "))
            return new GedcomDate.Single(ParsePartialDate(s[4..].Trim()), DateQualifier.Estimated);

        // 1. Точна
        return new GedcomDate.Single(ParsePartialDate(s), DateQualifier.Exact);
    }

    /// <summary>Безпечний варіант: повертає false замість викидання винятку.</summary>
    public static bool TryParse(string raw, out GedcomDate? result)
    {
        try
        {
            result = Parse(raw);
            return true;
        }
        catch (FormatException)
        {
            result = null;
            return false;
        }
    }

    private static PartialDate ParsePartialDate(string input)
    {
        var s = input.Trim();
        var calendar = GedcomCalendar.Gregorian;

        // Escape календаря на початку
        var esc = CalendarEscapeRegex().Match(s);
        if (esc.Success)
        {
            calendar = esc.Groups[1].Value.Trim().ToUpperInvariant() switch
            {
                "JULIAN" => GedcomCalendar.Julian,
                "HEBREW" => GedcomCalendar.Hebrew,
                "FRENCH R" => GedcomCalendar.French,
                _ => GedcomCalendar.Gregorian,
            };
            s = s[esc.Length..];
        }

        // Ера (в кінці)
        var isBce = false;
        var bce = BceRegex().Match(s);
        if (bce.Success)
        {
            isBce = true;
            s = s[..bce.Index];
        }

        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            throw new FormatException($"Не вдалося розібрати дату: «{input}».");

        // Знаходимо позицію місяця; за граматикою день стоїть до нього, рік — після.
        int monthIndex = -1;
        int? month = null;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (MonthLookup.TryGetValue(tokens[i], out var m))
            {
                month = m;
                monthIndex = i;
                break;
            }
        }

        int? day = null, year = null, dualYear = null;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (i == monthIndex) continue;

            var tok = tokens[i];

            // Подвійний рік: 1750/51 або 1750/1751
            if (tok.Contains('/'))
            {
                var parts = tok.Split('/', 2);
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y0))
                    throw new FormatException($"Некоректний рік у «{tok}».");
                year = y0;
                dualYear = NormalizeDualYear(y0, parts[1]);
                continue;
            }

            if (!int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                throw new FormatException($"Неочікуваний токен «{tok}» у даті «{input}».");

            // До місяця -> день; після місяця (або якщо місяця нема) -> рік
            if (monthIndex >= 0 && i < monthIndex)
                day = n;
            else
                year = n;
        }

        if (year is null && month is null && day is null)
            throw new FormatException($"Порожня дата: «{input}».");

        return new PartialDate(year, month, day, isBce, dualYear, calendar);
    }

    private static int? NormalizeDualYear(int year, string tail)
    {
        if (!int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
            return null;

        // Дволітерний хвіст (1750/51) -> добудовуємо століття
        if (tail.Length <= 2)
        {
            var century = year / 100 * 100;
            var candidate = century + t;
            if (candidate <= year) candidate += 100; // перехід через межу століття
            return candidate;
        }
        return t;
    }

    // ------------------------------------------------------------ Serialize

    /// <summary>Серіалізує GedcomDate у рядок DATE_VALUE.</summary>
    public static string Format(GedcomDate date, GedcomVersion version = GedcomVersion.V551) => date switch
    {
        GedcomDate.Single s => QualifierPrefix(s.Qualifier) + FormatPartial(s.Date, version),
        GedcomDate.Before b => "BEF " + FormatPartial(b.Date, version),
        GedcomDate.After a => "AFT " + FormatPartial(a.Date, version),
        GedcomDate.Between bt => $"BET {FormatPartial(bt.From, version)} AND {FormatPartial(bt.To, version)}",
        GedcomDate.Period p => FormatPeriod(p, version),
        GedcomDate.Interpreted i => $"INT {FormatPartial(i.Date, version)} ({i.OriginalText})",
        GedcomDate.Phrase ph => $"({ph.Text})",
        _ => throw new ArgumentOutOfRangeException(nameof(date)),
    };

    private static string QualifierPrefix(DateQualifier q) => q switch
    {
        DateQualifier.Exact => string.Empty,
        DateQualifier.About => "ABT ",
        DateQualifier.Calculated => "CAL ",
        DateQualifier.Estimated => "EST ",
        _ => string.Empty,
    };

    private static string FormatPeriod(GedcomDate.Period p, GedcomVersion version)
    {
        if (p.From is not null && p.To is not null)
            return $"FROM {FormatPartial(p.From, version)} TO {FormatPartial(p.To, version)}";
        if (p.From is not null)
            return $"FROM {FormatPartial(p.From, version)}";
        if (p.To is not null)
            return $"TO {FormatPartial(p.To, version)}";
        throw new FormatException("Період без жодної межі.");
    }

    private static string FormatPartial(PartialDate d, GedcomVersion version)
    {
        var sb = new StringBuilder();

        if (d.Calendar != GedcomCalendar.Gregorian)
            sb.Append(CalendarEscape(d.Calendar)).Append(' ');

        if (d.Day is int day)
            sb.Append(day).Append(' ');

        if (d.Month is int m)
            sb.Append(MonthNames[m - 1]).Append(' ');

        if (d.Year is int y)
        {
            sb.Append(y);
            if (d.DualYear is int dual)
                sb.Append('/').Append((dual % 100).ToString("D2", CultureInfo.InvariantCulture));
        }

        var result = sb.ToString().TrimEnd();

        if (d.IsBce)
            result += version == GedcomVersion.V70 ? " BCE" : " B.C.";

        return result;
    }

    private static string CalendarEscape(GedcomCalendar c) => c switch
    {
        GedcomCalendar.Julian => "@#DJULIAN@",
        GedcomCalendar.Hebrew => "@#DHEBREW@",
        GedcomCalendar.French => "@#DFRENCH R@",
        _ => "@#DGREGORIAN@",
    };
}
