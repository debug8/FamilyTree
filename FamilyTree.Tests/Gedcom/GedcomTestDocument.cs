using FamilyTree.Domain;
using FamilyTree.Storage;

namespace FamilyTree.Tests.Gedcom;

/// <summary>
/// Складання документа-фікстури для тестів обміну (T-5.2). Родини будуються кодом,
/// а не читанням із <c>samples/</c>: так у самому тесті видно, яку конфігурацію
/// він перевіряє, і тест не залежить від файлів поза своїм проєктом.
/// </summary>
internal sealed class GedcomTestDocument
{
    private int _next;

    public FamilyDocument Document { get; } = FamilyDocument.CreateNew("Тестова родина");

    /// <summary>
    /// Додає особу з передбачуваним <c>Id</c> (<c>…0001</c>, <c>…0002</c>, …).
    /// Передбачуваність потрібна там, де порядок слотів вирішує саме Id.
    /// </summary>
    public Person Add(
        string lastName,
        string firstName,
        Gender gender,
        string? middleName = null,
        string? maidenName = null,
        FamilyDate? birth = null,
        FamilyDate? death = null,
        string? birthPlace = null,
        string? notes = null)
    {
        var person = new Person
        {
            Id = NextId(),
            LastName = lastName,
            FirstName = firstName,
            MiddleName = middleName,
            MaidenName = maidenName,
            Gender = gender,
            BirthDate = birth,
            DeathDate = death,
            BirthPlace = birthPlace,
            Notes = notes,
            UpdatedAt = new DateTime(2026, 9, 3, 10, 20, 30, DateTimeKind.Utc),
        };

        Document.Persons.Add(person);
        return person;
    }

    /// <summary>Ребро «батько/мати → дитина».</summary>
    public ParentChildLink Parent(Person parent, Person child, ParentRole role = ParentRole.Biological)
    {
        var link = new ParentChildLink
        {
            Id = NextId(),
            ParentId = parent.Id,
            ChildId = child.Id,
            ParentRole = role,
        };

        Document.ParentChildLinks.Add(link);
        return link;
    }

    /// <summary>Ребро подружжя.</summary>
    public SpouseLink Marry(
        Person first,
        Person second,
        FamilyDate? marriage = null,
        FamilyDate? divorce = null,
        bool divorced = false)
    {
        var link = SpouseLink.Create(first.Id, second.Id, marriage, divorce, divorced);
        Document.SpouseLinks.Add(link);
        return link;
    }

    private Guid NextId() => new($"00000000-0000-0000-0000-{++_next:D12}");
}
