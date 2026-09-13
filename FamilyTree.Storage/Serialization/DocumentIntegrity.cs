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
/// <para>
/// Публічний (а не internal) з T-5.2: тим самим механізмом чиститься документ,
/// зібраний імпортером GEDCOM із чужого файлу — задача та сама, дублювати її
/// в шарі обміну було б помилкою.
/// </para>
/// </summary>
public static class DocumentIntegrity
{
    /// <summary>
    /// Перевірка інваріантів ПЕРЕД записом (B-12). Читання має жорсткий гейт
    /// (<see cref="Verify"/> відмовляє на порожніх і неунікальних Id осіб), а запис
    /// не мав жодного — тож будь-який шлях, що створив дублікат Id у пам'яті,
    /// збереженням перетворювався на файл, який застосунок сам відмовиться відкрити,
    /// і назад дороги вже не було.
    /// <para>
    /// Свідомо перевіряє РІВНО те, від чого відмовляється завантаження, і нічого
    /// понад те: решта дефектів на читанні мовчки лагодиться, тож блокувати через них
    /// збереження означало б не дати користувачу зберегти роботу через дрібницю.
    /// Викликати ДО будь-якого запису на диск.
    /// </para>
    /// </summary>
    public static void EnsureWritable(FamilyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        CheckPersonIds(
            document.Persons,
            FileErrorKeys.WriteEmptyPersonId,
            FileErrorKeys.WriteDuplicatePersonId);
    }

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

        // Безпека (B-13). PhotoPath з чужого файлу потрапляє прямо в Image.Source картки
        // особи. UNC/URL/абсолютний/«..»-шлях дозволяв би SMB-хендшейк до атакувальника
        // (витік NTLM), зовнішній HTTP-трекер або читання поза текою даних. Захист тут,
        // у сховищі, щоб не залежати від того, як саме UI резолвить і показує фото.
        var badPhotoPaths = SanitizePhotoPaths(document);
        Add(issues, FileErrorKeys.RepairedBadPhotoPaths, badPhotoPaths);

        // Структурно биті неточні дати (T-5.2a): напр. range без меж, approx без кваліфікатора,
        // порожня фраза — у чужому/ручному файлі. Скидаємо в null (дата «невідома») зі звітом.
        var badDates = SanitizeDates(document);
        Add(issues, FileErrorKeys.RepairedBadDates, badDates);

        // Вбудовані мініатюри фото: у нашому експорті це ~5 КБ на особу, але чужий
        // (чи зіпсований) файл може принести мегабайти base64 на кожного — і застосунок
        // спробує це декодувати. Завелике значення скидаємо: оригінал у теці даних, якщо
        // він є, усе одно має пріоритет при показі.
        var bigThumbnails = SanitizeThumbnails(document);
        Add(issues, FileErrorKeys.RepairedBigThumbnails, bigThumbnails);

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

    private static void EnsureUsablePersonIds(List<Person> persons) =>
        CheckPersonIds(persons, FileErrorKeys.EmptyPersonId, FileErrorKeys.DuplicatePersonId);

