using FamilyTree.Domain;
using FamilyTree.Gedcom;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Gedcom;

/// <summary>Складання записів HEAD/INDI/FAM/TRLR (T-5.2, Частина 2).</summary>
public sealed class GedcomExporterTests
{
    private static readonly DateTime Stamp = new(2026, 9, 3, 14, 5, 0, DateTimeKind.Utc);

    private static GedcomFile Export(GedcomTestDocument doc) =>
        GedcomReader.Read(GedcomExporter.ExportText(doc.Document, "0.9.4", Stamp));

    private static GedcomNode Individual(GedcomFile file, string xref) =>
        file.Records("INDI").Single(r => r.Xref == xref);

    [Fact]
    public void Header_declares_the_supported_dialect()
    {
        var file = Export(new GedcomTestDocument());
        var head = file.Root.Child("HEAD").ShouldNotBeNull();

        head.Path("GEDC", "VERS").ShouldBe("5.5.1");
        head.Path("GEDC", "FORM").ShouldBe("LINEAGE-LINKED");
        head.ChildValue("CHAR").ShouldBe("UTF-8");
        head.Path("SOUR", "VERS").ShouldBe("0.9.4");

        // SUBM формально обов'язковий у 5.5.1 — без нього Gramps лається.
        head.ChildPointer("SUBM").ShouldBe("SUBM1");
        file.Records("SUBM").ShouldHaveSingleItem();
        file.Root.Child("TRLR").ShouldNotBeNull();
    }

    [Fact]
    public void Name_carries_patronymic_as_second_given_word()
    {
        var doc = new GedcomTestDocument();
        doc.Add("Коваленко", "Іван", Gender.Male, middleName: "Петрович");

        var name = Individual(Export(doc), "I1").Child("NAME").ShouldNotBeNull();

        name.Value.ShouldBe("Іван Петрович /Коваленко/");
        name.ChildValue("GIVN").ShouldBe("Іван Петрович");
        name.ChildValue("SURN").ShouldBe("Коваленко");
        name.Child("_MARNM").ShouldBeNull();
    }

    [Fact]
    public void Maiden_name_becomes_the_primary_surname()
    {
        var doc = new GedcomTestDocument();
        doc.Add("Коваленко", "Марія", Gender.Female, maidenName: "Шевченко");

        var name = Individual(Export(doc), "I1").Child("NAME").ShouldNotBeNull();

        name.Value.ShouldBe("Марія /Шевченко/");
        name.ChildValue("SURN").ShouldBe("Шевченко");
        name.ChildValue("_MARNM").ShouldBe("Коваленко");
    }

    [Theory]
    [InlineData(Gender.Male, "M")]
    [InlineData(Gender.Female, "F")]
    [InlineData(Gender.Unknown, "U")]
    public void Sex_is_written_for_every_gender(Gender gender, string expected)
    {
        var doc = new GedcomTestDocument();
        doc.Add("Тест", "Особа", gender);

        Individual(Export(doc), "I1").ChildValue("SEX").ShouldBe(expected);
    }

    [Fact]
    public void Birth_and_death_events()
    {
        var doc = new GedcomTestDocument();
        doc.Add(
            "Коваленко", "Іван", Gender.Male,
            birth: FamilyDate.Exact(new DateOnly(1950, 3, 12)),
            death: FamilyDate.Approximate(DateApproximation.About, new DatePoint { Year = 2010 }),
            birthPlace: "Полтава");

        var indi = Individual(Export(doc), "I1");

        indi.Path("BIRT", "DATE").ShouldBe("12 MAR 1950");
        indi.Path("BIRT", "PLAC").ShouldBe("Полтава");
        indi.Path("DEAT", "DATE").ShouldBe("ABT 2010");
    }

    [Fact]
    public void Death_place_and_note_are_written_as_subtags()
    {
        var doc = new GedcomTestDocument();
        doc.Add(
            "Коваленко", "Іван", Gender.Male,
            death: FamilyDate.Exact(new DateOnly(1939, 5, 1)),
            deathPlace: "Полтава",
            deathNote: "помер у 40 років");

        var indi = Individual(Export(doc), "I1");

        indi.Path("DEAT", "DATE").ShouldBe("1 MAY 1939");
        indi.Path("DEAT", "PLAC").ShouldBe("Полтава");
        indi.Path("DEAT", "NOTE").ShouldBe("помер у 40 років");

        // Маркер «Y» тут зайвий: подія має підтеги, і «1 DEAT Y» із ними був би суперечливим.
        indi.Child("DEAT")!.Value.ShouldBeNull();
    }

