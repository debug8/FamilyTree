namespace FamilyTree.Domain.Layout;

/// <summary>
/// T-4.1 — розрахунок координат вузлів дерева (розд. 5.2). Чиста математика, без WPF-типів.
/// Предки/нащадки — пошарова розкладка з центруванням батьків над дітьми та подружжям поруч;
/// повний режим — рядкове пакування за поколіннями (евристика MVP, розд. 10).
/// </summary>
public sealed class TreeLayoutEngine
{
    // Розміри картки й проміжки в умовних одиницях (рендерер масштабує).
    public const double NodeWidth = 160;
    public const double NodeHeight = 72;
    public const double HorizontalGap = 28;
    public const double VerticalGap = 90;

    public const double ColumnStep = NodeWidth + HorizontalGap;
    public const double RowStep = NodeHeight + VerticalGap;

    private const double LeafGap = 1.0;   // проміжок (у колонках) між сусідніми піддеревами
    private const double MinColGap = 1.0; // мінімальна відстань між вузлами одного рівня

    public TreeLayout Build(FamilyGraph graph, Guid rootId, TreeMode mode, int maxDepth = 0)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!graph.Contains(rootId))
        {
            return TreeLayout.Empty;
        }

        var depthLimit = maxDepth <= 0 ? int.MaxValue : maxDepth;
        var positions = mode switch
        {
            TreeMode.Ancestors => BuildTree(graph, rootId, depthLimit, ancestors: true),
            TreeMode.Descendants => BuildTree(graph, rootId, depthLimit, ancestors: false),
            _ => BuildFull(graph, rootId, depthLimit),
        };

        ResolveOverlaps(positions);
        return Finalize(graph, positions);
    }

    // ---- Режими предків/нащадків: unit-дерево з центруванням --------------

    private static Dictionary<Guid, (double Col, int Depth)> BuildTree(
        FamilyGraph graph, Guid rootId, int depthLimit, bool ancestors)
    {
        var visited = new HashSet<Guid>();
        var root = ancestors
            ? BuildAncestorUnit(graph, rootId, 0, depthLimit, visited)
            : BuildDescendantUnit(graph, rootId, 0, depthLimit, visited);

        var ctx = new LayoutContext();
        PlaceUnit(root, 0, ctx);

        if (ancestors)
        {
            // Предки: інвертувати рівні, щоб корінь опинився знизу.
            foreach (var id in ctx.Positions.Keys.ToList())
            {
                var (col, depth) = ctx.Positions[id];
                ctx.Positions[id] = (col, ctx.MaxDepth - depth);
            }
        }

        return ctx.Positions;
    }

    private static Unit BuildDescendantUnit(FamilyGraph graph, Guid personId, int depth, int depthLimit, HashSet<Guid> visited)
    {
        visited.Add(personId);
        var unit = new Unit();
        unit.Persons.Add(personId);

        var spouse = graph.GetSpouses(personId).FirstOrDefault(s => !visited.Contains(s.Id));
        if (spouse is not null)
        {
            visited.Add(spouse.Id);
            unit.Persons.Add(spouse.Id);
        }

        if (depth < depthLimit)
        {
            foreach (var child in graph.GetChildren(personId).Where(c => !visited.Contains(c.Id)))
            {
                unit.Children.Add(BuildDescendantUnit(graph, child.Id, depth + 1, depthLimit, visited));
            }
        }

        return unit;
    }

    private static Unit BuildAncestorUnit(FamilyGraph graph, Guid personId, int depth, int depthLimit, HashSet<Guid> visited)
    {
        visited.Add(personId);
        var unit = new Unit();
        unit.Persons.Add(personId);

        if (depth < depthLimit)
        {
            foreach (var parent in graph.GetParents(personId).Where(p => !visited.Contains(p.Id)))
            {
                unit.Children.Add(BuildAncestorUnit(graph, parent.Id, depth + 1, depthLimit, visited));
            }
        }

        return unit;
    }

    private static double PlaceUnit(Unit unit, int depth, LayoutContext ctx)
    {
        ctx.MaxDepth = Math.Max(ctx.MaxDepth, depth);

        if (unit.Children.Count == 0)
        {
            var startCol = ctx.Cursor;
            for (var i = 0; i < unit.Persons.Count; i++)
            {
                ctx.Positions[unit.Persons[i]] = (startCol + i, depth);
            }

            ctx.Cursor = startCol + unit.Persons.Count + LeafGap;
            return startCol + (unit.Persons.Count - 1) / 2.0;
        }

        var childCenters = unit.Children.Select(c => PlaceUnit(c, depth + 1, ctx)).ToList();
        var center = (childCenters[0] + childCenters[^1]) / 2.0;
        var start = center - (unit.Persons.Count - 1) / 2.0;
        for (var i = 0; i < unit.Persons.Count; i++)
        {
            ctx.Positions[unit.Persons[i]] = (start + i, depth);
        }

        return center;
    }

    // ---- Повний режим: рядкове пакування за поколіннями --------------------

    private static Dictionary<Guid, (double Col, int Depth)> BuildFull(FamilyGraph graph, Guid rootId, int depthLimit)
    {
        // Покоління через BFS: батько −1, дитина +1, подружжя 0.
        var generation = new Dictionary<Guid, int> { [rootId] = 0 };
        var order = new List<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var gen = generation[current];

            void Visit(Guid id, int g)
            {
                if (Math.Abs(g) > depthLimit || generation.ContainsKey(id))
                {
                    return;
                }

                generation[id] = g;
                order.Add(id);
                queue.Enqueue(id);
            }

            foreach (var parent in graph.GetParents(current))
            {
                Visit(parent.Id, gen - 1);
            }

            foreach (var child in graph.GetChildren(current))
            {
                Visit(child.Id, gen + 1);
            }

            foreach (var spouse in graph.GetSpouses(current))
            {
                Visit(spouse.Id, gen);
            }
        }

        var minGen = generation.Values.Min();

        // Групуємо по поколіннях, у кожному кладемо подружжя поруч.
        var positions = new Dictionary<Guid, (double Col, int Depth)>();
        foreach (var group in generation.Keys.GroupBy(id => generation[id]))
        {
            var ordered = OrderKeepingSpousesTogether(graph, group.OrderBy(id => order.IndexOf(id)).ToList());
            var depth = group.Key - minGen;
            for (var i = 0; i < ordered.Count; i++)
            {
                positions[ordered[i]] = (i, depth);
            }
        }

        return positions;
    }

    private static List<Guid> OrderKeepingSpousesTogether(FamilyGraph graph, List<Guid> ids)
    {
        var set = new HashSet<Guid>(ids);
        var placed = new HashSet<Guid>();
        var result = new List<Guid>();

        foreach (var id in ids)
        {
            if (!placed.Add(id))
            {
                continue;
            }

            result.Add(id);
            foreach (var spouse in graph.GetSpouses(id).Where(s => set.Contains(s.Id) && !placed.Contains(s.Id)))
            {
                placed.Add(spouse.Id);
                result.Add(spouse.Id);
            }
        }

        return result;
    }

    // ---- Спільне завершення -----------------------------------------------

    private static void ResolveOverlaps(Dictionary<Guid, (double Col, int Depth)> positions)
    {
        foreach (var level in positions.GroupBy(p => p.Value.Depth).ToList())
        {
            var ordered = level.OrderBy(p => p.Value.Col).ToList();
            var previous = double.NegativeInfinity;
            foreach (var entry in ordered)
            {
                var col = entry.Value.Col;
                if (col < previous + MinColGap)
                {
                    col = previous + MinColGap;
                }

                positions[entry.Key] = (col, entry.Value.Depth);
                previous = col;
            }
        }
    }

    private static TreeLayout Finalize(FamilyGraph graph, Dictionary<Guid, (double Col, int Depth)> positions)
    {
        if (positions.Count == 0)
        {
            return TreeLayout.Empty;
        }

        var minCol = positions.Values.Min(p => p.Col);
        var nodes = positions
            .Select(kvp => new NodeLayout(
                kvp.Key,
                (kvp.Value.Col - minCol) * ColumnStep,
                kvp.Value.Depth * RowStep,
                kvp.Value.Depth))
            .ToList();

        var placed = new HashSet<Guid>(positions.Keys);
        var edges = new List<EdgeLayout>();

        foreach (var id in placed)
        {
            foreach (var child in graph.GetChildren(id))
            {
                if (placed.Contains(child.Id))
                {
                    edges.Add(new EdgeLayout(id, child.Id, EdgeKind.ParentChild));
                }
            }

            foreach (var spouse in graph.GetSpouses(id))
            {
                if (placed.Contains(spouse.Id) && id.CompareTo(spouse.Id) < 0)
                {
                    edges.Add(new EdgeLayout(id, spouse.Id, EdgeKind.Spouse));
                }
            }
        }

        var width = nodes.Max(n => n.X) + NodeWidth;
        var height = nodes.Max(n => n.Y) + NodeHeight;
        return new TreeLayout(nodes, edges, width, height);
    }

    private sealed class Unit
    {
        public List<Guid> Persons { get; } = new();

        public List<Unit> Children { get; } = new();
    }

    private sealed class LayoutContext
    {
        public double Cursor { get; set; }

        public int MaxDepth { get; set; }

        public Dictionary<Guid, (double Col, int Depth)> Positions { get; } = new();
    }
}
