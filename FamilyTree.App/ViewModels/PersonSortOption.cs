using FamilyTree.App.Localization;

namespace FamilyTree.App.ViewModels;

/// <summary>Поле сортування списку осіб.</summary>
public enum PersonSortField
{
    LastName,
    FirstName,
    BirthDate,
}

/// <summary>Пункт вибору сортування для UI.</summary>
public sealed class PersonSortOption : LocalizedOption
{
    public PersonSortOption(PersonSortField field, string nameKey)
        : base(nameKey) => Field = field;

    public PersonSortField Field { get; }
}
