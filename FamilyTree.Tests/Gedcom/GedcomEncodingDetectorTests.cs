using System.Text;
using FamilyTree.Gedcom;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Gedcom;

/// <summary>Визначення кодування вхідного .ged (T-5.2, Частина 1).</summary>
public sealed class GedcomEncodingDetectorTests
{
    static GedcomEncodingDetectorTests() =>
        // Тест сам будує байти в CP1251, тож провайдер потрібен і тут.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Заголовок із заданим значенням HEAD.CHAR і кирилицею в тілі.</summary>
    private static string Sample(string? charset) =>
        "0 HEAD\n1 GEDC\n2 VERS 5.5.1\n"
        + (charset is null ? string.Empty : $"1 CHAR {charset}\n")
        + "0 @I1@ INDI\n1 NAME Оксана /Шевченко/\n0 TRLR\n";

    private static byte[] Bytes(Encoding encoding, string text, bool bom = false)
    {
        var body = encoding.GetBytes(text);
        if (!bom)
        {
            return body;
        }

        var preamble = encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    [Fact]
    public void Reads_utf8_with_bom()
    {
        var result = GedcomEncodingDetector.Decode(Bytes(new UTF8Encoding(true), Sample("UTF-8"), bom: true));

        result.EncodingName.ShouldBe("utf-8");
        result.FallbackFrom.ShouldBeNull();
        result.Text.ShouldContain("Оксана");
    }

    [Fact]
    public void Reads_utf8_without_bom()
    {
        var result = GedcomEncodingDetector.Decode(Bytes(Encoding.UTF8, Sample("UTF-8")));

        result.EncodingName.ShouldBe("utf-8");
        result.Declared.ShouldBe("UTF-8");
        result.FallbackFrom.ShouldBeNull();
        result.Text.ShouldContain("Оксана");
    }

    [Fact]
    public void Reads_cp1251_declared_as_ansi()
    {
        var result = GedcomEncodingDetector.Decode(Bytes(Encoding.GetEncoding(1251), Sample("ANSI")));

        result.EncodingName.ShouldBe("windows-1251");
        result.FallbackFrom.ShouldBeNull();
        result.Text.ShouldContain("Оксана");
    }

    [Fact]
    public void Falls_back_when_header_lies_about_utf8()
    {
        // Найчастіший реальний випадок: програма пише «UTF-8», а байти в CP1251.
        var result = GedcomEncodingDetector.Decode(Bytes(Encoding.GetEncoding(1251), Sample("UTF-8")));

        result.EncodingName.ShouldBe("windows-1251");
        result.FallbackFrom.ShouldBe("UTF-8");
        result.Text.ShouldContain("Оксана");
    }

    [Fact]
    public void Falls_back_when_charset_is_absent()
    {
        var result = GedcomEncodingDetector.Decode(Bytes(Encoding.GetEncoding(1251), Sample(charset: null)));

        result.Declared.ShouldBeNull();
        result.EncodingName.ShouldBe("windows-1251");
        result.FallbackFrom.ShouldBe("utf-8");
        result.Text.ShouldContain("Оксана");
    }

    [Fact]
    public void Reads_utf16_with_bom()
    {
        var result = GedcomEncodingDetector.Decode(Bytes(Encoding.Unicode, Sample("UNICODE"), bom: true));

        result.EncodingName.ShouldBe("utf-16");
        result.Text.ShouldContain("Оксана");
    }

    [Fact]
    public void Reads_utf16_without_bom()
    {
        var result = GedcomEncodingDetector.Decode(Bytes(Encoding.Unicode, Sample("UNICODE")));

        result.Text.ShouldContain("Оксана");
    }

    [Fact]
    public void Rejects_ansel()
    {
        var ex = Should.Throw<GedcomException>(
            () => GedcomEncodingDetector.Decode(Bytes(Encoding.ASCII, Sample("ANSEL"))));

        ex.MessageKey.ShouldBe(GedcomKeys.AnselUnsupported);
    }

    [Fact]
    public void Reader_reports_encoding_fallback_in_info()
    {
        var file = GedcomReader.Read(Bytes(Encoding.GetEncoding(1251), Sample("UTF-8")));

        file.Info.EncodingName.ShouldBe("windows-1251");
        file.Info.EncodingFallbackFrom.ShouldBe("UTF-8");
        file.Records("INDI").ShouldHaveSingleItem().ChildValue("NAME").ShouldBe("Оксана /Шевченко/");
    }
}
