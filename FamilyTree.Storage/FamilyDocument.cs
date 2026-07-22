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
    public bool IsDirty { get; set; }

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
