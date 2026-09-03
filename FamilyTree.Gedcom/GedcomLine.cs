namespace FamilyTree.Gedcom;

/// <summary>
/// Один розібраний рядок GEDCOM: <c>рівень [@xref@] ТЕГ [значення]</c>.
/// Наприклад <c>1 BIRT</c>, <c>0 @I1@ INDI</c>, <c>2 DATE 12 MAR 1950</c>, <c>1 FAMS @F1@</c>.
/// </summary>
/// <param name="Level">Рівень вкладення (0 — запис верхнього рівня).</param>
/// <param name="Xref">Власний ідентифікатор запису без «собачок» (напр. <c>I1</c>), або null.</param>
/// <param name="Tag">Тег у верхньому регістрі (<c>INDI</c>, <c>BIRT</c>, <c>_UID</c>).</param>
/// <param name="Value">Значення після тега; для рядка без значення — null.</param>
/// <param name="Pointer">
/// Значення-вказівник без «собачок» (напр. <c>F1</c> для <c>1 FAMS @F1@</c>), або null.
/// Вказівник і звичайне значення взаємовиключні.
/// </param>
public sealed record GedcomLine(int Level, string? Xref, string Tag, string? Value, string? Pointer)
{
    /// <summary>Чи є значення цього рядка посиланням на інший запис.</summary>
    public bool IsPointer => Pointer is not null;

    /// <summary>
    /// Розбирає рядок. Повертає <c>null</c>, якщо рядок порожній або структурно негодящий
    /// (немає рівня чи тега) — викликач рахує такі рядки як пропущені, а не падає:
    /// реальні файли рясніють сміттям, і один битий рядок не привід відкидати родину.
    /// </summary>
    public static GedcomLine? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Провідні пробіли стандартом заборонені, але трапляються — і це не привід відмовляти.
        var s = raw.Trim();

        // ---- рівень ----
        var i = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            i++;
        }

        if (i == 0 || !int.TryParse(s[..i], out var level) || level < 0)
        {
            return null;
        }

        i = SkipSpaces(s, i);
        if (i >= s.Length)
        {
            return null;
        }

        // ---- необов'язковий xref ----
        string? xref = null;
        if (s[i] == '@')
        {
            var close = s.IndexOf('@', i + 1);
            if (close < 0 || close == i + 1)
            {
                return null; // «@» без пари або порожній @@ на місці xref
            }

            xref = s[(i + 1)..close];
            i = SkipSpaces(s, close + 1);
            if (i >= s.Length)
            {
                return null; // xref без тега
            }
        }

        // ---- тег ----
        var tagStart = i;
        while (i < s.Length && !char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        var tag = s[tagStart..i];
        if (tag.Length == 0 || !IsValidTag(tag))
        {
            return null;
        }

        // ---- значення ----
        // Рівно один пробіл-роздільник; решта пробілів належить значенню (важливо для CONC).
        var value = i < s.Length ? s[(i + 1)..] : null;
        if (value is { Length: 0 })
        {
            value = null;
        }

        string? pointer = null;
        if (value is not null)
        {
            if (IsPointerValue(value))
            {
                pointer = value[1..^1];
                value = null;
            }
            else if (value.Contains('@', StringComparison.Ordinal))
            {
                // За 5.5.1 літеральна «собачка» у тексті подвоюється.
                value = value.Replace("@@", "@", StringComparison.Ordinal);
            }
        }

        return new GedcomLine(level, xref, tag.ToUpperInvariant(), value, pointer);
    }

    private static int SkipSpaces(string s, int index)
    {
        while (index < s.Length && s[index] == ' ')
        {
            index++;
        }

        return index;
    }

    /// <summary>Тег — літери, цифри та підкреслення (кастомні теги починаються з «_»).</summary>
    private static bool IsValidTag(string tag)
    {
        foreach (var c in tag)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Значення-вказівник: рівно <c>@xxx@</c> без внутрішніх «собачок».</summary>
    private static bool IsPointerValue(string value) =>
        value.Length > 2
        && value[0] == '@'
        && value[^1] == '@'
        && value.IndexOf('@', 1) == value.Length - 1;
}
