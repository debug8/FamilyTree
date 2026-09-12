namespace FamilyTree.Gedcom;

/// <summary>
/// Ключі локалізації для помилок і попереджень обміну GEDCOM (T-5.2).
/// Шар обміну не залежить від WPF/resx, тому передає лише ключ і аргументи;
/// текст резолвить шар застосунку — як це вже робиться для
/// <c>ValidationKeys</c> та <c>FileErrorKeys</c>.
/// Кожен ключ мусить існувати в ОБОХ resx-файлах.
/// </summary>
public static class GedcomKeys
{
    // ---- Помилки читання (файл відкрити неможливо) ----------------------

    /// <summary>Вміст не схожий на GEDCOM (немає запису <c>0 HEAD</c>).</summary>
    public const string NotGedcom = "GedcomError_NotGedcom";

    /// <summary>Кодування ANSEL не підтримується. {0} — оголошене значення HEAD.CHAR.</summary>
    public const string AnselUnsupported = "GedcomError_AnselUnsupported";

    /// <summary>Непідтримувана версія стандарту (напр. 7.0). {0} — версія з HEAD.GEDC.VERS.</summary>
    public const string UnsupportedVersion = "GedcomError_UnsupportedVersion";

    /// <summary>Вміст не вдалося декодувати жодним підтримуваним кодуванням. {0} — оголошене кодування.</summary>
    public const string BadEncoding = "GedcomError_BadEncoding";

    // ---- Попередження (файл читається, але щось втрачено) ---------------

    /// <summary>
    /// Оголошене кодування не підійшло, застосовано інше.
    /// {0} — оголошене, {1} — фактично застосоване.
    /// </summary>
    public const string EncodingFallback = "GedcomWarn_EncodingFallback";

    /// <summary>Рядки, які не вдалося розібрати, пропущено. {0} — кількість.</summary>
    public const string MalformedLines = "GedcomWarn_MalformedLines";

    /// <summary>Теги поза профілем пропущено. {0} — кількість, {1} — приклади через кому.</summary>
    public const string SkippedTags = "GedcomWarn_SkippedTags";

    /// <summary>Особи без імені отримали «?». {0} — кількість.</summary>
    public const string UnnamedPersons = "GedcomWarn_UnnamedPersons";

    /// <summary>
    /// Запис DEAT без дати. Особа позначається померлою (<c>Person.Deceased</c>),
    /// але дата лишається невідомою. {0} — кількість.
    /// </summary>
    public const string DeathWithoutDate = "GedcomWarn_DeathWithoutDate";

    /// <summary>
    /// Дати, які не лягли в модель і збережені текстом (періоди, INT, роки до н.е.,
    /// інші календарі). {0} — кількість.
    /// </summary>
    public const string TextOnlyDates = "GedcomWarn_TextOnlyDates";

    /// <summary>Записи, пропущені цілком (без xref, дубльований xref). {0} — кількість.</summary>
    public const string SkippedRecords = "GedcomWarn_SkippedRecords";

    /// <summary>Зв'язки, відхилені валідатором (цикл, третій біологічний батько тощо). {0} — кількість.</summary>
    public const string RejectedLinks = "GedcomWarn_RejectedLinks";
}
