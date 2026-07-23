namespace FamilyTree.Domain.Kinship;

/// <summary>
/// T-3.3 — будує людиночитне пояснення шляху родства: ланцюжок осіб від кореневої A
/// до родича B через найближчого спільного предка, з описом кожного кроку.
/// Крокові й підсумкові назви беруться з <see cref="KinshipCalculator"/> (локалізовані).
/// </summary>
public sealed class KinshipPathExplainer
{
    private readonly CommonAncestorFinder _finder;
    private readonly KinshipCalculator _calculator;

    public KinshipPathExplainer(CommonAncestorFinder finder, KinshipCalculator calculator)
    {
        _finder = finder;
        _calculator = calculator;
    }

    public KinshipPath Explain(Person root, Person relative, FamilyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(relative);
        ArgumentNullException.ThrowIfNull(graph);

        // Свояцтво (розд. 4.5) враховуємо в підсумку: якщо кровного зв'язку немає,
        // назва може бути «тесть», «зять» тощо (кровного ланцюжка кроків при цьому не буде).
        var result = _calculator.Compute(root, relative, graph, includeAffinity: true);
        var summary = $"{relative.FullName} — {result.DisplayName} ({root.FullName})";

        if (root.Id == relative.Id)
        {
            return new KinshipPath(new[] { root.Id }, Array.Empty<string>(), summary);
        }

        var chain = BuildChain(root.Id, relative.Id, graph);
        if (chain is null)
        {
            // Кровного шляху немає: якщо це подружжя — покажемо прямий крок, інакше без шляху.
            if (graph.GetSpouses(root.Id).Any(s => s.Id == relative.Id))
            {
                return new KinshipPath(
                    new[] { root.Id, relative.Id },
                    new[] { Step(root, relative, graph) },
                    summary);
            }

            return KinshipPath.NoPath(summary);
        }

        var steps = new List<string>();
        for (var i = 0; i < chain.Count - 1; i++)
        {
            steps.Add(Step(graph.GetPerson(chain[i]), graph.GetPerson(chain[i + 1]), graph));
        }

        return new KinshipPath(chain, steps, summary);
    }

    private string Step(Person from, Person to, FamilyGraph graph) =>
        $"{from.FullName} → {to.FullName}: {_calculator.Compute(from, to, graph).DisplayName}";

    /// <summary>Ланцюжок A → … → НСП → … → B, або null, якщо спільного предка немає.</summary>
    private List<Guid>? BuildChain(Guid rootId, Guid relativeId, FamilyGraph graph)
    {
        var nca = _finder.FindNearest(rootId, relativeId, graph);
        if (nca is null)
        {
            return null;
        }

        var upFromRoot = AncestorPath(rootId, nca.AncestorId, graph);
        var upFromRelative = AncestorPath(relativeId, nca.AncestorId, graph);
        if (upFromRoot is null || upFromRelative is null)
        {
            return null;
        }

        // A → … → НСП, далі НСП → … → B (розворот шляху родича, без повтору НСП).
        var chain = new List<Guid>(upFromRoot);
        for (var i = upFromRelative.Count - 2; i >= 0; i--)
        {
            chain.Add(upFromRelative[i]);
        }

        return chain;
    }

    /// <summary>Найкоротший шлях угору від особи до предка (включно), або null.</summary>
    private static List<Guid>? AncestorPath(Guid from, Guid target, FamilyGraph graph)
    {
        if (from == target)
        {
            return new List<Guid> { from };
        }

        var predecessor = new Dictionary<Guid, Guid>();
        var visited = new HashSet<Guid> { from };
        var queue = new Queue<Guid>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var parent in graph.GetParents(current))
            {
                if (!visited.Add(parent.Id))
                {
                    continue;
                }

                predecessor[parent.Id] = current;
                if (parent.Id == target)
                {
                    return BuildPath(predecessor, from, target);
                }

                queue.Enqueue(parent.Id);
            }
        }

        return null;
    }

    private static List<Guid> BuildPath(Dictionary<Guid, Guid> predecessor, Guid from, Guid target)
    {
        var path = new List<Guid> { target };
        var current = target;
        while (current != from)
        {
            current = predecessor[current];
            path.Add(current);
        }

        path.Reverse(); // from → … → target
        return path;
    }
}
