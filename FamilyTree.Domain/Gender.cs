namespace FamilyTree.Domain;

/// <summary>
/// Стать особи. Потрібна для назв родинних зв'язків (дядько/тітка тощо).
/// </summary>
public enum Gender
{
    /// <summary>Стать невідома чи не вказана — використовуються нейтральні назви родства.</summary>
    Unknown = 0,
    Male = 1,
    Female = 2,
}
