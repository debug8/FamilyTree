namespace FamilyTree.Domain.Kinship;

/// <summary>
/// T-3.2 — обчислення родинного зв'язку особи-родича відносно кореневої особи (розд. 4.3):
/// пошук НСП, класифікація пари (a, b), уточнення рідні/зведені сиблінги (4.4),
/// лінія (по батькові/по матері), базове подружжя, делегування назви форматеру.
/// </summary>
public sealed class KinshipCalculator
{
    private readonly CommonAncestorFinder _ancestorFinder;
    private readonly IKinshipFormatter _formatter;

    public KinshipCalculator(CommonAncestorFinder ancestorFinder, IKinshipFormatter formatter)
    {
        _ancestorFinder = ancestorFinder;
        _formatter = formatter;
    }

    /// <summary>Визначає, ким є <paramref name="relative"/> для кореневої особи <paramref name="root"/>.</summary>
    /// <param name="includeAffinity">Шукати зв'язки через шлюб (свояцтво, розд. 4.5), якщо кровного немає.</param>
    public KinshipResult Compute(Person root, Person relative, FamilyGraph graph, bool includeAffinity = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(relative);
        ArgumentNullException.ThrowIfNull(graph);

        if (root.Id == relative.Id)
        {
            return Build(KinshipKind.SamePerson, 0, 0, SiblingKind.NotSibling, Lineage.Unknown, relative.Gender, Array.Empty<Guid>());
        }

        var nca = _ancestorFinder.FindNearestSet(root.Id, relative.Id, graph);

        // Подружжя перевіряємо ДО кровної класифікації. Раніше ця перевірка стояла лише
        // в гілці «кровного зв'язку немає», тож у шлюбі двоюрідних (звична річ у реальних
        // деревах) власна дружина показувалася як «двоюрідна сестра», а KinshipKind.Spouse
        // та IsFormerSpouse губилися назовсім.
        if (graph.GetSpouses(root.Id).Any(s => s.Id == relative.Id))
        {
            var former = !graph.IsSpouseActive(root.Id, relative.Id);

            // Кровний зв'язок не втрачаємо — форматер допише «(також двоюрідна сестра)».
            string? alsoBlood = null;
            if (nca.Found)
            {
                var bloodContext = BloodContext(root, relative, graph, nca);
                alsoBlood = _formatter.Format(in bloodContext);
            }

            var spouseContext = new KinshipContext(
                KinshipKind.Spouse, 0, 0, relative.Gender, SiblingKind.NotSibling, Lineage.Unknown,
                IsFormerSpouse: former, BloodRelationName: alsoBlood);

            // Спільних предків віддаємо, якщо вони є: KinshipPathExplainer зможе показати
            // кровний ланцюжок, а підсумок при цьому лишиться «дружина (також …)».
            return new KinshipResult(
                KinshipKind.Spouse, 0, 0, SiblingKind.NotSibling, Lineage.Unknown,
                _formatter.Format(in spouseContext), nca.Found ? nca.AncestorIds : Array.Empty<Guid>());
        }

        if (!nca.Found)
        {
            if (includeAffinity && TryAffinity(root, relative, graph) is { } affinity)
            {
                return affinity;
            }

            return Build(KinshipKind.None, 0, 0, SiblingKind.NotSibling, Lineage.Unknown, relative.Gender, Array.Empty<Guid>());
        }

        var context = BloodContext(root, relative, graph, nca);
        return new KinshipResult(
            context.Kind, context.StepsUp, context.StepsDown, context.SiblingKind, context.Lineage,
            _formatter.Format(in context), nca.AncestorIds);
    }

    /// <summary>
    /// Класифікує кровний зв'язок за знайденим НСП: тип, уточнення сиблінгів, лінія.
    /// Виділено окремо, щоб ту саму класифікацію можна було застосувати і до подружжя,
    /// яке водночас є кровним родичем.
    /// </summary>
    private static KinshipContext BloodContext(Person root, Person relative, FamilyGraph graph, NearestCommonAncestors nca)
    {
        var a = nca.StepsFromA;
        var b = nca.StepsFromB;

        var kind = b == 0 ? KinshipKind.DirectAncestor
            : a == 0 ? KinshipKind.DirectDescendant
            : KinshipKind.Collateral;

        var siblingKind = a == 1 && b == 1
            ? ClassifySiblings(graph, root.Id, relative.Id)
            : SiblingKind.NotSibling;

        var lineage = DetermineLineage(graph, root.Id, a, nca.AncestorIds);

        return new KinshipContext(kind, a, b, relative.Gender, siblingKind, lineage);
    }

