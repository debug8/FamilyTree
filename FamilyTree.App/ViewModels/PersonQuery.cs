using FamilyTree.Domain;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Спільні пошук і сортування списків осіб. Виділено окремо, щоб ліва панель
/// і діалог вибору родича поводилися однаково: один і той самий порядок та
/// одні правила зіставлення тексту (інакше «те саме» сортування розповзається
/// між екранами при кожній правці).
/// </summary>
internal static class PersonQuery
{
    /// <summary>
    /// Чи підходить особа під пошуковий запит. Перевіряються всі імена:
    /// прізвище, ім'я, по батькові та дівоче прізвище.
    /// </summary>
    public static bool Matches(Person person, string term) =>
        Contains(person.LastName, term)
        || Contains(person.FirstName, term)
        || Contains(person.MiddleName, term)
        || Contains(person.MaidenName, term);

    /// <summary>Сортує осіб за вибраним полем і напрямком.</summary>
    public static List<Person> Sort(IEnumerable<Person> source, PersonSortField field, bool descending) =>
        (field switch
        {
            PersonSortField.FirstName => Direction(source, p => p.FirstName, StringComparer.CurrentCulture, descending)
                .ThenBy(p => p.LastName, StringComparer.CurrentCulture),

            // Невідома дата народження завжди в кінці списку — незалежно від напрямку.
            PersonSortField.BirthDate => Direction(
                    source,
                    p => p.BirthDate ?? (descending ? DateOnly.MinValue : DateOnly.MaxValue),
                    Comparer<DateOnly>.Default,
                    descending)
                .ThenBy(p => p.LastName, StringComparer.CurrentCulture),

            _ => Direction(source, p => p.LastName, StringComparer.CurrentCulture, descending)
                .ThenBy(p => p.FirstName, StringComparer.CurrentCulture),
        }).ToList();

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.CurrentCultureIgnoreCase);

    private static IOrderedEnumerable<Person> Direction<TKey>(
        IEnumerable<Person> source, Func<Person, TKey> key, IComparer<TKey> comparer, bool descending) =>
        descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);
}
