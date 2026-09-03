using System.Globalization;
using System.Text;

namespace FamilyTree.Gedcom;

/// <summary>
/// Запис дерева <see cref="GedcomNode"/> у текст GEDCOM (T-5.2, Частина 2):
/// рівні, розбиття довгих значень на <c>CONC</c>, переноси через <c>CONT</c>,
/// подвоєння «собачки», кінці рядків CRLF.
/// </summary>
public static class GedcomWriter
{
    /// <summary>Стандарт 5.5.1 обмежує рядок 255 байтами разом із кінцем рядка.</summary>
    private const int MaxLineBytes = 255;

    private const string NewLine = "\r\n";

    /// <summary>Записує дерево в текст. Корінь сам не друкується — лише його нащадки.</summary>
    public static string Write(GedcomNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var sb = new StringBuilder();
        foreach (var record in root.Children)
        {
            WriteNode(record, 0, sb);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Записує дерево в байти: UTF-8 <b>з BOM</b>. BOM тут навмисно — частина
    /// генеалогічних програм визначає кодування саме за ним, а не за <c>HEAD.CHAR</c>.
    /// </summary>
    public static byte[] WriteBytes(GedcomNode root)
    {
        var text = Write(root);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(text);

        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    private static void WriteNode(GedcomNode node, int level, StringBuilder sb)
    {
        var prefix = new StringBuilder()
            .Append(level.ToString(CultureInfo.InvariantCulture))
            .Append(' ');

        if (node.Xref is not null)
        {
            prefix.Append('@').Append(node.Xref).Append('@').Append(' ');
        }

        prefix.Append(node.Tag);
        var head = prefix.ToString();

        if (node.Pointer is not null)
        {
            // Вказівник не екранується — «собачки» тут структурні.
            sb.Append(head).Append(" @").Append(node.Pointer).Append('@').Append(NewLine);
        }
        else if (node.Value is null)
        {
            sb.Append(head).Append(NewLine);
        }
        else
        {
            WriteValue(head, level, Escape(node.Value), sb);
        }

        foreach (var child in node.Children)
        {
            WriteNode(child, level + 1, sb);
        }
    }

    /// <summary>
    /// Друкує значення: перенос рядка — окремим <c>CONT</c>, «хвіст» задовгого
    /// рядка — <c>CONC</c>. Обидва продовжувачі стоять на рівні <c>level + 1</c>.
    /// </summary>
    private static void WriteValue(string head, int level, string value, StringBuilder sb)
    {
        var contPrefix = (level + 1).ToString(CultureInfo.InvariantCulture) + " CONT";
        var concPrefix = (level + 1).ToString(CultureInfo.InvariantCulture) + " CONC";

        var segments = value.Split('\n');
        for (var s = 0; s < segments.Length; s++)
        {
            var rest = segments[s];
            var isFirstChunk = true;

            do
            {
                var linePrefix = isFirstChunk ? (s == 0 ? head : contPrefix) : concPrefix;

                // −1 на пробіл-роздільник, −2 на CRLF.
                var budget = MaxLineBytes - Encoding.UTF8.GetByteCount(linePrefix) - 3;
                var chunk = Take(rest, Math.Max(budget, 1), out rest);

                sb.Append(linePrefix);
                if (chunk.Length > 0)
                {
                    sb.Append(' ').Append(chunk);
                }

                sb.Append(NewLine);
                isFirstChunk = false;
            }
            while (rest.Length > 0);
        }
    }

    /// <summary>
    /// Відкушує від початку рядка стільки, скільки влазить у <paramref name="budgetBytes"/>,
    /// не розриваючи символ і не розриваючи екрановану «@@» навпіл.
    /// Завжди відкушує хоча б один символ, щоб не зациклитись на мізерному бюджеті.
    /// </summary>
    private static string Take(string text, int budgetBytes, out string rest)
    {
        if (text.Length == 0)
        {
            rest = string.Empty;
            return string.Empty;
        }

        var used = 0;
        var count = 0;

        while (count < text.Length)
        {
            // Сурогатна пара — неподільна.
            var runeLength = char.IsHighSurrogate(text[count]) && count + 1 < text.Length ? 2 : 1;
            var size = Encoding.UTF8.GetByteCount(text.AsSpan(count, runeLength));

            if (used + size > budgetBytes && count > 0)
            {
                break;
            }

            used += size;
            count += runeLength;

            if (used >= budgetBytes)
            {
                break;
            }
        }

        // Не лишати непарну «@» на межі: інакше при читанні «@@» розпадеться на дві.
        var trailingAts = 0;
        while (trailingAts < count && text[count - 1 - trailingAts] == '@')
        {
            trailingAts++;
        }

        if (trailingAts % 2 == 1 && count > 1)
        {
            count--;
        }

        // Не лишати пробіл у кінці рядка: читач обрізає рядок з обох боків
        // (у чужих файлах хвостові пробіли — сміття), тож розрив саме на пробілі
        // мовчки з'їв би його й зіпсував склеювання CONC. Переносимо пробіли далі.
        var withoutTrailingSpaces = count;
        while (withoutTrailingSpaces > 0 && text[withoutTrailingSpaces - 1] == ' ')
        {
            withoutTrailingSpaces--;
        }

        if (withoutTrailingSpaces > 0)
        {
            count = withoutTrailingSpaces;
        }

        rest = text[count..];
        return text[..count];
    }

    /// <summary>За 5.5.1 літеральна «собачка» у значенні подвоюється.</summary>
    private static string Escape(string value) => value.Replace("@", "@@", StringComparison.Ordinal);
}
