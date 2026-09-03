using FamilyTree.Gedcom;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Gedcom;

/// <summary>Складання пласких рівнів у дерево записів (T-5.2, Частина 1).</summary>
public sealed class GedcomReaderTests
{
    private const string MinimalHead =
        "0 HEAD\n1 SOUR FamilyTree\n1 GEDC\n2 VERS 5.5.1\n2 FORM LINEAGE-LINKED\n1 CHAR UTF-8\n";

    private static GedcomFile Read(string body) => GedcomReader.Read(MinimalHead + body + "0 TRLR\n");

    [Fact]
    public void Builds_nested_tree()
    {
        var file = Read("0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 BIRT\n2 DATE 12 MAR 1950\n2 PLAC Полтава\n");

        var indi = file.Records("INDI").ShouldHaveSingleItem();
        indi.Xref.ShouldBe("I1");
        indi.ChildValue("NAME").ShouldBe("Іван /Коваленко/");
        indi.Path("BIRT", "DATE").ShouldBe("12 MAR 1950");
        indi.Path("BIRT", "PLAC").ShouldBe("Полтава");
    }

    [Fact]
    public void Reads_header_metadata()
    {
        var file = Read(string.Empty);

        file.Info.Version.ShouldBe("5.5.1");
        file.Info.Source.ShouldBe("FamilyTree");
        file.Info.MalformedLines.ShouldBe(0);
    }

    [Fact]
    public void Resolves_pointers_between_records()
    {
        var file = Read("0 @I1@ INDI\n1 FAMS @F1@\n0 @F1@ FAM\n1 HUSB @I1@\n");

        file.Records("INDI").ShouldHaveSingleItem().ChildPointer("FAMS").ShouldBe("F1");
        file.Records("FAM").ShouldHaveSingleItem().ChildPointer("HUSB").ShouldBe("I1");
    }

    [Fact]
    public void Conc_appends_without_separator()
    {
        var file = Read("0 @I1@ INDI\n1 NOTE довгий ряд\n2 CONC ок, продовження\n");

        file.Records("INDI").Single().ChildValue("NOTE").ShouldBe("довгий рядок, продовження");
    }

    [Fact]
    public void Cont_appends_new_line()
    {
        var file = Read("0 @I1@ INDI\n1 NOTE перший\n2 CONT другий\n");

        file.Records("INDI").Single().ChildValue("NOTE").ShouldBe("перший\nдругий");
    }

    [Fact]
    public void Conc_continues_parent_not_previous_sibling()
    {
        // Пастка: CONC на рівні 2 продовжує NOTE (рівень 1), а не сусідній SOUR (рівень 2).
        var file = Read("0 @I1@ INDI\n1 NOTE поча\n2 SOUR @S1@\n2 CONC ток\n");

        var note = file.Records("INDI").Single().Child("NOTE").ShouldNotBeNull();
        note.Value.ShouldBe("початок");
        note.ChildPointer("SOUR").ShouldBe("S1");
    }

    [Fact]
    public void Repeated_tags_are_all_kept()
    {
        var file = Read("0 @F1@ FAM\n1 CHIL @I2@\n1 CHIL @I3@\n1 CHIL @I4@\n");

        file.Records("FAM").Single().ChildrenOf("CHIL")
            .Select(c => c.Pointer)
            .ShouldBe(new string?[] { "I2", "I3", "I4" });
    }

    [Fact]
    public void Sibling_after_deeper_node_returns_to_correct_parent()
    {
        var file = Read("0 @I1@ INDI\n1 BIRT\n2 DATE 1950\n1 DEAT\n2 DATE 2010\n");

        var indi = file.Records("INDI").Single();
        indi.Path("BIRT", "DATE").ShouldBe("1950");
        indi.Path("DEAT", "DATE").ShouldBe("2010");
        indi.Children.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Handles_all_line_endings(string newLine)
    {
        var text = (MinimalHead + "0 @I1@ INDI\n1 SEX M\n0 TRLR\n").Replace("\n", newLine);

        GedcomReader.Read(text).Records("INDI").Single().ChildValue("SEX").ShouldBe("M");
    }

    [Fact]
    public void Malformed_lines_are_counted_not_fatal()
    {
        var file = Read("0 @I1@ INDI\nсміття посеред файлу\n1 SEX M\n\n\nще сміття\n");

        file.Info.MalformedLines.ShouldBe(2);
        file.Records("INDI").Single().ChildValue("SEX").ShouldBe("M");
    }

    [Fact]
    public void Level_jump_is_repaired_and_counted()
    {
        // 0 -> 2 стандартом заборонено; вузол не губиться, дефект рахується.
        var file = Read("0 @I1@ INDI\n2 SEX M\n");

        file.Info.MalformedLines.ShouldBe(1);
        file.Records("INDI").Single().ChildValue("SEX").ShouldBe("M");
    }

    [Fact]
    public void Missing_trailer_is_tolerated()
    {
        var file = GedcomReader.Read(MinimalHead + "0 @I1@ INDI\n1 SEX F\n");

        file.Records("INDI").Single().ChildValue("SEX").ShouldBe("F");
    }

    [Fact]
    public void Rejects_content_without_head()
    {
        var ex = Should.Throw<GedcomException>(() => GedcomReader.Read("0 @I1@ INDI\n1 SEX M\n"));

        ex.MessageKey.ShouldBe(GedcomKeys.NotGedcom);
    }

    [Fact]
    public void Rejects_gedcom_7()
    {
        var head = "0 HEAD\n1 GEDC\n2 VERS 7.0\n0 TRLR\n";

        var ex = Should.Throw<GedcomException>(() => GedcomReader.Read(head));

        ex.MessageKey.ShouldBe(GedcomKeys.UnsupportedVersion);
        ex.Arguments.ShouldContain("7.0");
    }
}
