namespace FamilyTree.Domain.Validation;

/// <summary>
/// Ключі ресурсів для повідомлень валідації. Це саме ключі (не тексти) — домен
/// не містить рядків для користувача; локалізацію робить шар App за цими ключами.
/// Відповідні рядки мають існувати у Strings.resx / Strings.en.resx.
/// </summary>
public static class ValidationKeys
{
    // Жорсткі помилки
    public const string SelfParent = "Validation_SelfParent";
    public const string SelfSpouse = "Validation_SelfSpouse";
    public const string DuplicateParentChild = "Validation_DuplicateParentChild";
    public const string DuplicateSpouse = "Validation_DuplicateSpouse";
    public const string CycleDetected = "Validation_CycleDetected";
    public const string SecondBiologicalFather = "Validation_SecondBiologicalFather";
    public const string SecondBiologicalMother = "Validation_SecondBiologicalMother";

    // М'які попередження
    public const string ChildBornBeforeParentAdult = "Validation_ChildBornBeforeParentAdult";
    public const string ParentYoungerThanChild = "Validation_ParentYoungerThanChild";
    public const string DeathBeforeBirth = "Validation_DeathBeforeBirth";
}
