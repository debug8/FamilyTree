using FamilyTree.Gedcom;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Gedcom;

/// <summary>Розбір одного рядка GEDCOM (T-5.2, Частина 1).</summary>
public sealed class GedcomLineTests
{
    [Fact]
    public void Parses_tag_without_value()
    {
        var line = GedcomLine.Parse("1 BIRT").ShouldNotBeNull();

        line.Level.ShouldBe(1);
        line.Tag.ShouldBe("BIRT");
        line.Xref.ShouldBeNull();
        line.Value.ShouldBeNull();
        line.Pointer.ShouldBeNull();
    }

    [Fact]
    public void Parses_record_header_with_xref()
    {
        var line = GedcomLine.Parse("0 @I1@ INDI").ShouldNotBeNull();

        line.Level.ShouldBe(0);
        line.Xref.ShouldBe("I1");
        line.Tag.ShouldBe("INDI");
        line.Value.ShouldBeNull();
    }

    [Fact]
    public void Parses_value()
    {
        var line = GedcomLine.Parse("2 DATE 12 MAR 1950").ShouldNotBeNull();

        line.Tag.ShouldBe("DATE");
        line.Value.ShouldBe("12 MAR 1950");
    }

    [Fact]
    public void Parses_pointer_value()
    {
        var line = GedcomLine.Parse("1 FAMS @F1@").ShouldNotBeNull();

        line.Pointer.ShouldBe("F1");
        line.Value.ShouldBeNull();
        line.IsPointer.ShouldBeTrue();
    }

    [Fact]
    public void Unescapes_doubled_at_sign()
    {
        // За 5.5.1 літеральна «собачка» у тексті подвоюється.
        GedcomLine.Parse("1 NOTE пошта a@@b.com")!.Value.ShouldBe("пошта a@b.com");
    }

    [Fact]
    public void Keeps_only_first_space_as_delimiter()
    {
        // Другий пробіл уже належить значенню — інакше зіпсуємо склеювання CONC.
        GedcomLine.Parse("1 NOTE  з відступом")!.Value.ShouldBe(" з відступом");
    }

    [Fact]
    public void Uppercases_tag_and_tolerates_leading_space()
    {
        var line = GedcomLine.Parse("   1 birt").ShouldNotBeNull();

        line.Level.ShouldBe(1);
        line.Tag.ShouldBe("BIRT");
    }

    [Fact]
    public void Accepts_custom_underscore_tag()
    {
        GedcomLine.Parse("1 _UID 0f8fad5b-d9cb-469f-a165-70867728950e")!.Tag.ShouldBe("_UID");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("це не GEDCOM")]
    [InlineData("1")]
    [InlineData("1 ")]
    [InlineData("1 @I1@")]
    [InlineData("-1 INDI")]
    [InlineData("1 BAD-TAG значення")]
    public void Returns_null_for_unparsable_lines(string raw) =>
        GedcomLine.Parse(raw).ShouldBeNull();
}
