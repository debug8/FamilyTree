using FamilyTree.Domain;
using FamilyTree.Gedcom;
using FamilyTree.Storage;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Gedcom;

/// <summary>Читання GEDCOM у документ родини (T-5.2, Частина 3).</summary>
public sealed class GedcomImporterTests
{
    private const string Head =
        "0 HEAD\n1 SOUR ЧужаПрограма\n1 GEDC\n2 VERS 5.5.1\n2 FORM LINEAGE-LINKED\n1 CHAR UTF-8\n";

    private static FamilyDocument Import(string body, out GedcomImportReport report) =>
        GedcomImporter.Import(GedcomReader.Read(Head + body + "0 TRLR\n"), out report);

    private static FamilyDocument Import(string body) => Import(body, out _);

    [Fact]
    public void Reads_person_fields()
    {
        var doc = Import(
            "0 @I1@ INDI\n" +
            "1 NAME Іван Петрович /Коваленко/\n" +
            "2 GIVN Іван Петрович\n" +
            "2 SURN Коваленко\n" +
            "1 SEX M\n" +
            "1 BIRT\n2 DATE 12 MAR 1950\n2 PLAC Полтава\n" +
            "1 DEAT\n2 DATE ABT 2010\n" +
            "1 NOTE коваль\n");

        var person = doc.Persons.ShouldHaveSingleItem();

        person.LastName.ShouldBe("Коваленко");
        person.FirstName.ShouldBe("Іван");
        person.MiddleName.ShouldBe("Петрович");
        person.Gender.ShouldBe(Gender.Male);
        (person.Birth?.Date).ShouldBe(FamilyDate.Exact(new DateOnly(1950, 3, 12)));
        (person.Birth?.Place).ShouldBe("Полтава");
        person.Death!.Date!.Approximation.ShouldBe(DateApproximation.About);
        person.Notes.ShouldBe("коваль");
    }

    [Fact]
    public void Takes_surname_from_slashes_when_subtags_are_absent()
    {
        var doc = Import("0 @I1@ INDI\n1 NAME Оксана Іванівна /Шевченко/\n1 SEX F\n");

        var person = doc.Persons.Single();

        person.LastName.ShouldBe("Шевченко");
        person.FirstName.ShouldBe("Оксана");
        person.MiddleName.ShouldBe("Іванівна");
    }

    [Fact]
    public void Married_surname_moves_the_maiden_one_into_its_own_field()
    {
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Марія /Шевченко/\n2 SURN Шевченко\n2 GIVN Марія\n2 _MARNM Коваленко\n1 SEX F\n");

        var person = doc.Persons.Single();

        person.LastName.ShouldBe("Коваленко");
        person.MaidenName.ShouldBe("Шевченко");
    }

    [Fact]
    public void Missing_name_becomes_a_question_mark_and_is_counted()
    {
        var doc = Import("0 @I1@ INDI\n1 SEX M\n", out var report);

        var person = doc.Persons.Single();
        person.LastName.ShouldBe("?");
        person.FirstName.ShouldBe("?");
        report.UnnamedPersons.ShouldBe(1);
    }

    [Fact]
    public void Death_without_a_date_marks_person_deceased()
    {
        // «1 DEAT» без DATE — стан «помер, дата невідома»; його тримає Person.Deceased.
        var doc = Import("0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 DEAT\n", out var report);

        var person = doc.Persons.Single();

        person.Deceased.ShouldBeTrue();
        (person.Death?.Date).ShouldBeNull();
        person.IsAlive.ShouldBeFalse();
        report.DeathsWithoutDate.ShouldBe(1);
    }

    [Fact]
    public void Death_marker_Y_marks_person_deceased()
    {
        // «1 DEAT Y» — та сама подія, лише записана явним маркером.
        var doc = Import("0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 DEAT Y\n");

        doc.Persons.Single().IsAlive.ShouldBeFalse();
    }

