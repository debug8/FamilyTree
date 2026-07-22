namespace FamilyTree.Domain;

/// <summary>
/// Особа — центральна сутність родинного дерева (розд. 3.1 специфікації).
/// Ідентичність — за <see cref="Entity.Id"/>. Редаговані поля мають set;
/// зв'язки між особами зберігаються окремо (<see cref="ParentChildLink"/>, <see cref="SpouseLink"/>).
/// </summary>
public sealed class Person : Entity
{
    /// <summary>Прізвище (обов'язкове).</summary>
    public required string LastName { get; set; }

    /// <summary>Ім'я (обов'язкове).</summary>
    public required string FirstName { get; set; }

    /// <summary>Стать (обов'язкова — потрібна для назв родства).</summary>
    public required Gender Gender { get; set; }

    /// <summary>По батькові.</summary>
    public string? MiddleName { get; set; }

    /// <summary>Дівоче прізвище.</summary>
    public string? MaidenName { get; set; }

    /// <summary>Дата народження (може бути невідома).</summary>
    public DateOnly? BirthDate { get; set; }

    /// <summary>Місце народження.</summary>
    public string? BirthPlace { get; set; }

    /// <summary>Дата смерті (null — особа вважається живою).</summary>
    public DateOnly? DeathDate { get; set; }

    /// <summary>Відносний шлях до фото у папці даних застосунку.</summary>
    public string? PhotoPath { get; set; }

    /// <summary>Довільні нотатки.</summary>
    public string? Notes { get; set; }

    /// <summary>Час створення запису (аудит).</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Час останнього оновлення запису (аудит).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Обчислюване: особа жива, якщо не вказано дату смерті.</summary>
    public bool IsAlive => DeathDate is null;

    /// <summary>Зручне повне ім'я «Прізвище Ім'я По батькові» (для UI).</summary>
    public string FullName =>
        string.Join(' ', new[] { LastName, FirstName, MiddleName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
}
