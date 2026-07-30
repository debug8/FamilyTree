using FamilyTree.App.Localization;
using FamilyTree.Domain;

namespace FamilyTree.App.ViewModels;

/// <summary>Фільтр за статтю. <c>null</c> — будь-яка стать.</summary>
public sealed class GenderFilterOption : LocalizedOption
{
    public GenderFilterOption(Gender? value, string nameKey)
        : base(nameKey) => Value = value;

    public Gender? Value { get; }
}

/// <summary>Стан життя для фільтра списку осіб.</summary>
public enum LifeStatus
{
    /// <summary>Без обмеження.</summary>
    Any,

    /// <summary>Лише живі (без дати смерті).</summary>
    Alive,

    /// <summary>Лише померлі (з датою смерті).</summary>
    Deceased,
}

/// <summary>Фільтр за станом життя.</summary>
public sealed class LifeStatusFilterOption : LocalizedOption
{
    public LifeStatusFilterOption(LifeStatus value, string nameKey)
        : base(nameKey) => Value = value;

    public LifeStatus Value { get; }
}

/// <summary>
/// Спільні набори пунктів фільтрів і сортування. Екземпляри статичні, бо
/// <see cref="LocalizedOption"/> підписується на зміну мови — створювати їх
/// на кожне відкриття діалогу означало б накопичувати підписки.
/// </summary>
public static class PersonFilterOptions
{
    public static IReadOnlyList<PersonSortOption> Sorts { get; } = new[]
    {
        new PersonSortOption(PersonSortField.LastName, "Sort_LastName"),
        new PersonSortOption(PersonSortField.FirstName, "Sort_FirstName"),
        new PersonSortOption(PersonSortField.BirthDate, "Sort_BirthDate"),
    };

    public static IReadOnlyList<GenderFilterOption> Genders { get; } = new[]
    {
        new GenderFilterOption(null, "Filter_Any"),
        new GenderFilterOption(Gender.Male, "Gender_Male"),
        new GenderFilterOption(Gender.Female, "Gender_Female"),
        new GenderFilterOption(Gender.Unknown, "Gender_Unknown"),
    };

    public static IReadOnlyList<LifeStatusFilterOption> LifeStatuses { get; } = new[]
    {
        // Окремий ключ від Filter_Any: українською прикметник узгоджується з іменником
        // («будь-яка стать», але «будь-який стан»).
        new LifeStatusFilterOption(LifeStatus.Any, "Filter_AnyStatus"),
        new LifeStatusFilterOption(LifeStatus.Alive, "Filter_Alive"),
        new LifeStatusFilterOption(LifeStatus.Deceased, "Filter_Deceased"),
    };
}
