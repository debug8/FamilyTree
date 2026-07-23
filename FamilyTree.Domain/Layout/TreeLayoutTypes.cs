namespace FamilyTree.Domain.Layout;

/// <summary>Режим побудови дерева (розд. 5.1).</summary>
public enum TreeMode
{
    /// <summary>Предки: коренева особа знизу, батьки/діди — вгору.</summary>
    Ancestors,

    /// <summary>Нащадки: коренева особа зверху, діти/онуки — вниз (з подружжям поруч).</summary>
    Descendants,

    /// <summary>Усі родичі: увесь зв'язний компонент, поколіннями.</summary>
    FullRelatives,
}

/// <summary>Тип ребра дерева.</summary>
public enum EdgeKind
{
    ParentChild,
    Spouse,
}

/// <summary>
/// Розкладка одного вузла: центр картки в умовних одиницях (без WPF-типів).
/// </summary>
public sealed record NodeLayout(Guid PersonId, double X, double Y, int Level);

/// <summary>Ребро між двома вузлами.</summary>
public sealed record EdgeLayout(Guid FromId, Guid ToId, EdgeKind Kind);

/// <summary>Повна розкладка дерева: вузли, ребра та габарити області.</summary>
public sealed record TreeLayout(
    IReadOnlyList<NodeLayout> Nodes,
    IReadOnlyList<EdgeLayout> Edges,
    double Width,
    double Height)
{
    public static TreeLayout Empty { get; } =
        new(Array.Empty<NodeLayout>(), Array.Empty<EdgeLayout>(), 0, 0);
}
