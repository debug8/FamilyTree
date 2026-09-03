namespace FamilyTree.Gedcom;

/// <summary>
/// Технічні відомості про прочитаний файл — усе, що потрібно звіту імпорту.
/// </summary>
/// <param name="EncodingName">Фактично застосоване кодування.</param>
/// <param name="DeclaredCharset">Значення <c>HEAD.CHAR</c> з файлу, або null.</param>
/// <param name="EncodingFallbackFrom">Оголошене кодування, яке не підійшло, або null.</param>
/// <param name="MalformedLines">Скільки рядків не вдалося розібрати (їх пропущено).</param>
/// <param name="Version">Значення <c>HEAD.GEDC.VERS</c>, або null.</param>
/// <param name="Source">Значення <c>HEAD.SOUR</c> — програма-автор файлу, або null.</param>
public sealed record GedcomReadInfo(
    string EncodingName,
    string? DeclaredCharset,
    string? EncodingFallbackFrom,
    int MalformedLines,
    string? Version,
    string? Source);

/// <summary>Прочитаний файл: дерево записів верхнього рівня + відомості про читання.</summary>
public sealed class GedcomFile
{
    internal GedcomFile(GedcomNode root, GedcomReadInfo info)
    {
        Root = root;
        Info = info;
    }

    /// <summary>Штучний корінь; його нащадки — записи рівня 0 (HEAD, INDI, FAM, TRLR).</summary>
    public GedcomNode Root { get; }

    /// <summary>Відомості про читання (кодування, версія, пропущені рядки).</summary>
    public GedcomReadInfo Info { get; }

    /// <summary>Записи верхнього рівня з указаним тегом.</summary>
    public IEnumerable<GedcomNode> Records(string tag) => Root.ChildrenOf(tag);
}

/// <summary>
/// Читання GEDCOM: байти → дерево <see cref="GedcomNode"/> (T-5.2, Частина 1).
/// Розбір навмисно поблажливий: рядки, які не піддалися, пропускаються й рахуються,
/// а не валять імпорт. Відмова буває лише в двох випадках — це взагалі не GEDCOM
/// або версія стандарту нам не по зубах.
/// </summary>
public static class GedcomReader
{
    /// <summary>Версії 7.x мають іншу граматику й вимагають окремої реалізації.</summary>
    private const string UnsupportedVersionPrefix = "7";

    public static GedcomFile Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var decoded = GedcomEncodingDetector.Decode(bytes);
        return Read(decoded.Text, decoded.EncodingName, decoded.Declared, decoded.FallbackFrom);
    }

    /// <summary>Читання з готового тексту — для тестів і для повторного розбору.</summary>
    public static GedcomFile Read(
        string text,
        string encodingName = "utf-8",
        string? declaredCharset = null,
        string? encodingFallbackFrom = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var root = new GedcomNode(string.Empty);
        var malformed = 0;

        // Стек відкритих вузлів: індекс = рівень. Нульовий елемент — корінь.
        var open = new List<GedcomNode> { root };

        foreach (var rawLine in SplitLines(text))
        {
            var line = GedcomLine.Parse(rawLine);
            if (line is null)
            {
                if (!string.IsNullOrWhiteSpace(rawLine))
                {
                    malformed++;
                }

                continue;
            }

            // Стрибок рівня вниз (0 -> 2) стандартом заборонений; підв'язуємо до найглибшого
            // наявного, щоб не втратити дані, і рахуємо як дефект.
            var level = line.Level;
            if (level >= open.Count)
            {
                malformed++;
                level = open.Count - 1;
            }

            // open[k] — вузол, до якого підв'язуються рядки рівня k (тобто вузол рівня k-1).
            var parent = open[level];

            // CONC/CONT продовжують значення БАТЬКА, а не попереднього сусіда: у
            // «1 NOTE …» / «2 SOUR …» / «2 CONC …» продовження належить NOTE, не SOUR.
            if (line.Tag is "CONC" or "CONT")
            {
                if (level == 0)
                {
                    malformed++;
                    continue;
                }

                parent.Value = line.Tag == "CONC"
                    ? (parent.Value ?? string.Empty) + (line.Value ?? string.Empty)
                    : (parent.Value ?? string.Empty) + "\n" + (line.Value ?? string.Empty);

                continue;
            }

            var node = new GedcomNode(line.Tag, line.Xref, line.Value, line.Pointer);
            parent.Add(node);

            // Вузол стає відкритим на своєму рівні; глибші рівні втрачають чинність.
            open.RemoveRange(level + 1, open.Count - level - 1);
            open.Add(node);
        }

        var head = root.Child("HEAD");
        if (head is null)
        {
            throw GedcomException.Create(GedcomKeys.NotGedcom, inner: null);
        }

        var version = head.Path("GEDC", "VERS");
        if (version is not null && version.TrimStart().StartsWith(UnsupportedVersionPrefix, StringComparison.Ordinal))
        {
            throw GedcomException.Create(GedcomKeys.UnsupportedVersion, inner: null, version);
        }

        var info = new GedcomReadInfo(
            encodingName,
            declaredCharset,
            encodingFallbackFrom,
            malformed,
            version,
            head.ChildValue("SOUR"));

        return new GedcomFile(root, info);
    }

    /// <summary>Розбиття на рядки для всіх трьох варіантів кінця рядка (CRLF, LF, CR).</summary>
    private static IEnumerable<string> SplitLines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is not ('\n' or '\r'))
            {
                continue;
            }

            yield return text[start..i];

            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }
}