    [Fact]
    public void Death_place_alone_is_enough_to_write_the_event()
    {
        // Дати немає, але місце є — подія мусить доїхати підтегом, а не маркером «Y».
        var doc = new GedcomTestDocument();
        doc.Add("Коваленко", "Іван", Gender.Male, deathPlace: "Полтава");

        var indi = Individual(Export(doc), "I1");

        indi.Path("DEAT", "PLAC").ShouldBe("Полтава");
        indi.Child("DEAT")!.Value.ShouldBeNull();
    }

    [Fact]
    public void Birth_note_is_written_separately_from_person_notes()
    {
        var doc = new GedcomTestDocument();
        doc.Add(
            "Коваленко", "Іван", Gender.Male,
            birth: FamilyDate.Exact(new DateOnly(1900, 1, 1)),
            birthNote: "за метричною книгою",
            notes: "коваль");

        var indi = Individual(Export(doc), "I1");

        indi.Path("BIRT", "NOTE").ShouldBe("за метричною книгою");
        indi.ChildValue("NOTE").ShouldBe("коваль");
    }

    [Fact]
    public void Deceased_without_a_date_is_written_as_DEAT_Y()
    {
        // Стан «помер, дата невідома»: подія мусить дійти до файлу, інакше
        // стороння програма побачить особу живою. «Y» — той самий маркер, що й у MARR/DIV.
        var doc = new GedcomTestDocument();
        doc.Add("Коваленко", "Іван", Gender.Male, deceased: true);

        var indi = Individual(Export(doc), "I1");

        indi.Child("DEAT").ShouldNotBeNull();
        indi.Child("DEAT")!.Value.ShouldBe("Y");
        indi.Path("DEAT", "DATE").ShouldBeNull();
    }

    [Fact]
    public void Living_person_has_no_death_record()
    {
        var doc = new GedcomTestDocument();
        doc.Add("Коваленко", "Іван", Gender.Male, birth: FamilyDate.Exact(new DateOnly(1990, 1, 1)));

        var indi = Individual(Export(doc), "I1");

        indi.Child("DEAT").ShouldBeNull();
    }

    [Fact]
    public void Person_without_dates_has_no_event_records()
    {
        var doc = new GedcomTestDocument();
        doc.Add("Коваленко", "Іван", Gender.Male);

        var indi = Individual(Export(doc), "I1");

        indi.Child("BIRT").ShouldBeNull();
        indi.Child("DEAT").ShouldBeNull();
    }

    [Fact]
    public void Notes_and_uid_are_written()
    {
        var doc = new GedcomTestDocument();
        var person = doc.Add("Коваленко", "Іван", Gender.Male, notes: "коваль у третьому поколінні");

        var indi = Individual(Export(doc), "I1");

        indi.ChildValue("NOTE").ShouldBe("коваль у третьому поколінні");
        indi.ChildValue("_UID").ShouldBe(person.Id.ToString("D"));
    }

    [Fact]
    public void Family_links_husband_wife_and_children()
    {
        var doc = new GedcomTestDocument();
        var father = doc.Add("Аденко", "Іван", Gender.Male);
        var mother = doc.Add("Аденко", "Марія", Gender.Female);
        var child = doc.Add("Аденко", "Оксана", Gender.Female);
        doc.Parent(father, child);
        doc.Parent(mother, child);
        doc.Marry(father, mother, FamilyDate.Exact(new DateOnly(1975, 6, 1)));

        var file = Export(doc);
        var fam = file.Records("FAM").ShouldHaveSingleItem();

        fam.ChildPointer("HUSB").ShouldBe(file.Records("INDI").Single(r => r.ChildValue("_UID") == father.Id.ToString("D")).Xref);
        fam.ChildPointer("WIFE").ShouldNotBeNull();
        fam.ChildrenOf("CHIL").ShouldHaveSingleItem();
        fam.Path("MARR", "DATE").ShouldBe("1 JUN 1975");

        // Зустрічні посилання з боку особи.
        Individual(file, fam.ChildPointer("HUSB")!).ChildPointer("FAMS").ShouldBe(fam.Xref);
        Individual(file, fam.ChildrenOf("CHIL").Single().Pointer!).ChildPointer("FAMC").ShouldBe(fam.Xref);
    }