    private KinshipResult Build(
        KinshipKind kind, int a, int b, SiblingKind siblingKind, Lineage lineage, Gender gender,
        IReadOnlyList<Guid> ancestors, bool isFormerSpouse = false)
    {
        var context = new KinshipContext(kind, a, b, gender, siblingKind, lineage, isFormerSpouse);
        var name = _formatter.Format(in context);
        return new KinshipResult(kind, a, b, siblingKind, lineage, name, ancestors);
    }

    /// <summary>
    /// Свояцтво (розд. 4.5): шукає сполучну особу X такою, що
    /// A —(кров)— X —(шлюб)— B (патерн A) або A —(шлюб)— X —(кров)— B (патерн B).
    /// Обирає найближчий варіант (за сумою кроків кровного плеча). null — свояцтва немає.
    /// </summary>
    private KinshipResult? TryAffinity(Person root, Person relative, FamilyGraph graph)
    {
        var best = AffinityKind.NotAffinity;
        var pivotGender = Gender.Unknown;
        var bestDistance = int.MaxValue;
        var bestFormer = true; // щоб будь-який чинний шлюб мав пріоритет над колишнім

        // Патерн A: X одружений із B(relative), X — кровний родич A(root).
        foreach (var x in graph.GetSpouses(relative.Id))
        {
            if (x.Id == root.Id)
            {
                continue;
            }

            var link = _ancestorFinder.FindNearestSet(root.Id, x.Id, graph);
            if (!link.Found)
            {
                continue;
            }

            var (kind, distance) = MapPatternA(link.StepsFromA, link.StepsFromB);
            if (kind == AffinityKind.NotAffinity)
            {
                continue;
            }

            // Свояцтво тримається на шлюбі X—relative: якщо він розірваний, це «колишня теща».
            var former = !graph.IsSpouseActive(x.Id, relative.Id);
            if (IsBetterAffinity(former, distance, bestFormer, bestDistance))
            {
                best = kind;
                bestDistance = distance;
                bestFormer = former;
                pivotGender = x.Gender;
            }
        }

        // Патерн B: X одружений із A(root), B(relative) — кровний родич X.
        foreach (var x in graph.GetSpouses(root.Id))
        {
            if (x.Id == relative.Id)
            {
                continue;
            }

            var link = _ancestorFinder.FindNearestSet(x.Id, relative.Id, graph);
            if (!link.Found)
            {
                continue;
            }

            var (kind, distance) = MapPatternB(link.StepsFromA, link.StepsFromB);
            if (kind == AffinityKind.NotAffinity)
            {
                continue;
            }

            // Тут свояцтво тримається на власному шлюбі root—X.
            var former = !graph.IsSpouseActive(root.Id, x.Id);
            if (IsBetterAffinity(former, distance, bestFormer, bestDistance))
            {
                best = kind;
                bestDistance = distance;
                bestFormer = former;
                pivotGender = x.Gender;
            }
        }

        if (best == AffinityKind.NotAffinity)
        {
            return null;
        }

        var context = new KinshipContext(
            KinshipKind.Affinity, 0, 0, relative.Gender, SiblingKind.NotSibling, Lineage.Unknown,
            IsFormerSpouse: bestFormer, Affinity: best, PivotGender: pivotGender);
        var name = _formatter.Format(in context);
        return new KinshipResult(KinshipKind.Affinity, 0, 0, SiblingKind.NotSibling, Lineage.Unknown, name, Array.Empty<Guid>());
    }

    /// <summary>
    /// Чинний шлюб важливіший за колишній; за однакової чинності — менша відстань.
    /// Інакше особа, що є і колишньою тещею, і, скажімо, дружиною дядька, могла б
    /// показатися колишньою тещею лише через меншу відстань.
    /// </summary>
    private static bool IsBetterAffinity(bool former, int distance, bool bestFormer, int bestDistance) =>
        former != bestFormer ? !former : distance < bestDistance;

