namespace FamilyTree.Domain.Validation;

/// <summary>
/// Результат валідації: жорсткі помилки (блокують дію) та м'які попередження
/// (не блокують, але показуються користувачу).
/// </summary>
public sealed class ValidationResult
{
    public static ValidationResult Valid { get; } = new(
        Array.Empty<ValidationMessage>(),
        Array.Empty<ValidationMessage>());

    public ValidationResult(IReadOnlyList<ValidationMessage> errors, IReadOnlyList<ValidationMessage> warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }

    public IReadOnlyList<ValidationMessage> Errors { get; }

    public IReadOnlyList<ValidationMessage> Warnings { get; }

    /// <summary>Дію дозволено (немає жорстких помилок). Попередження не впливають.</summary>
    public bool IsValid => Errors.Count == 0;

    public bool HasWarnings => Warnings.Count > 0;
}
