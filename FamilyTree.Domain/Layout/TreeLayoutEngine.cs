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
    public const double NodeHeight = 80;
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

        // Усі партнери, а не лише перший. Раніше тут був FirstOrDefault, тож особа
        // з двома шлюбами показувалася поруч із випадковим (першим у файлі) партнером,
        // а решта не потрапляла ні у visited, ні в positions — тобто зникала з полотна,
        // і Finalize не малював до неї ребра, хоч діти під нею були саме від неї.
        var partners = new List<Guid>();
        foreach (var spouse in graph.GetSpouses(personId))
        {
            if (visited.Add(spouse.Id))
            {
                partners.Add(spouse.Id);
            }
        }

        // Рядок юніта: один партнер ліворуч від особи, решта — праворуч.
        // Так у типовому випадку повторного шлюбу обидві пари лишаються сусідніми
        // (S1 — Особа — S2), і рамки шлюбів не розтягуються через усе полотно.
        if (partners.Count > 0)
        {
            unit.Persons.Add(partners[0]);
        }

        unit.Persons.Add(personId);

        for (var i = 1; i < partners.Count; i++)
        {
            unit.Persons.Add(partners[i]);
        }

        if (depth >= depthLimit)
        {
            return unit;
        }

        foreach (var childId in OrderChildrenByParentCouple(graph, personId, partners))
        {
            if (visited.Contains(childId))
            {
                continue;
            }

            unit.Children.Add(BuildDescendantUnit(graph, childId, depth + 1, depthLimit, visited));
        }

        return unit;
    }

    /// <summary>
    /// Порядок дітей за парою батьків: спершу спільні з лівим партнером, потім ті, чий
    /// другий батько невідомий (або не є партнером), далі — діти з рештою партнерів.
    /// Це вирівнює групи дітей під відповідними парами й зменшує перетини ребер.
    /// Дитина має не більше двох батьків, тож у жодну групу не потрапляє двічі.
    /// </summary>
    private static List<Guid> OrderChildrenByParentCouple(FamilyGraph graph, Guid personId, List<Guid> partners)
    {
        var children = graph.GetChildren(personId).Select(c => c.Id).ToList();
        if (partners.Count == 0 || children.Count <= 1)
        {
            return children;
        }

        var byPartner = new Dictionary<Guid, List<Guid>>();
        var withoutPartner = new List<Guid>();

        foreach (var childId in children)
        {
            var otherParent = graph.GetParents(childId)
                .FirstOrDefault(p => p.Id != personId && partners.Contains(p.Id));

            if (otherParent is null)
            {
                withoutPartner.Add(childId);
                continue;
            }

            if (!byPartner.TryGetValue(otherParent.Id, out var group))
            {
                byPartner[otherParent.Id] = group = new List<Guid>();
            }

            group.Add(childId);
        }

        var ordered = new List<Guid>(children.Count);

        if (byPartner.TryGetValue(partners[0], out var leftGroup))
        {
            ordered.AddRange(leftGroup);
        }

        ordered.AddRange(withoutPartner);

        for (var i = 1; i < partners.Count; i++)
        {
            if (byPartner.TryGetValue(partners[i], out var group))
            {
                ordered.AddRange(group);
            }
        }

        return ordered;
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

        // Індекс порядку обходу мапою, а не order.IndexOf: IndexOf — це O(n) з лінійним
        // порівнянням Guid у ключі сортування, тобто O(n²) на все покоління.
        var orderIndex = new Dictionary<Guid, int>(order.Count);
        for (var i = 0; i < order.Count; i++)
        {
            orderIndex[order[i]] = i;
        }

        // Групуємо по поколіннях, у кожному кладемо подружжя поруч.
        var positions = new Dictionary<Guid, (double Col, int Depth)>();
        foreach (var group in generation.Keys.GroupBy(id => generation[id]))
        {
            var ordered = OrderKeepingSpousesTogether(graph, group.OrderBy(id => orderIndex[id]).ToList());
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

            // Особа стоїть МІЖ подружжям: усі колишні ліворуч, усі чинні праворуч.
            // Так рамка чинного шлюбу охоплює лише особу з чинним партнером (сусіднім
            // праворуч), а пунктир до колишнього йде по інший бік і не перетинає картку
            // чинного. Порядок у межах кожної групи зберігаємо (стабільний поділ за списком).
            var spouses = graph.GetSpouses(id)
                .Where(s => set.Contains(s.Id) && !placed.Contains(s.Id))
                .ToList();

            foreach (var spouse in spouses.Where(s => !graph.IsSpouseActive(id, s.Id)))
            {
                placed.Add(spouse.Id);
                result.Add(spouse.Id);
            }

            result.Add(id);

            foreach (var spouse in spouses.Where(s => graph.IsSpouseActive(id, s.Id)))
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
            // ThenBy(Key) — щоб при однакових колонках результат не залежав від порядку
            // перебору Dictionary (інакше розсування накладань було невідтворюваним).
            var ordered = level.OrderBy(p => p.Value.Col).ThenBy(p => p.Key).ToList();
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

        // Явне сортування: порядок перебору Dictionary/HashSet контрактом не визначений,
        // тож розкладка була відтворюваною лише для однакового порядку вставки. Це робило
        // неможливими snapshot-тести й давало нестабільний z-order при рендерингу.
        var nodes = positions
            .OrderBy(kvp => kvp.Value.Depth)
            .ThenBy(kvp => kvp.Value.Col)
            .ThenBy(kvp => kvp.Key)
            .Select(kvp => new NodeLayout(
                kvp.Key,
                (kvp.Value.Col - minCol) * ColumnStep,
                kvp.Value.Depth * RowStep,
                kvp.Value.Depth))
            .ToList();

        var placed = new HashSet<Guid>(positions.Keys);
        var edges = new List<EdgeLayout>();

        foreach (var node in nodes)
        {
            var id = node.PersonId;

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
