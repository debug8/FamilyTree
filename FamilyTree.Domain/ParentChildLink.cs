namespace FamilyTree.Domain;

/// <summary>
/// Спрямоване ребро «батько/мати → дитина» (розд. 3.2).
/// </summary>
public sealed class ParentChildLink : Entity
{
    /// <summary>Ідентифікатор батька/матері.</summary>
    public required Guid ParentId { get; init; }

    /// <summary>Ідентифікатор дитини.</summary>
    public required Guid ChildId { get; init; }

    /// <summary>Тип зв'язку. За замовчуванням — біологічний.</summary>
    public ParentRole ParentRole { get; set; } = ParentRole.Biological;

    /// <summary>Чи стосується це ребро вказаної особи (як батька чи як дитини).</summary>
    public bool Involves(Guid personId) => ParentId == personId || ChildId == personId;
}
