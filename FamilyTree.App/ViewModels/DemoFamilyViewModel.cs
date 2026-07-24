using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.Domain.Seeding;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Параметри діалогу «Створити демо-родину» (T-5.5): кількість поколінь і осіб,
/// дітей на пару, перемикачі складних зв'язків, розлучень і насіння генератора.
/// </summary>
public partial class DemoFamilyViewModel : ObservableObject
{
    [ObservableProperty]
    private int _generations = 4;

    [ObservableProperty]
    private int _maxPersons = 40;

    [ObservableProperty]
    private int _maxChildrenPerCouple = 4;

    [ObservableProperty]
    private bool _complexRelations = true;

    [ObservableProperty]
    private bool _includeDivorces = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeedEnabled))]
    private bool _randomSeed = true;

    [ObservableProperty]
    private int _seed = 42;

    // Межі повзунків (беруться з доменних констант, щоб не розходитися з нормалізацією).
    public int MinGenerations => DemoFamilyOptions.MinGenerations;

    public int MaxGenerations => DemoFamilyOptions.MaxGenerations;

    public int MinPersons => DemoFamilyOptions.MinPersons;

    /// <summary>Практична стеля для повзунка (домен допускає більше, але демо стільки не потребує).</summary>
    public int MaxPersonsBound => 500;

    public int MinChildren => DemoFamilyOptions.MinChildrenPerCouple;

    public int MaxChildren => DemoFamilyOptions.MaxChildrenPerCoupleLimit;

    /// <summary>Поле насіння активне лише коли вимкнено «випадкове».</summary>
    public bool SeedEnabled => !RandomSeed;

    /// <summary>Формує доменні параметри генерації з поточного стану діалогу.</summary>
    public DemoFamilyOptions ToOptions() => new()
    {
        Generations = Generations,
        MaxPersons = MaxPersons,
        MaxChildrenPerCouple = MaxChildrenPerCouple,
        ComplexRelations = ComplexRelations,
        IncludeDivorces = IncludeDivorces,
        Seed = RandomSeed ? null : Seed,
    };
}
