using FamilyTree.Domain;

namespace FamilyTree.Storage;

/// <summary>
/// Відкритий документ родини в пам'яті: особи, зв'язки, метадані та прапорець
/// незбережених змін. ViewModel-и працюють із ним напряму; <see cref="IFamilyStorage"/>
/// викликається лише на Open/Save.
/// </summary>
public sealed class FamilyDocument
{
    public DocumentMeta Meta { get; set; } = new();

    public List<Person> Persons { get; } = new();

    public List<ParentChildLink> ParentChildLinks { get; } = new();

    public List<SpouseLink> SpouseLinks { get; } = new();

    /// <summary>Чи є незбережені зміни (не серіалізується).</summary>
    /// <remarks>
    /// Змінюється лише через <see cref="MarkChanged"/> / <see cref="MarkSaved"/> /
    /// <see cref="MarkClean"/>: прапорець мусить рухатися разом із
    /// <see cref="Revision"/>, інакше повертається B-11.
    /// </remarks>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Лічильник змін вмісту (не серіалізується). Потрібен саме сховищу: запис
    /// асинхронний і триває секунди на великому документі в синхронізованій теці,
    /// а UI-потік у цей час вільний. Знімок для серіалізації робиться на початку, тож
    /// правка, зроблена під час запису, у файл не потрапляє — і зняти після цього
    /// «є незбережені зміни» означало б тихо її втратити (B-11).
    /// </summary>
    public long Revision { get; private set; }

    /// <summary>
    /// Позначає документ зміненим. Єдиний спосіб «забруднити» документ —
    /// пряме <c>IsDirty = true</c> лишило б <see cref="Revision"/> позаду.
    /// </summary>
    public void MarkChanged()
    {
        Revision++;
        IsDirty = true;
    }

    /// <summary>
    /// Знімає прапорець незбережених змін після успішного запису — але лише якщо
    /// з моменту знімка (<paramref name="savedRevision"/>) документ не змінювався.
    /// Повертає <c>true</c>, якщо прапорець знято.
    /// </summary>
    public bool MarkSaved(long savedRevision)
    {
        if (Revision != savedRevision)
        {
            return false;
        }

        IsDirty = false;
        return true;
    }

    /// <summary>
    /// Безумовно оголошує документ чистим. Для випадків, коли документ у пам'яті
    /// щойно ЗАМІЩЕНО (відкриття файлу, новий документ): порівнювати ревізії там
    /// нема з чим.
    /// </summary>
    public void MarkClean() => IsDirty = false;

    /// <summary>
    /// Дефекти, які сховище полагодило під час завантаження цього документа
    /// (не серіалізується). Порожньо для нових документів і для чистих файлів.
    /// UI показує це користувачу як попередження — інакше «зникнення» частини
    /// зв'язків виглядало б як втрата даних без причини.
    /// </summary>
    public IReadOnlyList<DocumentIssue> RepairedIssues { get; set; } = Array.Empty<DocumentIssue>();

    /// <summary>Створює новий порожній документ із заголовком.</summary>
    public static FamilyDocument CreateNew(string title)
    {
        var now = DateTime.UtcNow;
        return new FamilyDocument
        {
            Meta = new DocumentMeta { Title = title, CreatedAt = now, UpdatedAt = now },
        };
    }
}