    /// <summary>Кровне плече root→X (X одружений із relative): a кроків від root, b — від X.</summary>
    private static (AffinityKind Kind, int Distance) MapPatternA(int a, int b) => (a, b) switch
    {
        // X — мій батько/мати (НСП = сам X), B одружений(-а) з X → вітчим/мачуха.
        // Найближче свояцтво взагалі, тому distance = 0: якщо особа є і мачухою,
        // і, скажімо, дружиною дядька, показуємо ближчу роль.
        (1, 0) => (AffinityKind.StepParent, 0),
        (0, 1) => (AffinityKind.ChildSpouse, 1),     // X — дитина root → B подружжя моєї дитини
        (1, 1) => (AffinityKind.SiblingSpouse, 2),   // X — сиблінг root → B подружжя мого сиблінга
        (2, 1) => (AffinityKind.UncleAuntSpouse, 3), // X — дядько/тітка root → B подружжя дядька/тітки
        _ => (AffinityKind.NotAffinity, int.MaxValue),
    };

    /// <summary>Кровне плече X→relative (X одружений із root): a кроків від X, b — від relative.</summary>
    private static (AffinityKind Kind, int Distance) MapPatternB(int a, int b) => (a, b) switch
    {
        // X — моє подружжя, relative — дитина X (НСП = сам X) → пасинок/пасербиця.
        (0, 1) => (AffinityKind.StepChild, 0),
        (1, 0) => (AffinityKind.SpouseParent, 1),    // relative — батько/мати X → тесть/свекор…
        (1, 1) => (AffinityKind.SpouseSibling, 2),   // relative — сиблінг X → дівер/шурин…
        _ => (AffinityKind.NotAffinity, int.MaxValue),
    };

    private static SiblingKind ClassifySiblings(FamilyGraph graph, Guid firstId, Guid secondId)
    {
        var firstParents = graph.GetParents(firstId);
        var secondParents = graph.GetParents(secondId);

        var firstIds = firstParents.Select(p => p.Id).ToHashSet();
        var shared = secondParents.Where(p => firstIds.Contains(p.Id)).ToList();

        if (shared.Count >= 2)
        {
            return SiblingKind.Full;
        }

        if (shared.Count == 0)
        {
            return SiblingKind.NotSibling;
        }

        // Спільний рівно один батько. Стверджувати неповнорідність можна лише тоді,
        // коли в ОБОХ осіб відомі обидва батьки — тобто другі батьки справді різні.
        // Якщо хоч в однієї другий батько не записаний (типово для неповного дерева),
        // раніше застосунок видавав «єдинокровний брат», тобто стверджував факт
        // «різні матері», якого в даних немає.
        if (firstParents.Count < 2 || secondParents.Count < 2)
        {
            return SiblingKind.PossiblyHalf;
        }

        return shared[0].Gender switch
        {
            Gender.Male => SiblingKind.HalfPaternal,
            Gender.Female => SiblingKind.HalfMaternal,
            _ => SiblingKind.HalfUnknown,
        };
    }

    /// <summary>
    /// Визначає лінію: через кого з батьків кореневої особи веде шлях до спільного предка.
    /// Має сенс, коли зв'язок іде вгору принаймні на 1 крок (a ≥ 1). Якщо через обох —
    /// <see cref="Lineage.Mixed"/> (напр. рідні сиблінги); якщо стать батька невідома — Unknown.
    /// </summary>
    private static Lineage DetermineLineage(FamilyGraph graph, Guid rootId, int stepsUp, IReadOnlyList<Guid> ncaIds)
    {
        if (stepsUp < 1)
        {
            return Lineage.Unknown;
        }

        var ncaSet = ncaIds.ToHashSet();
        var genders = new HashSet<Gender>();

        foreach (var parent in graph.GetParents(rootId))
        {
            var distances = CommonAncestorFinder.AncestorDistances(parent.Id, graph);
            var leadsToNca = ncaSet.Any(id => distances.TryGetValue(id, out var d) && d == stepsUp - 1);
            if (leadsToNca)
            {
                genders.Add(parent.Gender);
            }
        }

        if (genders.Count == 0)
        {
            return Lineage.Unknown;
        }

        if (genders.Count > 1)
        {
            return Lineage.Mixed;
        }

        return genders.Single() switch
        {
            Gender.Male => Lineage.Paternal,
            Gender.Female => Lineage.Maternal,
            _ => Lineage.Unknown,
        };
    }
}
