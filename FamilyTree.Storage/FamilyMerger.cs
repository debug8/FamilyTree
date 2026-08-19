using FamilyTree.Domain;
using FamilyTree.Domain.Validation;

namespace FamilyTree.Storage;

/// <summary>Підсумок злиття для звіту користувачу (T-5.1).</summary>
/// <param name="AddedPersons">Скільки осіб буде/було додано.</param>
/// <param name="DuplicatePersons">Скільки осіб визначено як дублікати (не додаються).</param>
/// <param name="AddedParentLinks">Скільки нових зв'язків «батько–дитина».</param>
/// <param name="AddedSpouseLinks">Скільки нових подружніх зв'язків.</param>
/// <param name="RejectedLinks">Скільки зв'язків відхилено валідатором (цикл, третій біо-батько тощо).</param>
public sealed record MergeReport(
    int AddedPersons, int DuplicatePersons, int AddedParentLinks, int AddedSpouseLinks, int RejectedLinks = 0);

/// <summary>
/// План злиття: що саме буде додано (обчислюється без зміни документа, щоб показати
/// звіт і дати підтвердити). Застосовується через <see cref="FamilyMerger.Apply"/>.
/// </summary>
public sealed class MergePlan
{
    public List<Person> PersonsToAdd { get; } = new();

    public List<ParentChildLink> ParentLinksToAdd { get; } = new();

    public List<SpouseLink> SpouseLinksToAdd { get; } = new();

    public int DuplicatePersons { get; internal set; }

    /// <summary>Скільки кандидатних зв'язків відхилив валідатор (не додаються).</summary>
    public int RejectedLinks { get; internal set; }

    public MergeReport ToReport() =>
        new(PersonsToAdd.Count, DuplicatePersons, ParentLinksToAdd.Count, SpouseLinksToAdd.Count, RejectedLinks);
}

/// <summary>
/// T-5.1 — злиття іншого документа родини у відкритий. Дублікати осіб визначаються
/// за збігом Id (той самий запис) або за ПІБ + датою народження; зв'язки додаються
/// з переприв'язкою на підсумкові Id і дедуплікацією. Повторний імпорт того самого
/// файлу нічого не дублює.
/// </summary>
public sealed class FamilyMerger
{
    private readonly RelationshipValidator _validator;

    /// <param name="validator">
    /// Валідатор доменних правил зв'язків. Необов'язковий (стан не тримає) — щоб тести
    /// й прямі виклики працювали через <c>new FamilyMerger()</c>, а DI підставляв спільний.
    /// </param>
    public FamilyMerger(RelationshipValidator? validator = null) =>
        _validator = validator ?? new RelationshipValidator();

