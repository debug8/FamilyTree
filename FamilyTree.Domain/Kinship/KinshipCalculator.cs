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
    public KinshipResult Compute(Person root, Person relative, FamilyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(relative);
        ArgumentNullException.ThrowIfNull(graph);

        if (root.Id == relative.Id)
        {
            return Build(KinshipKind.SamePerson, 0, 0, SiblingKind.NotSibling, Lineage.Unknown, relative.Gender, Array.Empty<Guid>());
        }

        var nca = _ancestorFinder.FindNearestSet(root.Id, relative.Id, graph);

        if (!nca.Found)
        {
            var isSpouse = graph.GetSpouses(root.Id).Any(s => s.Id == relative.Id);
            var kind = isSpouse ? KinshipKind.Spouse : KinshipKind.None;
            var former = isSpouse && !graph.IsSpouseActive(root.Id, relative.Id);
            return Build(kind, 0, 0, SiblingKind.NotSibling, Lineage.Unknown, relative.Gender, Array.Empty<Guid>(), former);
        }

        var a = nca.StepsFromA;
        var b = nca.StepsFromB;

        var relationKind = b == 0 ? KinshipKind.DirectAncestor
            : a == 0 ? KinshipKind.DirectDescendant
            : KinshipKind.Collateral;

        var siblingKind = a == 1 && b == 1
            ? ClassifySiblings(graph, root.Id, relative.Id)
            : SiblingKind.NotSibling;

        var lineage = DetermineLineage(graph, root.Id, a, nca.AncestorIds);

        return Build(relationKind, a, b, siblingKind, lineage, relative.Gender, nca.AncestorIds);
    }

    private KinshipResult Build(
        KinshipKind kind, int a, int b, SiblingKind siblingKind, Lineage lineage, Gender gender,
        IReadOnlyList<Guid> ancestors, bool isFormerSpouse = false)
    {
        var context = new KinshipContext(kind, a, b, gender, siblingKind, lineage, isFormerSpouse);
        var name = _formatter.Format(in context);
        return new KinshipResult(kind, a, b, siblingKind, lineage, name, ancestors);
    }

    private static SiblingKind ClassifySiblings(FamilyGraph graph, Guid firstId, Guid secondId)
    {
        var firstParents = graph.GetParents(firstId).ToDictionary(p => p.Id);
        var shared = graph.GetParents(secondId).Where(p => firstParents.ContainsKey(p.Id)).ToList();

        return shared.Count switch
        {
            >= 2 => SiblingKind.Full,
            1 => shared[0].Gender switch
            {
                Gender.Male => SiblingKind.HalfPaternal,
                Gender.Female => SiblingKind.HalfMaternal,
                _ => SiblingKind.HalfUnknown,
            },
            _ => SiblingKind.NotSibling,
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
