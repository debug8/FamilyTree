using FamilyTree.Domain;

namespace FamilyTree.Storage.Serialization;

/// <summary>
/// Перевірка цілісності документа після десеріалізації.
/// <para>
/// Політика розділена свідомо:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Відмова</b> — коли дані неоднозначні й «полагодити» їх означало б вигадати
/// приналежність зв'язків: порожні або дубльовані <see cref="Entity.Id"/> осіб.
/// Переприв'язати такі зв'язки навмання гірше, ніж відмовитися відкривати файл.
/// </item>
/// <item>
/// <b>Полагодження + звіт</b> — коли запис однозначно сміттєвий і його відкидають:
/// зв'язок на неіснуючу особу, особа сама собі батько/подружжя, дубльований зв'язок,
/// значення переліку поза діапазоном.
/// </item>
/// <item>
/// <b>Полагодження без звіту</b> — коли зміна не втрачає даних: нормалізація порядку
/// Id у парі подружжя.
/// </item>
/// </list>
/// Без цього кроку битий файл валив застосунок у <c>ToDictionary(p =&gt; p.Id)</c>
/// вже ПІСЛЯ того, як документ було встановлено в сесію — з напівзламаним UI.
/// </summary>
internal static class DocumentIntegrity
{
    /// <summary>
    /// Перевіряє й за потреби чистить документ на місці.
    /// Кидає <see cref="FamilyFileException"/> на неоднозначних дефектах.
    /// </summary>
    /// <returns>Перелік полагоджених дефектів (порожній, якщо файл чистий).</returns>
    public static IReadOnlyList<DocumentIssue> Verify(FamilyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        EnsureUsablePersonIds(document.Persons);

        var issues = new List<DocumentIssue>();
        var known = document.Persons.Select(p => p.Id).ToHashSet();

        var badEnums = NormalizeEnums(document);
        Add(issues, FileErrorKeys.RepairedBadEnums, badEnums);

        // Нормалізація порядку Id у парі подружжя — без втрат даних, тому НЕ звітуємо:
        // інакше кожне відкриття файлу з tools/SeedGenerator (який пише пари в довільному
        // порядку) показувало б попередження на сотні зв'язків без реальної проблеми.
        // У звіт потрапляє лише те, що було відкинуто.
        NormalizeSpouseOrder(document.SpouseLinks);

        var selfLinks = RemoveSelfLinks(document);
        Add(issues, FileErrorKeys.RepairedSelfLinks, selfLinks);

        var dangling = RemoveDanglingLinks(document, known);
        Add(issues, FileErrorKeys.RepairedDanglingLinks, dangling);

        var duplicates = RemoveDuplicateLinks(document);
        Add(issues, FileErrorKeys.RepairedDuplicateLinks, duplicates);

        // Глобальні інваріанти (B-15). Локальні перевірки вище дивляться на одну пару Id;
        // ці — на граф загалом: цикл «батько-дитина» довжиною ≥2 і кілька біологічних
        // батьків тієї самої статі в однієї дитини. У UI валідатор це блокує, а «сирий»
        // чи чужий файл — ні, тож застосунок міг показувати взаємно суперечливе родство.
        var cycles = RemoveParentChildCycles(document);
        Add(issues, FileErrorKeys.RepairedCycles, cycles);

        var extraBioParents = RemoveExtraBiologicalParents(document);
        Add(issues, FileErrorKeys.RepairedExtraBioParents, extraBioParents);

        return issues;
    }

    private static void Add(List<DocumentIssue> issues, string key, int count)
    {
        if (count > 0)
        {
            issues.Add(new DocumentIssue(key, count));
        }
    }

    // ---- Відмова: Id осіб мусять бути присутні й унікальні ---------------

    private static void EnsureUsablePersonIds(List<Person> persons)
    {
        var empty = persons.Count(p => p.Id == Guid.Empty);
        if (empty > 0)
        {
            throw FamilyFileException.Create(FileErrorKeys.EmptyPersonId, inner: null, empty);
        }

        var firstDuplicate = persons
            .GroupBy(p => p.Id)
            .FirstOrDefault(g => g.Count() > 1);

        if (firstDuplicate is not null)
        {
            var affected = persons.Count - persons.Select(p => p.Id).Distinct().Count();
            throw FamilyFileException.Create(
                FileErrorKeys.DuplicatePersonId,
                inner: null,
                affected,
                firstDuplicate.Key);
        }
    }

    // ---- Полагодження ----------------------------------------------------

    /// <summary>
    /// Скидає значення переліків поза діапазоном. <c>JsonStringEnumConverter</c> за
    /// замовчуванням приймає цілі числа, тож <c>"gender": 7</c> перетворюється на
    /// <c>(Gender)7</c> і тече в усі форматери родства без жодної перевірки.
    /// </summary>
    private static int NormalizeEnums(FamilyDocument document)
    {
        var fixedCount = 0;

        foreach (var person in document.Persons)
        {
            if (!Enum.IsDefined(person.Gender))
            {
                person.Gender = Gender.Unknown;
                fixedCount++;
            }
        }

        foreach (var link in document.ParentChildLinks)
        {
            if (!Enum.IsDefined(link.ParentRole))
            {
                link.ParentRole = ParentRole.Biological;
                fixedCount++;
            }
        }

        return fixedCount;
    }