    /// <summary>Обчислює план злиття <paramref name="source"/> у <paramref name="target"/> (без змін).</summary>
    public MergePlan Plan(FamilyDocument target, FamilyDocument source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        var plan = new MergePlan();

        var existingIds = target.Persons.Select(p => p.Id).ToHashSet();
        var existingByKey = new Dictionary<string, Guid>();
        foreach (var person in target.Persons)
        {
            if (IdentityKey(person) is { } key)
            {
                existingByKey.TryAdd(key, person.Id);
            }
        }

        // importId → підсумковий Id (наявної особи, якщо дублікат, або нової доданої).
        var remap = new Dictionary<Guid, Guid>();
        var duplicates = 0;

        foreach (var person in source.Persons)
        {
            if (existingIds.Contains(person.Id))
            {
                remap[person.Id] = person.Id; // той самий запис уже є (напр. повторний імпорт)
                duplicates++;
                continue;
            }

            var key = IdentityKey(person);
            if (key is not null && existingByKey.TryGetValue(key, out var matchId))
            {
                remap[person.Id] = matchId; // збіг ПІБ + дати народження → зливаємо
                duplicates++;
                continue;
            }

            var clone = Clone(person);
            plan.PersonsToAdd.Add(clone);
            remap[person.Id] = clone.Id;
            existingIds.Add(clone.Id);
            if (key is not null)
            {
                existingByKey.TryAdd(key, clone.Id);
            }
        }

        plan.DuplicatePersons = duplicates;

        // Валідація зв'язків: імпорт додавав їх прямо, обходячи RelationshipValidator, тож
        // чужий файл міг внести цикл (А→Б→А), самобатьківство (колапс двох осіб в одну при
        // зіставленні) чи третього біологічного батька. Тепер кожен кандидат перевіряється
        // проти цілі + вже ПРИЙНЯТИХ кандидатів, а відхилені йдуть у звіт (B-14).
        var personsForValidation = new List<Person>(target.Persons);
        personsForValidation.AddRange(plan.PersonsToAdd);
        var rejected = 0;

        // Зв'язки «батько–дитина» з дедуплікацією за парою (parent, child).
        var parentPairs = target.ParentChildLinks.Select(l => (l.ParentId, l.ChildId)).ToHashSet();
        var acceptedParentLinks = new List<ParentChildLink>(target.ParentChildLinks);
        foreach (var link in source.ParentChildLinks)
        {
            if (!remap.TryGetValue(link.ParentId, out var parentId) ||
                !remap.TryGetValue(link.ChildId, out var childId))
            {
                continue;
            }

            if (!parentPairs.Add((parentId, childId)))
            {
                continue;
            }

            var candidate = new ParentChildLink
            {
                ParentId = parentId,
                ChildId = childId,
                ParentRole = link.ParentRole,
            };

            // Проти цілі + вже прийнятих кандидатів — щоб ловити й цикли серед самих імпортованих.
            if (!_validator.ValidateParentChild(candidate, personsForValidation, acceptedParentLinks).IsValid)
            {
                rejected++;
                continue;
            }

            plan.ParentLinksToAdd.Add(candidate);
            acceptedParentLinks.Add(candidate);
        }

        // Подружні зв'язки з дедуплікацією за невпорядкованою парою.
        var spousePairs = target.SpouseLinks.Select(l => OrderPair(l.Person1Id, l.Person2Id)).ToHashSet();
        var acceptedSpouseLinks = new List<SpouseLink>(target.SpouseLinks);
        foreach (var link in source.SpouseLinks)
        {
            if (!remap.TryGetValue(link.Person1Id, out var a) ||
                !remap.TryGetValue(link.Person2Id, out var b))
            {
                continue;
            }

            if (!spousePairs.Add(OrderPair(a, b)))
            {
                continue;
            }

            var candidate = SpouseLink.Create(a, b, link.MarriageDate, link.DivorceDate, link.Divorced);

            // Ловить самошлюб (обидві особи зіставилися в одну) та перетин періодів.
            if (!_validator.ValidateSpouse(candidate, acceptedSpouseLinks).IsValid)
            {
                rejected++;
                continue;
            }

            plan.SpouseLinksToAdd.Add(candidate);
            acceptedSpouseLinks.Add(candidate);
        }

        plan.RejectedLinks = rejected;
        return plan;
    }

    /// <summary>Застосовує план до документа й повертає підсумок.</summary>
    public MergeReport Apply(FamilyDocument target, MergePlan plan)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(plan);

        target.Persons.AddRange(plan.PersonsToAdd);
        target.ParentChildLinks.AddRange(plan.ParentLinksToAdd);
        target.SpouseLinks.AddRange(plan.SpouseLinksToAdd);
        return plan.ToReport();
    }

    /// <summary>Обчислити план і одразу застосувати (зручно для тестів).</summary>
    public MergeReport Merge(FamilyDocument target, FamilyDocument source) => Apply(target, Plan(target, source));

    private static (Guid, Guid) OrderPair(Guid a, Guid b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);

    /// <summary>
    /// Ключ ідентичності особи: ПІБ + дата народження. Якщо дати народження немає —
    /// null (не зливаємо автоматично, щоб не поєднати різних людей з однаковим ім'ям).
    /// </summary>
    private static string? IdentityKey(Person p)
    {
        if (p.BirthDate is not { } birth)
        {
            return null;
        }

        return string.Join('|', Norm(p.LastName), Norm(p.FirstName), Norm(p.MiddleName), birth.ToString("O"));
    }

    private static string Norm(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static Person Clone(Person p) => new()
    {
        Id = p.Id,
        LastName = p.LastName,
        FirstName = p.FirstName,
        Gender = p.Gender,
        MiddleName = p.MiddleName,
        MaidenName = p.MaidenName,
        BirthDate = p.BirthDate,
        BirthPlace = p.BirthPlace,
        DeathDate = p.DeathDate,
        PhotoPath = p.PhotoPath,
        Notes = p.Notes,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
    };
}
