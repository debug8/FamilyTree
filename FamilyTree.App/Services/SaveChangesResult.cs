namespace FamilyTree.App.Services;

/// <summary>Вибір користувача в запиті про незбережені зміни.</summary>
public enum SaveChangesResult
{
    /// <summary>Зберегти зміни й продовжити.</summary>
    Save,

    /// <summary>Відкинути зміни й продовжити.</summary>
    Discard,

    /// <summary>Скасувати дію.</summary>
    Cancel,
}
