using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.Domain;

namespace FamilyTree.App.ViewModels;

/// <summary>Пункт вибору виду факту (значення + ключ локалізованої назви).</summary>
public sealed record PersonFactKindOption(PersonFactKind Value, string NameKey);

/// <summary>
/// Рядок редактора життєвих фактів. Проміжок між незмінним <see cref="PersonFact"/>
/// і полями діалогу: у record'а немає сеттерів, тож редагувати його прив'язкою не можна.
/// </summary>
/// <remarks>
/// <see cref="PersonFactKind.Other"/> у списку вибору немає навмисно. Такий факт
/// з'являється лише з файлу, збереженого новішою збіркою, і власного тега в GEDCOM
/// не має — тобто експорт його не збереже. Дати користувачу створювати запис, який
/// мовчки зникне при обміні, було б пасткою. Рядок із чужим видом показується як є
/// (замість списку — його власна назва) і повертається у файл незміненим.
/// </remarks>
public sealed partial class PersonFactRow : ObservableObject
{
    /// <summary>Види, доступні для вибору.</summary>
    public static IReadOnlyList<PersonFactKindOption> Kinds { get; } = new[]
    {
        new PersonFactKindOption(PersonFactKind.Occupation, "PersonFactKind_Occupation"),
        new PersonFactKindOption(PersonFactKind.Residence, "PersonFactKind_Residence"),
    };

    // Оригінальна назва чужого виду. Зберігається окремо від списку вибору, бо
    // саме вона й робить round-trip точним.
    private readonly string? _label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKnownKind))]
    [NotifyPropertyChangedFor(nameof(IsCustomKind))]
    private PersonFactKindOption? _selectedKind;

    [ObservableProperty]
    private string? _value;

    [ObservableProperty]
    private string? _place;

    [ObservableProperty]
    private FamilyDate? _date;

    /// <param name="fact">Наявний факт для редагування; <c>null</c> — новий рядок.</param>
    public PersonFactRow(PersonFact? fact = null)
    {
        if (fact is null)
        {
            _selectedKind = Kinds[0];
            return;
        }

        _label = fact.Label;

        // Для Other збігу не буде — SelectedKind лишиться null, і рядок покаже
        // власну назву виду замість списку.
        _selectedKind = Kinds.FirstOrDefault(kind => kind.Value == fact.Kind);
        _value = fact.Value;
        _place = fact.Place;
        _date = fact.Date;
    }

    /// <summary>Список для прив'язки: ті самі <see cref="Kinds"/>, але доступні з екземпляра.</summary>
    public IReadOnlyList<PersonFactKindOption> KindOptions => Kinds;

    /// <summary>Вид відомий цій збірці — показуємо список вибору.</summary>
    public bool IsKnownKind => SelectedKind is not null;

    /// <summary>Вид із новішої збірки — показуємо його назву текстом, без вибору.</summary>
    public bool IsCustomKind => SelectedKind is null;

    /// <summary>Назва чужого виду для показу.</summary>
    public string CustomKindText => _label ?? string.Empty;

    /// <summary>Зібрати доменний факт із полів рядка.</summary>
    public PersonFact ToFact() => new()
    {
        Kind = SelectedKind?.Value ?? PersonFactKind.Other,

        // Label має сенс лише для чужого виду: якщо користувач перемкнув рядок
        // на «Професію», стара назва більше нічого не описує.
        Label = SelectedKind is null ? _label : null,
        Value = Normalize(Value),
        Place = Normalize(Place),
        Date = Date,
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
