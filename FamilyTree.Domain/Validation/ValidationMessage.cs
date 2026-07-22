namespace FamilyTree.Domain.Validation;

/// <summary>
/// Одне повідомлення валідації: ключ ресурсу + необов'язкові аргументи для форматування
/// (напр. імена осіб). Текст формується шаром App: <c>string.Format(GetString(Key), Arguments)</c>.
/// </summary>
public sealed record ValidationMessage(string Key, IReadOnlyList<object?> Arguments)
{
    public static ValidationMessage Of(string key, params object?[] args) =>
        new(key, args ?? Array.Empty<object?>());
}
