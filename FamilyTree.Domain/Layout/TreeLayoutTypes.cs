namespace FamilyTree.Domain.Layout;

/// <summary>Режим побудови дерева (розд. 5.1).</summary>
public enum TreeMode
{
    /// <summary>Предки: коренева особа знизу, батьки/діди — вгору.</summary>
    Ancestors,

    /// <summary>Нащадки: коренева особа зверху, діти/онуки — вниз (з подружжям поруч).</summary>
    Descendants,

    /// <summary>
    /// Усі: увесь зв'язний компонент, поколіннями. Через шлюби він розповзається й на
    /// сторонніх людей (рідня чоловіка сестри дружини), тому режим і називається «Усі».
    /// </summary>
    FullRelatives,

    /// <summary>
    /// Лише родичі: той самий обхід, що й «Усі», але обмежений набором осіб, для яких
    /// ядро спорідненості дає назву зв'язку (плюс подружжя за налаштуваннями фільтра).
    /// Набір рахує <c>RelativeFilter</c> у шарі Kinship і передає сюди — розкладка
    /// свідомо нічого не знає про спорідненість.
    /// </summary>
    RelativesOnly,
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
