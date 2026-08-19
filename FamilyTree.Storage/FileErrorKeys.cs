namespace FamilyTree.Storage;

/// <summary>
/// Ключі локалізації для помилок і попереджень роботи з файлом документа.
/// Шар сховища не залежить від WPF/resx, тому передає лише ключ і аргументи;
/// текст резолвить шар застосунку (як це вже робиться для <c>ValidationKeys</c>).
/// Кожен ключ мусить існувати в ОБОХ resx-файлах.
/// </summary>
public static class FileErrorKeys
{
    // ---- Помилки читання (файл відкрити неможливо) ----------------------

    /// <summary>Файл не знайдено. {0} — шлях.</summary>
    public const string NotFound = "FileError_NotFound";

    /// <summary>Немає прав на читання/запис. {0} — шлях.</summary>
    public const string AccessDenied = "FileError_AccessDenied";

    /// <summary>Помилка вводу-виводу (файл заблокований, мережа тощо). {0} — шлях.</summary>
    public const string Io = "FileError_Io";

    /// <summary>Вміст не є коректним JSON. {0} — деталі парсера.</summary>
    public const string MalformedJson = "FileError_MalformedJson";

    /// <summary>Файл не в кодуванні UTF-8.</summary>
    public const string BadEncoding = "FileError_BadEncoding";

    /// <summary>Відсутнє або некоректне поле schemaVersion.</summary>
    public const string BadSchemaVersion = "FileError_BadSchemaVersion";

    /// <summary>Відсутній обов'язковий розділ файлу. {0} — назва розділу.</summary>
    public const string MissingSection = "FileError_MissingSection";

    /// <summary>Особа без ідентифікатора. {0} — кількість.</summary>
    public const string EmptyPersonId = "FileError_EmptyPersonId";

    /// <summary>Кілька осіб з однаковим ідентифікатором. {0} — кількість, {1} — приклад Id.</summary>
    public const string DuplicatePersonId = "FileError_DuplicatePersonId";

    // ---- Полагоджені дефекти (файл відкривається з попередженням) -------

    /// <summary>Відкинуто зв'язки на неіснуючих осіб. {0} — кількість.</summary>
    public const string RepairedDanglingLinks = "FileRepair_DanglingLinks";

    /// <summary>Відкинуто зв'язки особи із собою. {0} — кількість.</summary>
    public const string RepairedSelfLinks = "FileRepair_SelfLinks";

    /// <summary>Відкинуто дубльовані зв'язки. {0} — кількість.</summary>
    public const string RepairedDuplicateLinks = "FileRepair_DuplicateLinks";

    /// <summary>Скинуто недопустимі значення переліків (стать, роль батька). {0} — кількість.</summary>
    public const string RepairedBadEnums = "FileRepair_BadEnums";

    /// <summary>Відкинуто зв'язки, що утворювали цикл «батько-дитина». {0} — кількість.</summary>
    public const string RepairedCycles = "FileRepair_Cycles";

    /// <summary>Відкинуто зайвих біологічних батьків тієї самої статі. {0} — кількість.</summary>
    public const string RepairedExtraBioParents = "FileRepair_ExtraBioParents";
}
