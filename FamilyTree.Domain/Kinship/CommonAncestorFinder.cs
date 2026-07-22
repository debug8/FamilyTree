namespace FamilyTree.Domain.Kinship;

/// <summary>
/// T-3.1 — пошук найближчого спільного предка (НСП) двох осіб (розд. 4.2).
/// </summary>
public sealed class CommonAncestorFinder
{
    /// <summary>
    /// Знаходить набір найближчих спільних предків: НСП з мінімальною сумою (a + b),
    /// за рівності — з меншим max(a, b). Повертає всіх предків на цій найкращій відстані
    /// (напр. обох спільних батьків для рідних сиблінгів). Порожньо, якщо зв'язку немає.
    /// </summary>
    public NearestCommonAncestors FindNearestSet(Guid personA, Guid personB, FamilyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (!graph.Contains(personA) || !graph.Contains(personB))
        {
            return NearestCommonAncestors.NotFound;
        }

        var ancestorsOfA = CollectAncestorDistances(personA, graph);
        var ancestorsOfB = CollectAncestorDistances(personB, graph);

        var bestA = -1;
        var bestB = -1;
        var ancestors = new List<Guid>();

        foreach (var (ancestorId, a) in ancestorsOfA)
        {
            if (!ancestorsOfB.TryGetValue(ancestorId, out var b))
            {
                continue;
            }

            if (bestA < 0 || IsCloser(a, b, bestA, bestB))
            {
                bestA = a;
                bestB = b;
                ancestors.Clear();
                ancestors.Add(ancestorId);
            }
            else if (a == bestA && b == bestB)
            {
                ancestors.Add(ancestorId);
            }
        }

        return ancestors.Count == 0
            ? NearestCommonAncestors.NotFound
            : new NearestCommonAncestors(bestA, bestB, ancestors);
    }

    /// <summary>Найближчий спільний предок (перший із набору) або null.</summary>
    public CommonAncestor? FindNearest(Guid personA, Guid personB, FamilyGraph graph)
    {
        var set = FindNearestSet(personA, personB, graph);
        return set.Found
            ? new CommonAncestor(set.AncestorIds[0], set.StepsFromA, set.StepsFromB)
            : null;
    }

    private static bool IsCloser(int a, int b, int bestA, int bestB)
    {
        var sum = a + b;
        var bestSum = bestA + bestB;
        if (sum != bestSum)
        {
            return sum < bestSum;
        }

        return Math.Max(a, b) < Math.Max(bestA, bestB);
    }

    /// <summary>
    /// Відстані від особи до кожного її предка (сама особа — 0). BFS угору гарантує мінімум.
    /// Публічно — щоб калькулятор міг визначати лінію спорідненості.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> AncestorDistances(Guid start, FamilyGraph graph) =>
        CollectAncestorDistances(start, graph);

    private static Dictionary<Guid, int> CollectAncestorDistances(Guid start, FamilyGraph graph)
    {
        var distances = new Dictionary<Guid, int> { [start] = 0 };
        var queue = new Queue<Guid>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var nextDistance = distances[current] + 1;

            foreach (var parent in graph.GetParents(current))
            {
                if (distances.TryAdd(parent.Id, nextDistance))
                {
                    queue.Enqueue(parent.Id);
                }
            }
        }

        return distances;
    }
}
