using FamilyTree.Storage;

namespace FamilyTree.Gedcom;

/// <summary>
/// Звіт про імпорт GEDCOM (T-5.2, Частина 3): що прочитано, що втрачено, що полагоджено.
/// Показується користувачу після імпорту — за зразком звіту злиття (<c>MergeReport</c>):
/// рядок додається лише для ненульового лічильника, щоб типовий чистий імпорт
/// не обростав зайвим текстом.
/// </summary>
public sealed class GedcomImportReport
{
    /// <summary>Скільки осіб створено.</summary>
    public required int Persons { get; init; }

    /// <summary>Скільки записів <c>FAM</c> оброблено.</summary>
    public required int Families { get; init; }

    /// <summary>Скільки ребер «батько → дитина» створено (після чистки).</summary>
    public required int ParentChildLinks { get; init; }

    /// <summary>Скільки зв'язків подружжя створено (після чистки).</summary>
    public required int SpouseLinks { get; init; }

    /// <summary>Особи без імені у файлі — їм проставлено «?».</summary>
    public required int UnnamedPersons { get; init; }

    /// <summary>Записи <c>DEAT</c> без дати: факт смерті відомий, а дата — ні; модель такого не тримає.</summary>
    public required int DeathsWithoutDate { get; init; }

    /// <summary>
    /// Дати, які не лягли в модель і збережені текстом разом із сирим виразом
    /// (періоди <c>FROM…TO</c>, <c>INT</c>, роки до н.е., подвійні роки, інші календарі).
    /// При зворотному експорті вони віддаються дослівно.
    /// </summary>
    public required int TextOnlyDates { get; init; }

    /// <summary>Записи, які довелося пропустити цілком (без xref, дубльований xref).</summary>
    public required int SkippedRecords { get; init; }

    /// <summary>Рядки, які не вдалося розібрати.</summary>
    public required int MalformedLines { get; init; }

    /// <summary>Теги поза профілем: тег → скільки разів трапився.</summary>
    public required IReadOnlyDictionary<string, int> SkippedTags { get; init; }

    /// <summary>Фактично застосоване кодування.</summary>
    public required string EncodingName { get; init; }

    /// <summary>Оголошене кодування, яке не підійшло, або null.</summary>
    public string? EncodingFallbackFrom { get; init; }

    /// <summary>Версія стандарту з <c>HEAD.GEDC.VERS</c>, або null.</summary>
    public string? Version { get; init; }

    /// <summary>Програма-автор файлу з <c>HEAD.SOUR</c>, або null.</summary>
    public string? Source { get; init; }

    /// <summary>
    /// Дефекти, полагоджені <c>DocumentIntegrity</c> (висячі зв'язки, дублі, цикли,
    /// зайві біологічні батьки, биті дати, небезпечні шляхи) — той самий механізм,
    /// що чистить чужі <c>.familytree</c>.
    /// </summary>
    public required IReadOnlyList<DocumentIssue> RepairedIssues { get; init; }

    /// <summary>Чи є про що повідомляти користувача, крім самих лічильників додавання.</summary>
    public bool HasWarnings =>
        UnnamedPersons > 0
        || DeathsWithoutDate > 0
        || TextOnlyDates > 0
        || SkippedRecords > 0
        || MalformedLines > 0
        || SkippedTags.Count > 0
        || EncodingFallbackFrom is not null
        || RepairedIssues.Count > 0;

    /// <summary>Найчастіші пропущені теги — для показу в звіті (решта не влізе й не потрібна).</summary>
    public IEnumerable<KeyValuePair<string, int>> TopSkippedTags(int count = 5) =>
        SkippedTags.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(count);
}
