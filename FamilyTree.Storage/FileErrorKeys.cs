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

    /// <summary>
    /// Немає прав на читання/запис. {0} — шлях.
    /// Спільний для обох напрямків: формулювання нейтральне («немає доступу до файлу»),
    /// тож на шляху збереження додаткового ключа не потрібно.
    /// </summary>
    public const string AccessDenied = "FileError_AccessDenied";

    /// <summary>Помилка вводу-виводу при ЧИТАННІ (файл заблокований, мережа тощо). {0} — шлях.</summary>
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

    // ---- Помилки запису (документ зберегти не вдалося) ------------------

    /// <summary>
    /// Помилка вводу-виводу при ЗБЕРЕЖЕННІ: на диску немає місця, файл заблокований
    /// іншою програмою, мережевий носій відпав. {0} — шлях цільового файлу.
    /// <para>
    /// Окремий ключ від <see cref="Io"/> навмисно: той текст каже «не вдалося прочитати
    /// файл» і на шляху збереження вводив би в оману. Для відмови в правах
    /// перевикористовується <see cref="AccessDenied"/>.
    /// </para>
    /// </summary>
    public const string WriteIo = "FileError_WriteIo";

    /// <summary>
    /// Запис скасовано: у документі є особи без ідентифікатора. {0} — кількість.
    /// Дзеркало <see cref="EmptyPersonId"/> для шляху запису (B-12): текст того ключа
    /// каже «файл не відкрито», а тут файл ще й не починали писати.
    /// </summary>
    public const string WriteEmptyPersonId = "FileError_WriteEmptyPersonId";

    /// <summary>
    /// Запис скасовано: неунікальні ідентифікатори осіб. {0} — кількість, {1} — приклад Id.
    /// Дзеркало <see cref="DuplicatePersonId"/> для шляху запису (B-12).
    /// </summary>
    public const string WriteDuplicatePersonId = "FileError_WriteDuplicatePersonId";

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

    /// <summary>Очищено небезпечні шляхи до фото (абсолютні/UNC/URL/обхід каталогів). {0} — кількість.</summary>
    public const string RepairedBadPhotoPaths = "FileRepair_BadPhotoPaths";

    /// <summary>Скинуто структурно некоректні неточні дати. {0} — кількість.</summary>
    public const string RepairedBadDates = "FileRepair_BadDates";

    /// <summary>Скинуто завеликі вбудовані мініатюри фото. {0} — кількість.</summary>
    public const string RepairedBigThumbnails = "FileRepair_BigThumbnails";
}
