namespace FamilyTree.Domain;

/// <summary>
/// Тип батьківського зв'язку. На старті використовується лише <see cref="Biological"/>,
/// але enum закладено одразу (див. інваріанти родства — «один біологічний батько/мати»).
/// </summary>
public enum ParentRole
{
    Biological = 0,
    Adoptive = 1,
    Step = 2,
}