    [Fact]
    public void Death_with_a_date_also_sets_the_flag()
    {
        var doc = Import("0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 DEAT\n2 DATE 1939\n", out var report);

        var person = doc.Persons.Single();

        person.Deceased.ShouldBeTrue();
        (person.Death?.Date).ShouldNotBeNull();
        report.DeathsWithoutDate.ShouldBe(0);
    }

    [Fact]
    public void Person_without_death_record_stays_alive()
    {
        var doc = Import("0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 BIRT\n2 DATE 1990\n");

        doc.Persons.Single().IsAlive.ShouldBeTrue();
    }

    [Fact]
    public void Family_creates_parent_links_for_both_parents()
    {
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 SEX M\n" +
            "0 @I2@ INDI\n1 NAME Марія /Коваленко/\n1 SEX F\n" +
            "0 @I3@ INDI\n1 NAME Оксана /Коваленко/\n1 SEX F\n" +
            "0 @F1@ FAM\n1 HUSB @I1@\n1 WIFE @I2@\n1 CHIL @I3@\n");

        doc.ParentChildLinks.Count.ShouldBe(2);
        doc.ParentChildLinks.ShouldAllBe(l => l.ParentRole == ParentRole.Biological);
        doc.SpouseLinks.ShouldBeEmpty();
    }

    [Fact]
    public void Pedi_on_the_child_sets_the_role()
    {
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Степан /Беденко/\n1 SEX M\n" +
            "0 @I2@ INDI\n1 NAME Марко /Беденко/\n1 SEX M\n1 FAMC @F1@\n2 PEDI adopted\n" +
            "0 @F1@ FAM\n1 HUSB @I1@\n1 CHIL @I2@\n");

        doc.ParentChildLinks.ShouldHaveSingleItem().ParentRole.ShouldBe(ParentRole.Adoptive);
    }

    [Fact]
    public void Per_parent_relation_tags_win_over_pedi()
    {
        // _FREL/_MREL — єдине, що розрізняє роль батька й матері окремо.
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Батько /Тест/\n1 SEX M\n" +
            "0 @I2@ INDI\n1 NAME Мати /Тест/\n1 SEX F\n" +
            "0 @I3@ INDI\n1 NAME Дитя /Тест/\n1 SEX M\n1 FAMC @F1@\n2 PEDI adopted\n" +
            "0 @F1@ FAM\n1 HUSB @I1@\n1 WIFE @I2@\n1 CHIL @I3@\n2 _FREL Natural\n2 _MREL Step\n");

        var father = doc.Persons.Single(p => p.FirstName == "Батько");
        var mother = doc.Persons.Single(p => p.FirstName == "Мати");

        doc.ParentChildLinks.Single(l => l.ParentId == father.Id).ParentRole.ShouldBe(ParentRole.Biological);
        doc.ParentChildLinks.Single(l => l.ParentId == mother.Id).ParentRole.ShouldBe(ParentRole.Step);
    }

    [Fact]
    public void Marriage_and_divorce_dates_are_read()
    {
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Петро /Мороз/\n1 SEX M\n" +
            "0 @I2@ INDI\n1 NAME Ганна /Мороз/\n1 SEX F\n" +
            "0 @F1@ FAM\n1 HUSB @I1@\n1 WIFE @I2@\n1 MARR\n2 DATE 5 MAY 1990\n1 DIV\n2 DATE 1 JUN 1999\n");

        var link = doc.SpouseLinks.ShouldHaveSingleItem();

        link.MarriageDate.ShouldBe(FamilyDate.Exact(new DateOnly(1990, 5, 5)));
        link.DivorceDate.ShouldBe(FamilyDate.Exact(new DateOnly(1999, 6, 1)));
        link.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Divorce_without_a_date_sets_the_flag()
    {
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Петро /Мороз/\n1 SEX M\n" +
            "0 @I2@ INDI\n1 NAME Ганна /Мороз/\n1 SEX F\n" +
            "0 @F1@ FAM\n1 HUSB @I1@\n1 WIFE @I2@\n1 MARR Y\n1 DIV Y\n");

        var link = doc.SpouseLinks.ShouldHaveSingleItem();

        link.MarriageDate.ShouldBeNull();
        link.DivorceDate.ShouldBeNull();
        link.Divorced.ShouldBeTrue();
        link.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Uid_restores_the_original_identity()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        var doc = Import($"0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 _UID {id:D}\n");

        doc.Persons.Single().Id.ShouldBe(id);
    }

    [Fact]
    public void Duplicate_uid_does_not_collide()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        var doc = Import(
            $"0 @I1@ INDI\n1 NAME Перший /Тест/\n1 _UID {id:D}\n" +
            $"0 @I2@ INDI\n1 NAME Другий /Тест/\n1 _UID {id:D}\n");

        doc.Persons.Count.ShouldBe(2);
        doc.Persons.Select(p => p.Id).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void Unknown_tags_are_counted_not_fatal()
    {
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 OCCU коваль\n1 RESI Полтава\n1 BAPM\n2 DATE 1950\n",
            out var report);

        doc.Persons.ShouldHaveSingleItem();

        // OCCU і RESI профіль тепер споживає — вони стають життєвими фактами особи,
        // тож у пропущених їх більше немає. Незнайомий BAPM — так само лише запис у звіті,
        // а не помилка: сенс тесту саме в цьому.
        report.SkippedTags.Keys.ShouldNotContain("OCCU");
        report.SkippedTags.Keys.ShouldNotContain("RESI");
        report.SkippedTags.Keys.ShouldContain("BAPM");
        report.HasWarnings.ShouldBeTrue();
    }

    [Fact]
    public void Reads_place_and_note_of_the_death_event()
    {
        // Той самий запис, з якого почалася задача: обставини й причина смерті лежать
        // у DEAT.NOTE прозою (а не в передбаченому стандартом CAUS), а CONT їх продовжує.
        var doc = Import(
            "0 @I1@ INDI\n" +
            "1 NAME Іван /Коваленко/\n" +
            "1 DEAT\n" +
            "2 DATE 1939\n" +
            "2 PLAC Полтава\n" +
            "2 NOTE помер у 40 років\n" +
            "3 CONT Причина смерті: запалення легень\n",
            out var report);

        var person = doc.Persons.ShouldHaveSingleItem();

        (person.Death?.Date).ShouldNotBeNull();
        (person.Death?.Place).ShouldBe("Полтава");
        (person.Death?.Note).ShouldBe("помер у 40 років\nПричина смерті: запалення легень");

        // Усе спожито — у звіті про пропущені теги порожньо.
        report.SkippedTags.ShouldBeEmpty();
    }

    [Fact]
    public void Reads_note_of_the_birth_event_separately_from_person_notes()
    {
        // BIRT.NOTE та INDI.NOTE — різні теги й різні поля: злиття їх в одне означало б,
        // що при зворотному експорті текст переїде в чужий тег.
        var doc = Import(
            "0 @I1@ INDI\n" +
            "1 NAME Іван /Коваленко/\n" +
            "1 BIRT\n2 DATE 1900\n2 PLAC Полтава\n2 NOTE за метричною книгою\n" +
            "1 NOTE коваль у третьому поколінні\n",
            out var report);

        var person = doc.Persons.ShouldHaveSingleItem();

        (person.Birth?.Place).ShouldBe("Полтава");
        (person.Birth?.Note).ShouldBe("за метричною книгою");
        person.Notes.ShouldBe("коваль у третьому поколінні");
        report.SkippedTags.ShouldBeEmpty();
    }

    [Fact]
    public void Reads_marriage_place()
    {
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 SEX M\n" +
            "0 @I2@ INDI\n1 NAME Марія /Коваленко/\n1 SEX F\n" +
            "0 @F1@ FAM\n1 HUSB @I1@\n1 WIFE @I2@\n1 MARR\n2 DATE 1950\n2 PLAC Полтава\n",
            out var report);

        doc.SpouseLinks.ShouldHaveSingleItem().MarriagePlace.ShouldBe("Полтава");
        report.SkippedTags.ShouldBeEmpty();
    }

    [Fact]
    public void Divorce_place_stays_outside_the_profile()
    {
        // Свідоме рішення: DIV.PLAC у реальних файлах не трапляється, поля в моделі немає,
        // тож він мусить чесно потрапляти у звіт, а не зникати мовчки.
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 SEX M\n" +
            "0 @I2@ INDI\n1 NAME Марія /Коваленко/\n1 SEX F\n" +
            "0 @F1@ FAM\n1 HUSB @I1@\n1 WIFE @I2@\n1 DIV\n2 DATE 1960\n2 PLAC Полтава\n",
            out var report);

        doc.SpouseLinks.ShouldHaveSingleItem();
        report.SkippedTags.Keys.ShouldContain("DIV.PLAC");
    }

    [Fact]
    public void Same_tag_under_a_consumed_parent_is_not_reported()
    {
        // Зворотний бік тієї самої монети: BIRT.PLAC читається, і шлях мусить це бачити.
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 BIRT\n2 DATE 1900\n2 PLAC Полтава\n1 NOTE коваль\n",
            out var report);

        (doc.Persons.ShouldHaveSingleItem().Birth?.Place).ShouldBe("Полтава");
        report.SkippedTags.ShouldBeEmpty();
    }

    [Fact]
    public void Subtags_of_an_unknown_tag_are_not_listed_separately()
    {
        // У незнайомий тег обхід не спускається: втрачено один запис BAPM, а не три
        // окремі теги. Інакше п'ятірка найчастіших у звіті заповнювалася б сміттям.
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 BAPM\n2 DATE 1900\n2 PLAC Полтава\n2 AGE 1y\n",
            out var report);

        doc.Persons.ShouldHaveSingleItem();

        report.SkippedTags.ShouldHaveSingleItem().Key.ShouldBe("BAPM");
        report.SkippedTags["BAPM"].ShouldBe(1);
    }

    [Fact]
    public void Skipped_tags_are_counted_per_occurrence()
    {
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Перший /Тест/\n1 BURI\n2 PLAC Полтава\n" +
            "0 @I2@ INDI\n1 NAME Другий /Тест/\n1 BURI\n2 PLAC Київ\n",
            out var report);

        doc.Persons.Count.ShouldBe(2);
        report.SkippedTags["BURI"].ShouldBe(2);
    }

    [Fact]
    public void Family_subtags_outside_the_profile_are_reported_too()
    {
        // Записи FAM скануються нарівні з INDI, і шлях рахується від самого FAM.
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 SEX M\n" +
            "0 @I2@ INDI\n1 NAME Марія /Коваленко/\n1 SEX F\n" +
            "0 @F1@ FAM\n1 HUSB @I1@\n1 WIFE @I2@\n1 MARR\n2 DATE 1950\n2 AGNC Парафія\n",
            out var report);

        doc.SpouseLinks.ShouldHaveSingleItem();
        report.SkippedTags.Keys.ShouldContain("MARR.AGNC");
    }

    [Fact]
    public void Reads_all_occupations_and_residences_in_file_order()
    {
        var doc = Import(
            "0 @I1@ INDI\n" +
            "1 NAME Іван /Коваленко/\n" +
            "1 OCCU коваль\n2 DATE FROM 1970 TO 1985\n2 PLAC Полтава\n" +
            "1 RESI\n2 PLAC Полтава\n2 DATE 1970\n" +
            "1 OCCU бригадир\n" +
            "1 RESI\n2 ADDR\n3 CITY Чернівці\n3 CTRY Україна\n");

        var person = doc.Persons.ShouldHaveSingleItem();

        // Обидва теги повторювані, і порядок із файлу зберігається:
        // послідовність професій і переїздів потім нізвідки не відновити.
        person.Facts.Count.ShouldBe(4);

        person.Facts[0].Kind.ShouldBe(PersonFactKind.Occupation);
        person.Facts[0].Value.ShouldBe("коваль");
        person.Facts[0].Place.ShouldBe("Полтава");
        person.Facts[0].Date.ShouldNotBeNull();

        person.Facts[1].Kind.ShouldBe(PersonFactKind.Residence);
        person.Facts[1].Place.ShouldBe("Полтава");

        person.Facts[2].Value.ShouldBe("бригадир");

        // PLAC немає — місце збирається зі складових ADDR.
        person.Facts[3].Place.ShouldBe("Чернівці, Україна");
    }

    [Fact]
    public void Residence_written_as_tag_value_keeps_the_place()
    {
        // «1 RESI Полтава» суперечить 5.5.1 (RESI — подія без значення), але так
        // пише чимало програм. Без обробки цієї форми місце зникало б мовчки.
        var doc = Import("0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 RESI Полтава\n");

        var fact = doc.Persons.ShouldHaveSingleItem().Facts.ShouldHaveSingleItem();

        fact.Kind.ShouldBe(PersonFactKind.Residence);
        fact.Place.ShouldBe("Полтава");
    }

    [Fact]
    public void Empty_residence_record_is_dropped()
    {
        // Голий «1 RESI» без місця, дати й пояснення — залишок після чужого
        // редагування, інформації нуль.
        var doc = Import("0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 RESI\n");

        doc.Persons.ShouldHaveSingleItem().Facts.ShouldBeEmpty();
    }

    [Fact]
    public void Pointers_to_missing_records_are_ignored()
    {
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 SEX M\n" +
            "0 @F1@ FAM\n1 HUSB @I1@\n1 WIFE @I99@\n1 CHIL @I98@\n");

        doc.ParentChildLinks.ShouldBeEmpty();
        doc.SpouseLinks.ShouldBeEmpty();
    }

    [Fact]
    public void Broken_graph_is_repaired_and_reported()
    {
        // Особа сама собі батько — DocumentIntegrity відкидає таке ребро.
        var doc = Import(
            "0 @I1@ INDI\n1 NAME Іван /Коваленко/\n1 SEX M\n" +
            "0 @F1@ FAM\n1 HUSB @I1@\n1 CHIL @I1@\n",
            out var report);

        doc.ParentChildLinks.ShouldBeEmpty();
        report.RepairedIssues.ShouldNotBeEmpty();
        report.RepairedIssues.ShouldContain(i => i.MessageKey == FileErrorKeys.RepairedSelfLinks);
    }

    [Fact]
    public void Records_without_xref_are_skipped()
    {
        var doc = Import("0 INDI\n1 NAME Безіменний /Запис/\n", out var report);

        doc.Persons.ShouldBeEmpty();
        report.SkippedRecords.ShouldBe(1);
    }

    [Fact]
    public void Document_title_and_source_come_from_the_header()
    {
        var file = GedcomReader.Read(Head + "1 NOTE Родина Шевченків\n0 TRLR\n");

        var doc = GedcomImporter.Import(file, out var report);

        doc.Meta.Title.ShouldBe("Родина Шевченків");
        report.Source.ShouldBe("ЧужаПрограма");
        report.Version.ShouldBe("5.5.1");
    }

    [Fact]
    public void Imported_document_is_marked_dirty()
    {
        // Документ ще не збережений у .familytree — інакше користувач втратив би імпорт.
        Import("0 @I1@ INDI\n1 NAME Іван /Коваленко/\n").IsDirty.ShouldBeTrue();
    }
}
