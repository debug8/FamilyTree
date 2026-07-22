namespace FamilyTree.Domain.Kinship;

/// <summary>
/// Найближчий спільний предок (НСП) двох осіб та відстані до нього (розд. 4.2).
/// </summary>
/// <param name="AncestorId">Id спільного предка.</param>
/// <param name="StepsFromA">Відстань (кількість поколінь) від особи A до предка (a).</param>
/// <param name="StepsFromB">Відстань від особи B до предка (b).</param>
public sealed record CommonAncestor(Guid AncestorId, int StepsFromA, int StepsFromB);

/// <summary>
/// Набір найближчих спільних предків на однаковій найкращій відстані (a, b).
/// Кілька предків трапляється, коли особи мають спільну пару предків (напр. рідні сиблінги —
/// спільні батько й мати).
/// </summary>
/// <param name="StepsFromA">a — відстань від A до предків набору (−1, якщо предків немає).</param>
/// <param name="StepsFromB">b — відстань від B до предків набору.</param>
/// <param name="AncestorIds">Ідентифікатори спільних предків (порожньо, якщо зв'язку немає).</param>
public sealed record NearestCommonAncestors(int StepsFromA, int StepsFromB, IReadOnlyList<Guid> AncestorIds)
{
    public bool Found => AncestorIds.Count > 0;

    public static NearestCommonAncestors NotFound { get; } =
        new(-1, -1, Array.Empty<Guid>());
}