    /// <summary>
    /// Спільна перевірка для читання і запису — різняться лише ключі повідомлень
    /// (на читанні «файл не відкрито», на записі «запис скасовано»). Одна реалізація
    /// на два напрямки гарантує, що записати можна рівно те, що потім відкриється.
    /// </summary>
    private static void CheckPersonIds(List<Person> persons, string emptyKey, string duplicateKey)
    {
        var empty = persons.Count(p => p.Id == Guid.Empty);
        if (empty > 0)
        {
            throw FamilyFileException.Create(emptyKey, inner: null, empty);
        }

        var firstDuplicate = persons
            .GroupBy(p => p.Id)
            .FirstOrDefault(g => g.Count() > 1);

        if (firstDuplicate is not null)
        {
            var affected = persons.Count - persons.Select(p => p.Id).Distinct().Count();
            throw FamilyFileException.Create(
                duplicateKey,
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
    /// Для подружжя дублем вважається запис, що збігається з попереднім УСІМА полями: пара,
    /// дати шлюбу й розлучення, прапорець завершення, місце. Будь-яка відмінність — окремий
    /// шлюб, і викидати його не можна (повторний шлюб пари — легітимна історія, B-16).
    /// </para>
    /// <para>
    /// Спершу ключ складався лише з пари й дати шлюбу — і з'їдав реальні дані (B-70): пара, у
    /// якої перший шлюб завершено без дати розлучення, а другий записано без дати шлюбу, давала
    /// два однакові ключі <c>(a, b, null)</c>, тож ДРУГИЙ шлюб зникав просто при відкриванні
    /// файлу. Тепер такі записи різняться прапорцем <see cref="SpouseLink.Divorced"/>, і
    /// лишаються обидва.
    /// </para>
    /// </summary>
    private static int RemoveDuplicateLinks(FamilyDocument document)
    {
        var removed = 0;

        var seenParentChild = new HashSet<(Guid Parent, Guid Child)>();
        removed += document.ParentChildLinks.RemoveAll(l => !seenParentChild.Add((l.ParentId, l.ChildId)));

        var seenSpouse = new HashSet<SpouseKey>();
        removed += document.SpouseLinks.RemoveAll(l => !seenSpouse.Add(new SpouseKey(
            l.Person1Id, l.Person2Id, l.MarriageDate, l.DivorceDate, l.Divorced, l.MarriagePlace)));

        return removed;
    }

    /// <summary>
    /// Ключ порівняння подружніх зв'язків — увесь вміст запису. Рівність за значенням
    /// (<see cref="FamilyDate"/> теж record), тож два однакові записи дають однаковий ключ, а
    /// будь-яка змістовна відмінність робить їх різними зв'язками.
    /// </summary>
    private readonly record struct SpouseKey(
        Guid First, Guid Second, FamilyDate? Marriage, FamilyDate? Divorce, bool Divorced, string? Place);

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
    /// Лишає в дитини не більше двох біологічних батьків і ніколи двох тієї самої ВІДОМОЇ
    /// статі (двоє Male або двоє Female). Стать Unknown не тригерить правило «та сама стать»
    /// (двоє з невідомою статтю припустимі), але рахується в межу двох: M+U, F+U, U+U — ок;
    /// M+F+U чи будь-який третій — відкидається. Те саме правило застосовує
    /// RelationshipValidator при вводі; файл — ні (B-15, B-18). Порядок:
    /// лишаються перші прийнятні зв'язки.
    /// </summary>
    private static int RemoveExtraBiologicalParents(FamilyDocument document)
    {
        var genderById = document.Persons.ToDictionary(p => p.Id, p => p.Gender);
        var acceptedByChild = new Dictionary<Guid, List<Gender>>();

        return document.ParentChildLinks.RemoveAll(link =>
        {
            if (link.ParentRole != ParentRole.Biological)
            {
                return false;
            }

            var gender = genderById.GetValueOrDefault(link.ParentId, Gender.Unknown);

            if (!acceptedByChild.TryGetValue(link.ChildId, out var accepted))
            {
                acceptedByChild[link.ChildId] = accepted = new List<Gender>();
            }

            // Другий батько/мати тієї самої відомої статі, або вже двоє прийнятих — відкидаємо.
            if ((gender != Gender.Unknown && accepted.Contains(gender)) || accepted.Count >= 2)
            {
                return true;
            }

            accepted.Add(gender);
            return false;
        });
    }

    /// <summary>
    /// Очищає <see cref="Person.PhotoPath"/>, який не є безпечним відносним шляхом усередині
    /// теки даних (див. <see cref="PhotoPathPolicy"/>): абсолютні шляхи, UNC, URL і обхід
    /// каталогів через «..». Небезпечне значення скидається в <see langword="null"/> —
    /// картка тоді показує силует за статтю замість звертання до чужого ресурсу (B-13).
    /// Порожні/пробільні значення не чіпаємо й не рахуємо.
    /// </summary>
    private static int SanitizePhotoPaths(FamilyDocument document)
    {
        var cleared = 0;

        foreach (var person in document.Persons)
        {
            if (string.IsNullOrWhiteSpace(person.PhotoPath))
            {
                continue;
            }

            if (!PhotoPathPolicy.IsSafeRelativePhotoPath(person.PhotoPath))
            {
                person.PhotoPath = null;
                cleared++;
            }
        }

        return cleared;
    }

    /// <summary>
    /// Найбільша прийнятна мініатюра. Наш експорт при 100 px дає 4–6 КБ; 256 КБ — це
    /// із запасом «у сорок разів більше, ніж треба», тобто явно не наша мініатюра.
    /// </summary>
    private const int MaxThumbnailBytes = 256 * 1024;

    /// <summary>
    /// Скидає надто великі вбудовані мініатюри (див. <see cref="MaxThumbnailBytes"/>).
    /// Самі байти не перевіряємо на «чи це взагалі JPEG» — декодер зображення в UI
    /// обгорнутий у try/catch, а вгадувати формати тут означало б дублювати кодеки.
    /// </summary>
    private static int SanitizeThumbnails(FamilyDocument document)
    {
        var cleared = 0;

        foreach (var person in document.Persons)
        {
            if (person.PhotoThumbnail is { Length: > MaxThumbnailBytes })
            {
                person.PhotoThumbnail = null;
                cleared++;
            }
        }

        return cleared;
    }

    /// <summary>
    /// Скидає структурно некоректні неточні дати (T-5.2a) у <see langword="null"/>: напр.
    /// діапазон без меж, приблизна без кваліфікатора, порожня фраза, точка без року — таке
    /// може прийти з ручного чи чужого v2-файлу. Перевірка — <see cref="FamilyDate.IsStructurallyValid"/>.
    /// Охоплює дати народження й смерті, шлюбу й розлучення, а також дати життєвих
    /// фактів (<see cref="PersonFact.Date"/>).
    /// </summary>
    private static int SanitizeDates(FamilyDocument document)
    {
        var cleared = 0;

        foreach (var person in document.Persons)
        {
            // PersonEvent — record, тож «скинути дату» означає перезібрати подію
            // (WithDate), а не присвоїти властивість. Якщо крім битої дати в події
            // нічого не було, вона стає null — порожніх подій не буває.
            if (Invalid(person.Birth?.Date))
            {
                person.Birth = PersonEvent.WithDate(person.Birth, null);
                cleared++;
            }

            if (Invalid(person.Death?.Date))
            {
                // Дата пішла, але сам факт смерті лишається відомим: без прапорця
                // особа мовчки «ожила» б після чистки чужого файлу.
                person.Death = PersonEvent.WithDate(person.Death, null);
                person.Deceased = true;
                cleared++;
            }

            // Дати життєвих фактів (OCCU/RESI) — той самий FamilyDate, тож той самий ризик.
            // PersonFact — record, тож «скинути дату» означає замінити елемент списку,
            // а не присвоїти властивість.
            for (var i = 0; i < person.Facts.Count; i++)
            {
                if (Invalid(person.Facts[i].Date))
                {
                    person.Facts[i] = person.Facts[i] with { Date = null };
                    cleared++;
                }
            }
        }

        foreach (var link in document.SpouseLinks)
        {
            if (Invalid(link.MarriageDate))
            {
                link.MarriageDate = null;
                cleared++;
            }

            if (Invalid(link.DivorceDate))
            {
                link.DivorceDate = null;
                cleared++;
            }
        }

        return cleared;

        static bool Invalid(FamilyDate? date) => date is not null && !date.IsStructurallyValid();
    }
}