    [Fact]
    public void Marriage_without_a_date_is_written_as_Y()
    {
        var doc = new GedcomTestDocument();
        var husband = doc.Add("Мороз", "Петро", Gender.Male);
        var wife = doc.Add("Мороз", "Ганна", Gender.Female);
        doc.Marry(husband, wife);

        // Порожній MARR програми ігнорують, тому подія позначається «Y».
        Export(doc).Records("FAM").Single().ChildValue("MARR").ShouldBe("Y");
    }

    [Fact]
    public void Divorce_without_a_date_is_written_as_Y()
    {
        var doc = new GedcomTestDocument();
        var husband = doc.Add("Мороз", "Петро", Gender.Male);
        var wife = doc.Add("Мороз", "Ганна", Gender.Female);
        doc.Marry(husband, wife, FamilyDate.Exact(new DateOnly(1990, 5, 5)), divorced: true);

        var fam = Export(doc).Records("FAM").Single();

        fam.Path("MARR", "DATE").ShouldBe("5 MAY 1990");
        fam.ChildValue("DIV").ShouldBe("Y");
    }

    [Fact]
    public void Adoption_is_marked_with_pedi()
    {
        var doc = new GedcomTestDocument();
        var bioMother = doc.Add("Аденко", "Ніна", Gender.Female);
        var adoptiveMother = doc.Add("Беденко", "Ольга", Gender.Female);
        var child = doc.Add("Беденко", "Марко", Gender.Male);
        doc.Parent(bioMother, child);
        doc.Parent(adoptiveMother, child, ParentRole.Adoptive);

        var file = Export(doc);
        var indi = file.Records("INDI").Single(r => r.ChildValue("_UID") == child.Id.ToString("D"));

        var famc = indi.ChildrenOf("FAMC").ToList();
        famc.Count.ShouldBe(2);

        // Біологічна родина не отримує PEDI — «birth» і так типовий випадок.
        famc.Count(f => f.Child("PEDI") is null).ShouldBe(1);
        famc.Single(f => f.Child("PEDI") is not null).ChildValue("PEDI").ShouldBe("adopted");
    }

    [Fact]
    public void Export_is_byte_for_byte_repeatable()
    {
        var doc = new GedcomTestDocument();
        var father = doc.Add("Аденко", "Іван", Gender.Male);
        var mother = doc.Add("Беденко", "Марія", Gender.Female, maidenName: "Веденко");
        var child = doc.Add("Аденко", "Оксана", Gender.Female, birth: FamilyDate.Exact(new DateOnly(2001, 2, 3)));
        doc.Parent(father, child);
        doc.Parent(mother, child);
        doc.Marry(father, mother);

        var first = GedcomExporter.ExportText(doc.Document, "0.9.4", Stamp);
        var second = GedcomExporter.ExportText(doc.Document, "0.9.4", Stamp);

        second.ShouldBe(first);
    }

    [Fact]
    public void Exported_file_reads_back_without_defects()
    {
        var doc = new GedcomTestDocument();
        var father = doc.Add("Аденко", "Іван", Gender.Male, middleName: "Петрович", notes: "нотатка з @ та кількома\nрядками");
        var mother = doc.Add("Аденко", "Марія", Gender.Female, maidenName: "Шевченко");
        var child = doc.Add("Аденко", "Оксана", Gender.Female);
        doc.Parent(father, child);
        doc.Parent(mother, child);
        doc.Marry(father, mother, FamilyDate.Exact(new DateOnly(1975, 6, 1)));

        var bytes = GedcomExporter.Export(doc.Document, "0.9.4", Stamp);
        var file = GedcomReader.Read(bytes);

        file.Info.MalformedLines.ShouldBe(0);
        file.Info.EncodingName.ShouldBe("utf-8");
        file.Info.Version.ShouldBe("5.5.1");
        file.Records("INDI").Count().ShouldBe(3);
        file.Records("FAM").Count().ShouldBe(1);

        file.Records("INDI").Single(r => r.ChildValue("_UID") == father.Id.ToString("D"))
            .ChildValue("NOTE").ShouldBe("нотатка з @ та кількома\nрядками");
    }
}
