namespace FamilyTree.Domain.Kinship;

/// <summary>
/// T-3.2 — обчислення родинного зв'язку особи-родича відносно кореневої особи (розд. 4.3):
/// пошук НСП, класифікація пари (a, b), уточнення рідні/зведені сиблінги (4.4),
/// базове подружжя, делегування назви форматеру.
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
            return Build(KinshipKind.SamePerson, 0, 0, SiblingKind.NotSibling, relative.Gender, Array.Empty<Guid>());
        }

        var nca = _ancestorFinder.FindNearestSet(root.Id, relative.Id, graph);

        if (!nca.Found)
        {
            // Кровного зв'язку немає — можливо, це подружжя.
            var kind = graph.GetSpouses(root.Id).Any(s => s.Id == relative.Id)
                ? KinshipKind.Spouse
                : KinshipKind.None;
            return Build(kind, 0, 0, SiblingKind.NotSibling, relative.Gender, Array.Empty<Guid>());
        }

        var a = nca.StepsFromA;
        var b = nca.StepsFromB;

        var relationKind = b == 0 ? KinshipKind.DirectAncestor
            : a == 0 ? KinshipKind.DirectDescendant
            : KinshipKind.Collateral;

        var siblingKind = a == 1 && b == 1
            ? ClassifySiblings(graph, root.Id, relative.Id)
            : SiblingKind.NotSibling;

        return Build(relationKind, a, b, siblingKind, relative.Gender, nca.AncestorIds);
    }

    private KinshipResult Build(
        KinshipKind kind, int a, int b, SiblingKind siblingKind, Gender gender, IReadOnlyList<Guid> ancestors)
    {
        var context = new KinshipContext(kind, a, b, gender, siblingKind);
        var name = _formatter.Format(in context);
        return new KinshipResult(kind, a, b, siblingKind, name, ancestors);
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
}
