using FamilyTree.Domain;
using FamilyTree.Domain.Validation;

namespace FamilyTree.Storage;

/// <summary>Підсумок злиття для звіту користувачу (T-5.1).</summary>
/// <param name="AddedPersons">Скільки осіб буде/було додано.</param>
/// <param name="DuplicatePersons">Скільки осіб визначено як дублікати (не додаються).</param>
/// <param name="AddedParentLinks">Скільки нових зв'язків «батько–дитина».</param>
/// <param name="AddedSpouseLinks">Скільки нових подружніх зв'язків.</param>
/// <param name="RejectedLinks">Скільки зв'язків відхилено валідатором (цикл, третій біо-батько тощо).</param>
/// <param name="UpdatedPersons">Скільки наявних осіб доповнено полями з джерела.</param>
/// <param name="Conflicts">Скільки непорожніх полів розійшлися (лишили значення цілі, не перезаписали).</param>
public sealed record MergeReport(
    int AddedPersons, int DuplicatePersons, int AddedParentLinks, int AddedSpouseLinks,
    int RejectedLinks = 0, int UpdatedPersons = 0, int Conflicts = 0);

/// <summary>
/// Заповнення порожніх полів наявної особи значеннями з джерела при злитті (B-04).
/// Кожне поле не-null лише тоді, коли його треба проставити (ціль порожня, джерело має значення).
/// Застосовується у <see cref="FamilyMerger.Apply"/>, щоб <c>Plan</c> не мутував документ.
/// </summary>
public sealed record PersonFieldFill(
    Person Target,
    DateOnly? DeathDate = null,
    string? BirthPlace = null,
    string? MaidenName = null,
    string? Notes = null,
    string? PhotoPath = null);

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

    /// <summary>Доповнення полів наявних осіб (заповнення порожніх значеннями з джерела).</summary>
    public List<PersonFieldFill> PersonUpdates { get; } = new();

    /// <summary>Скільки непорожніх полів розійшлися (значення цілі збережено).</summary>
    public int Conflicts { get; internal set; }

    public MergeReport ToReport() =>
        new(PersonsToAdd.Count, DuplicatePersons, ParentLinksToAdd.Count, SpouseLinksToAdd.Count,
            RejectedLinks, PersonUpdates.Count, Conflicts);
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

        // Мапи наявних/доданих осіб: за Id і за ключем ідентичності (ПІБ+дата народження).
        var personById = new Dictionary<Guid, Person>();
        var existingByKey = new Dictionary<string, Guid>();
        foreach (var person in target.Persons)
        {
            personById.TryAdd(person.Id, person);
            RegisterKey(existingByKey, person, person.Id);
        }

        // importId → підсумковий Id (наявної особи, якщо дублікат, або нової доданої).
        var remap = new Dictionary<Guid, Guid>();
        var duplicates = 0;
        var conflicts = 0;

        foreach (var person in source.Persons)
        {
            // 1. Той самий Id уже є в цілі.
            if (personById.TryGetValue(person.Id, out var sameId))
            {
                // Але чи це справді та сама людина? Якщо ПІБ+дата обох відомі й різні — це РІЗНІ
                // люди, що зіткнулися на Id (напр. детерміновані Id у зразках). Тоді додаємо як
                // нову з НОВИМ Guid; інакше зв'язки прив'язалися б до сторонньої людини (B-04).
                if (SameIdentity(sameId, person))
                {
                    MergeFields(sameId, person, plan, ref conflicts); // доповнюємо порожні поля цілі
                    remap[person.Id] = sameId.Id;
                    duplicates++;
                }
                else
                {
                    var reId = Clone(person, newId: true);
                    plan.PersonsToAdd.Add(reId);
                    remap[person.Id] = reId.Id;
                    personById[reId.Id] = reId;
                    RegisterKey(existingByKey, person, reId.Id);
                }

                continue;
            }

            // 2. Збіг ПІБ + дати народження з наявною особою → та сама людина.
            var key = IdentityKey(person);
            if (key is not null && existingByKey.TryGetValue(key, out var matchId) &&
                personById.TryGetValue(matchId, out var matched))
            {
                MergeFields(matched, person, plan, ref conflicts);
                remap[person.Id] = matchId;
                duplicates++;
                continue;
            }

            // 3. Нова особа.
            var clone = Clone(person);
            plan.PersonsToAdd.Add(clone);
            remap[person.Id] = clone.Id;
            personById[clone.Id] = clone;
            RegisterKey(existingByKey, person, clone.Id);
        }

        plan.DuplicatePersons = duplicates;
        plan.Conflicts = conflicts;

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

        // Доповнюємо порожні поля наявних осіб значеннями з джерела (B-04). Тільки порожні —
        // непорожні розбіжності лишаються як є й пораховані в plan.Conflicts.
        foreach (var fill in plan.PersonUpdates)
        {
            if (fill.DeathDate is { } death) fill.Target.DeathDate = death;
            if (fill.BirthPlace is { } birthPlace) fill.Target.BirthPlace = birthPlace;
            if (fill.MaidenName is { } maiden) fill.Target.MaidenName = maiden;
            if (fill.Notes is { } notes) fill.Target.Notes = notes;
            if (fill.PhotoPath is { } photo) fill.Target.PhotoPath = photo;
        }

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

    private static void RegisterKey(Dictionary<string, Guid> map, Person person, Guid id)
    {
        if (IdentityKey(person) is { } key)
        {
            map.TryAdd(key, id);
        }
    }

    /// <summary>
    /// Чи це та сама людина. Якщо ключ ідентичності обох відомий — мусить збігатися; якщо хоч в
    /// одного немає дати народження (ключ null) — довести відмінність не можна, вважаємо тією самою
    /// (типовий випадок: користувач редагує власний файл). Розходяться лише за обома відомими ключами.
    /// </summary>
    private static bool SameIdentity(Person a, Person b)
    {
        var keyA = IdentityKey(a);
        var keyB = IdentityKey(b);
        return keyA is null || keyB is null || keyA == keyB;
    }

    /// <summary>
    /// Готує доповнення порожніх полів цілі значеннями з джерела й рахує конфлікти
    /// (обидва непорожні й різні — значення цілі лишається). Додає fill у план, якщо є що заповнити.
    /// </summary>
    private static void MergeFields(Person target, Person source, MergePlan plan, ref int conflicts)
    {
        var any = false;
        var localConflicts = 0;

        DateOnly? death = null;
        if (target.DeathDate is null)
        {
            if (source.DeathDate is { } d)
            {
                death = d;
                any = true;
            }
        }
        else if (source.DeathDate is { } sd && sd != target.DeathDate.Value)
        {
            localConflicts++;
        }

        var birthPlace = ResolveText(target.BirthPlace, source.BirthPlace, ref any, ref localConflicts);
        var maiden = ResolveText(target.MaidenName, source.MaidenName, ref any, ref localConflicts);
        var notes = ResolveText(target.Notes, source.Notes, ref any, ref localConflicts);
        var photo = ResolveText(target.PhotoPath, source.PhotoPath, ref any, ref localConflicts);

        conflicts += localConflicts;

        if (any)
        {
            plan.PersonUpdates.Add(new PersonFieldFill(target, death, birthPlace, maiden, notes, photo));
        }
    }

    /// <summary>
    /// Значення для заповнення текстового поля: не-null лише якщо ціль порожня, а джерело — ні.
    /// Якщо обидва непорожні й різні — це конфлікт (лишаємо ціль, повертаємо null).
    /// </summary>
    private static string? ResolveText(string? targetValue, string? sourceValue, ref bool any, ref int conflicts)
    {
        var targetEmpty = string.IsNullOrWhiteSpace(targetValue);
        var sourceHas = !string.IsNullOrWhiteSpace(sourceValue);

        if (targetEmpty && sourceHas)
        {
            any = true;
            return sourceValue;
        }

        if (!targetEmpty && sourceHas &&
            !string.Equals(targetValue!.Trim(), sourceValue!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            conflicts++;
        }

        return null;
    }

    private static Person Clone(Person p, bool newId = false) => new()
    {
        Id = newId ? Guid.CreateVersion7() : p.Id,
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
