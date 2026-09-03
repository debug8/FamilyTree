using System.Text;
using System.Text.RegularExpressions;

namespace FamilyTree.Gedcom;

/// <summary>
/// Результат визначення кодування.
/// </summary>
/// <param name="Text">Декодований вміст файлу.</param>
/// <param name="EncodingName">Фактично застосоване кодування (напр. <c>windows-1251</c>).</param>
/// <param name="Declared">Значення <c>HEAD.CHAR</c> з файлу, або null, якщо тега немає.</param>
/// <param name="FallbackFrom">
/// Оголошене кодування, яке НЕ підійшло і було замінене. Null, коли фолбеку не було.
/// Ненульове значення потрапляє у звіт імпорту як попередження.
/// </param>
public sealed record GedcomDecodeResult(string Text, string EncodingName, string? Declared, string? FallbackFrom);

/// <summary>
/// Визначення кодування вхідного <c>.ged</c> і декодування (T-5.2).
/// Порядок: BOM → <c>HEAD.CHAR</c> → сувора спроба → фолбек на Windows-1251.
/// Фолбек обов'язковий: у реальних файлах <c>HEAD.CHAR</c> часто бреше — програма пише
/// «UTF-8», а байти лишаються в однобайтовій кодовій сторінці.
/// </summary>
public static partial class GedcomEncodingDetector
{
    /// <summary>Кодування-рятівник, коли оголошене не декодується.</summary>
    private const int FallbackCodePage = 1251;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [GeneratedRegex(@"^\s*1\s+CHAR\s+(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex CharsetRegex();

    static GedcomEncodingDetector() =>
        // Однобайтові кодові сторінки (1251/1252) у .NET Core доступні лише через цей провайдер.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Декодує вміст файлу. Кидає <see cref="GedcomException"/> для ANSEL
    /// та коли жодне кодування не дало коректного тексту.
    /// </summary>
    public static GedcomDecodeResult Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // 1. BOM — найнадійніший сигнал, він перебиває будь-який HEAD.CHAR.
        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
        {
            return Finish(StrictUtf8.GetString(bytes, 3, bytes.Length - 3), "utf-8", declared: null, fallbackFrom: null);
        }

        if (StartsWith(bytes, 0xFF, 0xFE))
        {
            return Finish(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "utf-16", null, null);
        }

        if (StartsWith(bytes, 0xFE, 0xFF))
        {
            return Finish(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "utf-16BE", null, null);
        }

        // 1b. UTF-16 без BOM: у тексті з латинських тегів кожен другий байт нульовий.
        if (LooksLikeUtf16(bytes, out var utf16))
        {
            return Finish(utf16.GetString(bytes), utf16.WebName, null, null);
        }

        // 2. HEAD.CHAR. Читаємо префікс як Latin1 — воно однозначно відображає байти
        //    на символи й ніколи не кидає, а теги в усіх цільових кодуваннях ASCII-сумісні.
        var declared = ReadDeclaredCharset(bytes);
        var primary = MapDeclared(declared);

        // 3. Сувора спроба оголошеного (або UTF-8 за замовчуванням).
        if (TryDecodeStrict(bytes, primary, out var text))
        {
            return Finish(text, primary.WebName, declared, fallbackFrom: null);
        }

        // 4. Фолбек. Windows-1251 не має недопустимих байтів, тож не впаде.
        var fallback = Encoding.GetEncoding(FallbackCodePage);
        if (ReferenceEquals(fallback, primary) || fallback.WebName == primary.WebName)
        {
            throw GedcomException.Create(GedcomKeys.BadEncoding, inner: null, declared ?? primary.WebName);
        }

        return Finish(fallback.GetString(bytes), fallback.WebName, declared, fallbackFrom: declared ?? primary.WebName);
    }

    /// <summary>Оголошене кодування з <c>1 CHAR …</c> у заголовку, або null.</summary>
    private static string? ReadDeclaredCharset(byte[] bytes)
    {
        var probeLength = Math.Min(bytes.Length, 4096);
        var probe = Encoding.Latin1.GetString(bytes, 0, probeLength);
        var match = CharsetRegex().Match(probe);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>Оголошене значення → кодування. ANSEL відхиляється одразу.</summary>
    private static Encoding MapDeclared(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return StrictUtf8;
        }

        var key = declared.Trim().ToUpperInvariant();

        if (key is "ANSEL")
        {
            throw GedcomException.Create(GedcomKeys.AnselUnsupported, inner: null, declared);
        }

        return key switch
        {
            "UTF-8" or "UTF8" => StrictUtf8,
            // У 5.5.1 «UNICODE» означає саме UTF-16.
            "UNICODE" or "UTF-16" => Encoding.Unicode,
            "ANSI" or "WINDOWS-1251" or "CP1251" or "IBM WINDOWS-1251" => Encoding.GetEncoding(1251),
            "ASCII" or "IBM WINDOWS" or "WINDOWS-1252" or "CP1252" => Encoding.GetEncoding(1252),
            _ => StrictUtf8,
        };
    }

    private static bool TryDecodeStrict(byte[] bytes, Encoding encoding, out string text)
    {
        try
        {
            // Однобайтові сторінки не мають недопустимих послідовностей — сувора спроба для них
            // завжди успішна, і це правильно: там «неправильних» байтів не буває.
            text = encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static bool LooksLikeUtf16(byte[] bytes, out Encoding encoding)
    {
        encoding = Encoding.Unicode;

        var probe = Math.Min(bytes.Length, 64);
        if (probe < 8)
        {
            return false;
        }

        var zerosAtOdd = 0;
        var zerosAtEven = 0;
        for (var i = 0; i < probe; i++)
        {
            if (bytes[i] != 0)
            {
                continue;
            }

            if (i % 2 == 0)
            {
                zerosAtEven++;
            }
            else
            {
                zerosAtOdd++;
            }
        }

        var threshold = probe / 4;
        if (zerosAtOdd > threshold && zerosAtEven == 0)
        {
            encoding = Encoding.Unicode; // LE: «0 H E A D» -> 30 00 20 00 …
            return true;
        }

        if (zerosAtEven > threshold && zerosAtOdd == 0)
        {
            encoding = Encoding.BigEndianUnicode;
            return true;
        }

        return false;
    }

    private static GedcomDecodeResult Finish(string text, string encodingName, string? declared, string? fallbackFrom) =>
        new(text, encodingName, declared, fallbackFrom);

    private static bool StartsWith(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }
}
