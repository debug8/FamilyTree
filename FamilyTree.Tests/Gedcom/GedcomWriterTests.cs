using System.Text;
using FamilyTree.Gedcom;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Gedcom;

/// <summary>Запис дерева в текст GEDCOM (T-5.2, Частина 2).</summary>
public sealed class GedcomWriterTests
{
    private static GedcomNode Tree(params GedcomNode[] records)
    {
        var root = new GedcomNode(string.Empty);
        foreach (var record in records)
        {
            root.Add(record);
        }

        return root;
    }

    private static GedcomNode Node(string tag, string? value = null, string? xref = null, string? pointer = null)
        => new(tag, xref, value, pointer);

    [Fact]
    public void Writes_levels_and_xrefs()
    {
        var indi = Node("INDI", xref: "I1");
        var birt = Node("BIRT");
        birt.Add(Node("DATE", "12 MAR 1950"));
        indi.Add(birt);

        GedcomWriter.Write(Tree(indi))
            .ShouldBe("0 @I1@ INDI\r\n1 BIRT\r\n2 DATE 12 MAR 1950\r\n");
    }

    [Fact]
    public void Writes_pointer_without_escaping()
    {
        GedcomWriter.Write(Tree(Node("FAMS", pointer: "F1")))
            .ShouldBe("0 FAMS @F1@\r\n");
    }

    [Fact]
    public void Escapes_literal_at_sign_in_value()
    {
        GedcomWriter.Write(Tree(Node("NOTE", "пошта a@b.com")))
            .ShouldBe("0 NOTE пошта a@@b.com\r\n");
    }

    [Fact]
    public void Splits_line_breaks_into_cont()
    {
        var text = GedcomWriter.Write(Tree(Node("NOTE", "перший\nдругий\nтретій")));

        text.ShouldBe("0 NOTE перший\r\n1 CONT другий\r\n1 CONT третій\r\n");
    }

    [Fact]
    public void Splits_long_value_into_conc()
    {
        var value = new string('я', 400);

        var text = GedcomWriter.Write(Tree(Node("NOTE", value)));

        text.ShouldContain("1 CONC ");
        foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            Encoding.UTF8.GetByteCount(line).ShouldBeLessThanOrEqualTo(253);
        }
    }

    [Fact]
    public void Long_value_survives_a_round_trip_through_the_reader()
    {
        // Найкращий доказ коректності CONC — прочитати назад те, що записали.
        var value = string.Join(" ", Enumerable.Range(0, 60).Select(i => $"слово{i}"));

        var head = Node("HEAD");
        var note = Node("NOTE", value);
        head.Add(note);

        var text = GedcomWriter.Write(Tree(head, Node("TRLR")));

        GedcomReader.Read(text).Root.Child("HEAD")!.ChildValue("NOTE").ShouldBe(value);
    }

    [Fact]
    public void Multiline_value_survives_a_round_trip_through_the_reader()
    {
        var value = "перший рядок\nдругий рядок\nтретій рядок";

        var head = Node("HEAD");
        head.Add(Node("NOTE", value));

        var text = GedcomWriter.Write(Tree(head, Node("TRLR")));

        GedcomReader.Read(text).Root.Child("HEAD")!.ChildValue("NOTE").ShouldBe(value);
    }

    [Fact]
    public void Escaped_at_sign_survives_a_round_trip_even_when_split()
    {
        // «@@» не можна розірвати між CONC-рядками — інакше при читанні
        // вона розпадеться на дві окремі «собачки».
        var value = new string('ю', 240) + "@@@" + new string('я', 240);

        var head = Node("HEAD");
        head.Add(Node("NOTE", value));

        var text = GedcomWriter.Write(Tree(head, Node("TRLR")));

        GedcomReader.Read(text).Root.Child("HEAD")!.ChildValue("NOTE").ShouldBe(value);
    }

    [Fact]
    public void Writes_utf8_with_bom()
    {
        var bytes = GedcomWriter.WriteBytes(Tree(Node("HEAD"), Node("TRLR")));

        bytes[0].ShouldBe((byte)0xEF);
        bytes[1].ShouldBe((byte)0xBB);
        bytes[2].ShouldBe((byte)0xBF);
    }
}
