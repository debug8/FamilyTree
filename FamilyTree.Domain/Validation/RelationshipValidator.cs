namespace FamilyTree.Domain.Validation;

/// <summary>
/// Перевірка доменних правил зв'язків (розд. 3.3): жорсткі інваріанти та м'які попередження.
/// Не залежить від UI — повертає ключі ресурсів (див. <see cref="ValidationKeys"/>).
/// Стан не зберігає; перевіряє кандидата на додавання проти наявних даних.
/// </summary>
public sealed class RelationshipValidator
{
    private const int MinParentAgeYears = 12;

    /// <summary>
    /// Валідує додавання зв'язку «батько/мати → дитина».
    /// </summary>
    public ValidationResult ValidateParentChild(
        ParentChildLink candidate,
        IReadOnlyCollection<Person> persons,
        IReadOnlyCollection<ParentChildLink> existingLinks)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(persons);
        ArgumentNullException.ThrowIfNull(existingLinks);

        var byId = persons.ToDictionary(p => p.Id);
        var errors = new List<ValidationMessage>();
        var warnings = new List<ValidationMessage>();

        // 1. Особа не може бути власним батьком/матір'ю (розд. 3.3, п.4).
        if (candidate.ParentId == candidate.ChildId)
        {
            errors.Add(ValidationMessage.Of(ValidationKeys.SelfParent));
            return new ValidationResult(errors, warnings);
        }

        // 2. Дубль ребра (той самий ParentId+ChildId) (п.3).
        var isDuplicate = existingLinks.Any(l => l.ParentId == candidate.ParentId && l.ChildId == candidate.ChildId);
        if (isDuplicate)
        {
            errors.Add(ValidationMessage.Of(ValidationKeys.DuplicateParentChild));
        }

        // 3. Заборона циклів: дитина не може бути предком батька (п.2).
        if (IsAncestor(candidate.ChildId, candidate.ParentId, existingLinks))
        {
            errors.Add(ValidationMessage.Of(ValidationKeys.CycleDetected));
        }

        // 4. Не більше одного біологічного батька та однієї біологічної матері (п.1).
        if (candidate.ParentRole == ParentRole.Biological &&
            byId.TryGetValue(candidate.ParentId, out var parent) &&
            parent.Gender != Gender.Unknown)
        {
            var hasSameGenderBioParent = existingLinks
                .Where(l => l.ChildId == candidate.ChildId
                            && l.ParentRole == ParentRole.Biological
                            && l.ParentId != candidate.ParentId)
                .Select(l => byId.GetValueOrDefault(l.ParentId))
                .Any(p => p is not null && p.Gender == parent.Gender);

            if (hasSameGenderBioParent)
            {
                errors.Add(ValidationMessage.Of(parent.Gender == Gender.Male
                    ? ValidationKeys.SecondBiologicalFather
                    : ValidationKeys.SecondBiologicalMother));
            }
        }

        // 5. М'які попередження за віком (лише для біологічних, де вік має сенс).
        if (candidate.ParentRole == ParentRole.Biological &&
            byId.TryGetValue(candidate.ParentId, out var parentPerson) &&
            byId.TryGetValue(candidate.ChildId, out var childPerson) &&
            parentPerson.BirthDate is { } parentBirth &&
            childPerson.BirthDate is { } childBirth)
        {
            if (parentBirth > childBirth)
            {
                warnings.Add(ValidationMessage.Of(ValidationKeys.ParentYoungerThanChild,
                    parentPerson.FullName, childPerson.FullName));
            }
            else if (childBirth < parentBirth.AddYears(MinParentAgeYears))
            {
                warnings.Add(ValidationMessage.Of(ValidationKeys.ChildBornBeforeParentAdult,
                    parentPerson.FullName, childPerson.FullName));
            }
        }

        return new ValidationResult(errors, warnings);
    }

    /// <summary>
    /// Валідує додавання зв'язку «подружжя».
    /// </summary>
    public ValidationResult ValidateSpouse(
        SpouseLink candidate,
        IReadOnlyCollection<SpouseLink> existingLinks)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existingLinks);

        var errors = new List<ValidationMessage>();

        // Особа не може бути в шлюбі сама з собою (п.4).
        if (candidate.Person1Id == candidate.Person2Id)
        {
            errors.Add(ValidationMessage.Of(ValidationKeys.SelfSpouse));
            return new ValidationResult(errors, Array.Empty<ValidationMessage>());
        }

        // Дубль пари (ідентифікатори нормалізовані у SpouseLink) (п.3).
        var isDuplicate = existingLinks.Any(l =>
            l.Person1Id == candidate.Person1Id && l.Person2Id == candidate.Person2Id);
        if (isDuplicate)
        {
            errors.Add(ValidationMessage.Of(ValidationKeys.DuplicateSpouse));
        }

        return new ValidationResult(errors, Array.Empty<ValidationMessage>());
    }

    /// <summary>
    /// Валідує самі дати особи (м'яке попередження: смерть раніше народження).
    /// </summary>
    public ValidationResult ValidatePerson(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);

        var warnings = new List<ValidationMessage>();

        if (person.BirthDate is { } birth && person.DeathDate is { } death && death < birth)
        {
            warnings.Add(ValidationMessage.Of(ValidationKeys.DeathBeforeBirth));
        }

        return new ValidationResult(Array.Empty<ValidationMessage>(), warnings);
    }

    /// <summary>
    /// Чи є <paramref name="potentialAncestor"/> предком <paramref name="ofPerson"/>
    /// у наявних зв'язках (обхід угору по ребрах «дитина → батьки»).
    /// </summary>
    private static bool IsAncestor(
        Guid potentialAncestor,
        Guid ofPerson,
        IReadOnlyCollection<ParentChildLink> links)
    {
        var parentsOf = links
            .GroupBy(l => l.ChildId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ParentId).ToList());

        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(ofPerson);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!parentsOf.TryGetValue(current, out var parents))
            {
                continue;
            }

            foreach (var parentId in parents)
            {
                if (parentId == potentialAncestor)
                {
                    return true;
                }

                if (visited.Add(parentId))
                {
                    stack.Push(parentId);
                }
            }
        }

        return false;
    }
}