    /// <summary>
    /// Приводить пари подружжя до інваріанта Person1Id ≤ Person2Id. Домен його
    /// декларує (див. <see cref="SpouseLink"/>), але <c>required init</c> дозволяє
    /// створити зв'язок і в зворотному порядку — саме так приходять файли з інших
    /// джерел, і тоді перевірка дубля шлюбу у валідаторі не знаходить дубля.
    /// </summary>
    /// <returns>Скільки зв'язків було переставлено (для тестів; у звіт не йде).</returns>
    internal static int NormalizeSpouseOrder(List<SpouseLink> links)
    {
        var fixedCount = 0;

        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (link.Person1Id.CompareTo(link.Person2Id) <= 0)
            {
                continue;
            }

            links[i] = new SpouseLink
            {
                Id = link.Id,
                Person1Id = link.Person2Id,
                Person2Id = link.Person1Id,
                MarriageDate = link.MarriageDate,
                DivorceDate = link.DivorceDate,
                Divorced = link.Divorced,
            };
            fixedCount++;
        }

        return fixedCount;
    }

    private static int RemoveSelfLinks(FamilyDocument document)
    {
        var removed = document.ParentChildLinks.RemoveAll(l => l.ParentId == l.ChildId);
        removed += document.SpouseLinks.RemoveAll(l => l.Person1Id == l.Person2Id);
        return removed;
    }

    private static int RemoveDanglingLinks(FamilyDocument document, HashSet<Guid> known)
    {
        var removed = document.ParentChildLinks.RemoveAll(
            l => !known.Contains(l.ParentId) || !known.Contains(l.ChildId));

        removed += document.SpouseLinks.RemoveAll(
            l => !known.Contains(l.Person1Id) || !known.Contains(l.Person2Id));

        return removed;
    }

    /// <summary>
    /// Прибирає повторні зв'язки тієї самої пари. Порядок збережених зв'язків не
    /// змінюється — лишається перший.
    /// <para>
    /// Для подружжя ключ включає <see cref="SpouseLink.MarriageDate"/>: повторний шлюб тієї
    /// самої пари з іншою датою — легітимна історія (B-16), і викидати його не можна.
    /// Дублем лишається запис із тією самою парою і тією самою датою шлюбу.
    /// </para>
    /// </summary>
    private static int RemoveDuplicateLinks(FamilyDocument document)
    {
        var removed = 0;

        var seenParentChild = new HashSet<(Guid Parent, Guid Child)>();
        removed += document.ParentChildLinks.RemoveAll(l => !seenParentChild.Add((l.ParentId, l.ChildId)));

        var seenSpouse = new HashSet<(Guid First, Guid Second, DateOnly? Marriage)>();
        removed += document.SpouseLinks.RemoveAll(l => !seenSpouse.Add((l.Person1Id, l.Person2Id, l.MarriageDate)));

        return removed;
    }

    /// <summary>
    /// Відкидає ребра «батько-дитина», що замикають цикл (особа стає власним предком).
    /// Ребра обробляються по порядку; ребро приймається, лише якщо дитина ще НЕ є предком
    /// батька в уже прийнятому графі — інакше воно замкнуло б цикл і його відкидаємо.
    /// Детерміновано за порядком у файлі; ловить цикли будь-якої довжини (A→B→A і довші).
    /// </summary>
    private static int RemoveParentChildCycles(FamilyDocument document)
    {
        // childId -> його вже прийняті батьки (для обходу вгору).
        var parentsOf = new Dictionary<Guid, List<Guid>>();

        var removed = document.ParentChildLinks.RemoveAll(link =>
        {
            // Додавання parent→child замкнуло б цикл, якщо child уже є предком parent
            // (тобто, йдучи вгору від parent, ми досягаємо child).
            if (IsReachableUpward(link.ParentId, link.ChildId, parentsOf))
            {
                return true; // відкинути
            }

            if (!parentsOf.TryGetValue(link.ChildId, out var parents))
            {
                parentsOf[link.ChildId] = parents = new List<Guid>();
            }

            parents.Add(link.ParentId);
            return false;
        });

        return removed;
    }

    /// <summary>Чи досяжний <paramref name="target"/>, йдучи вгору (дитина→батьки) від <paramref name="start"/>.</summary>
    private static bool IsReachableUpward(Guid start, Guid target, Dictionary<Guid, List<Guid>> parentsOf)
    {
        var stack = new Stack<Guid>();
        var seen = new HashSet<Guid>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!parentsOf.TryGetValue(current, out var parents))
            {
                continue;
            }

            foreach (var parent in parents)
            {
                if (parent == target)
                {
                    return true;
                }

                if (seen.Add(parent))
                {
                    stack.Push(parent);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Лишає в дитини не більше одного біологічного батька кожної статі (по одному на
    /// Male/Female/Unknown); наступні біологічні зв'язки тієї самої статі відкидає.
    /// Валідатор забороняє це при вводі, а файл — ні (B-15). Порядок: лишається перший.
    /// </summary>
    private static int RemoveExtraBiologicalParents(FamilyDocument document)
    {
        var genderById = document.Persons.ToDictionary(p => p.Id, p => p.Gender);
        var seen = new HashSet<(Guid Child, Gender Gender)>();

        return document.ParentChildLinks.RemoveAll(link =>
        {
            if (link.ParentRole != ParentRole.Biological)
            {
                return false;
            }

            var gender = genderById.GetValueOrDefault(link.ParentId, Gender.Unknown);
            return !seen.Add((link.ChildId, gender));
        });
    }
}
