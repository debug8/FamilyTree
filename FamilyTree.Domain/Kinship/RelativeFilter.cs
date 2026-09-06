namespace FamilyTree.Domain.Kinship;

/// <summary>
/// Склад режиму «Лише родичі»: кого вважати ріднею кореневої особи.
/// <para>
/// Винесено в окремий тип навмисно — щоб пізніше ці прапорці можна було віддати
/// в екран налаштувань, не переписуючи ні фільтр, ні дерево. Поки що всюди
/// використовується <see cref="Default"/>.
/// </para>
/// </summary>
/// <param name="IncludeAffinity">
/// Чи включати свояцтво — рідню через шлюб (тесть, свекруха, шурин, невістка).
/// Без нього дерево звужується до кровних, і з нього зникає частина реальної родини.
/// </param>
/// <param name="IncludeCurrentSpouses">
/// Чи включати чинне подружжя відібраних осіб навіть тоді, коли для нього самого
/// назви зв'язку немає (напр. дружина троюрідного брата).
/// </param>
/// <param name="IncludeFormerSpouses">
/// Те саме для розлучених партнерів. За замовчуванням вимкнено: колишній чоловік
/// двоюрідної сестри — уже не родина, хоч і лишається батьком спільних дітей
/// (самі діти потрапляють у дерево як кровні, незалежно від цього прапорця).
/// Вимкнений прапорець не лише не додає таких людей проходом подружжям, а й прибирає
/// тих, кого ядро спорідненості назвало через розірваний шлюб («колишній чоловік сестри»);
/// колишнє подружжя самої кореневої особи — виняток, воно лишається.
/// </param>
public sealed record RelativeFilterOptions(
    bool IncludeAffinity = true,
    bool IncludeCurrentSpouses = true,
    bool IncludeFormerSpouses = false)
{
    /// <summary>Типовий склад: кровні + свояцтво + чинне подружжя.</summary>
    public static RelativeFilterOptions Default { get; } = new();
}

/// <summary>
/// Відбирає осіб, які є ріднею кореневої особи, для режиму дерева «Лише родичі».
/// <para>
/// Навіщо: режим «Усі» будує весь зв'язний компонент, ходячи по батьках, дітях і
/// подружжю без обмежень. Через шлюби компонент розповзається на цілком сторонніх
/// людей — рідню чоловіка сестри дружини і далі, — тож «усі родичі» насправді
/// показує всіх, хто є у файлі й хоч якось дотичний.
/// </para>
/// <para>
/// Критерій тут інший і збігається з тим, що бачить користувач у бейджі вузла та на
/// вкладці «Хто кому»: особа входить у дерево, якщо ядро спорідненості дає для неї
/// назву зв'язку (<see cref="KinshipKind"/> не <see cref="KinshipKind.None"/>).
/// </para>
/// </summary>
public sealed class RelativeFilter
{
    private readonly KinshipCalculator _kinship;

    public RelativeFilter(KinshipCalculator kinship) =>
        _kinship = kinship ?? throw new ArgumentNullException(nameof(kinship));

    /// <summary>
    /// Повертає ідентифікатори осіб, які потрапляють у дерево (разом із самим коренем).
    /// Порожня множина — якщо кореня немає в графі.
    /// </summary>
    public HashSet<Guid> Select(FamilyGraph graph, Guid rootId, RelativeFilterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= RelativeFilterOptions.Default;

        var included = new HashSet<Guid>();
        if (graph.Find(rootId) is not { } root)
        {
            return included;
        }

        included.Add(rootId);

        foreach (var person in graph.Persons)
        {
            if (person.Id == rootId)
            {
                continue;
            }

            var kind = _kinship.Compute(root, person, graph, options.IncludeAffinity).Kind;
            if (kind != KinshipKind.None)
            {
                included.Add(person.Id);
            }
        }

        // Порядок важливий: спершу відсіюємо «колишніх», і лише потім додаємо подружжя —
        // інакше відсіяна особа встигла б затягнути в дерево ще й своє нове подружжя.
        if (!options.IncludeFormerSpouses)
        {
            TrimFormerSpouses(graph, root, included);
        }

        AddSpouses(graph, included, options);
        return included;
    }

    /// <summary>
    /// Прибирає тих, хто тримається в наборі ЛИШЕ на розірваному шлюбі, — наприклад
    /// колишнього чоловіка сестри.
    /// <para>
    /// Навіщо окремий прохід: ядро спорідненості називає й такі зв'язки («колишній чоловік
    /// сестри»), тож самої перевірки «чи є назва» не досить — без цього кроку прапорець
    /// <see cref="RelativeFilterOptions.IncludeFormerSpouses"/> ні на що не впливав би.
    /// </para>
    /// <para>
    /// Правило: особу прибираємо, якщо (1) вона має шлюбні зв'язки з набором, (2) ЖОДЕН із
    /// них не чинний, (3) серед них немає шлюбу з самим коренем і (4) вона не кровна рідня.
    /// Пункт (3) навмисний: колишнє подружжя самої кореневої особи з дерева не викидаємо —
    /// це, як правило, другий батько спільних дітей, і рамки подружжя його вже показують.
    /// Пункт (1) лишає в наборі свояків без власних шлюбів (тесть, шурин).
    /// </para>
    /// <para>
    /// Відома межа: батьки колишнього подружжя (колишній тесть) лишаються — вони не мають
    /// шлюбних зв'язків із набором, тож під правило не підпадають, а ядро для них назву дає.
    /// </para>
    /// </summary>
    private void TrimFormerSpouses(FamilyGraph graph, Person root, HashSet<Guid> included)
    {
        var doubtful = new List<Guid>();

        foreach (var id in included)
        {
            if (id == root.Id)
            {
                continue;
            }

            var linksInSet = graph.GetSpouses(id)
                .Where(spouse => included.Contains(spouse.Id))
                .ToList();

            if (linksInSet.Count == 0 ||
                linksInSet.Any(spouse => spouse.Id == root.Id) ||
                linksInSet.Any(spouse => graph.IsSpouseActive(id, spouse.Id)))
            {
                continue;
            }

            doubtful.Add(id);
        }

        // Кровність перевіряємо лише для підозрюваних: це ще один прохід ядром,
        // і робити його для всього документа заради кількох осіб не варто.
        foreach (var id in doubtful)
        {
            if (graph.Find(id) is not { } person)
            {
                continue;
            }

            if (_kinship.Compute(root, person, graph, includeAffinity: false).Kind == KinshipKind.None)
            {
                included.Remove(id);
            }
        }
    }

    /// <summary>
    /// Додає подружжя вже відібраних осіб — РІВНО один прохід, по знімку множини.
    /// Каскад тут заборонений принципово: подружжя подружжя родича — це вже
    /// чужа родина, і саме через такий каскад режим «Усі» показує півфайлу.
    /// </summary>
    private static void AddSpouses(FamilyGraph graph, HashSet<Guid> included, RelativeFilterOptions options)
    {
        if (!options.IncludeCurrentSpouses && !options.IncludeFormerSpouses)
        {
            return;
        }

        var relatives = included.ToList();
        foreach (var id in relatives)
        {
            foreach (var spouse in graph.GetSpouses(id))
            {
                var wanted = graph.IsSpouseActive(id, spouse.Id)
                    ? options.IncludeCurrentSpouses
                    : options.IncludeFormerSpouses;

                if (wanted)
                {
                    included.Add(spouse.Id);
                }
            }
        }
    }
}
