namespace FamilyTree.App.ViewModels;

/// <summary>Поле сортування списку осіб.</summary>
public enum PersonSortField
{
    LastName,
    FirstName,
    BirthDate,
}

/// <summary>Пункт вибору сортування для UI.</summary>
public sealed record PersonSortOption(PersonSortField Field, string NameKey);
