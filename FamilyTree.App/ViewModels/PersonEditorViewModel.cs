using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.Domain;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// ViewModel діалогу створення/редагування особи (розд. 6.2).
/// Валідація через <see cref="ObservableValidator"/>: без прізвища/імені/статі зберегти не можна.
/// </summary>
public partial class PersonEditorViewModel : ObservableValidator
{
    private readonly Person? _existing;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(AllowEmptyStrings = false)]
    private string? _lastName;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(AllowEmptyStrings = false)]
    private string? _firstName;

    [ObservableProperty]
    private string? _middleName;

    [ObservableProperty]
    private string? _maidenName;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required]
    private GenderOption? _selectedGender;

    [ObservableProperty]
    private DateTime? _birthDate;

    [ObservableProperty]
    private string? _birthPlace;

    [ObservableProperty]
    private DateTime? _deathDate;

    [ObservableProperty]
    private string? _notes;

    public PersonEditorViewModel(Person? existing = null)
    {
        _existing = existing;

        if (existing is not null)
        {
            _lastName = existing.LastName;
            _firstName = existing.FirstName;
            _middleName = existing.MiddleName;
            _maidenName = existing.MaidenName;
            _selectedGender = Genders.FirstOrDefault(g => g.Value == existing.Gender);
            _birthDate = ToDateTime(existing.BirthDate);
            _birthPlace = existing.BirthPlace;
            _deathDate = ToDateTime(existing.DeathDate);
            _notes = existing.Notes;
        }

        // CanSave залежить від наявності помилок — оновлюємо його при зміні помилок.
        ErrorsChanged += (_, _) => OnPropertyChanged(nameof(CanSave));

        // Одразу перевіряємо, щоб у режимі створення Save був заблокований до заповнення.
        ValidateAllProperties();
    }

    public bool IsEditMode => _existing is not null;

    /// <summary>Результат після успішного збереження (особа, створена чи оновлена).</summary>
    public Person? Result { get; private set; }

    /// <summary>Ключ заголовка діалогу (створення / редагування).</summary>
    public string TitleKey => IsEditMode ? "Person_Editor_Title_Edit" : "Person_Editor_Title_New";

    public IReadOnlyList<GenderOption> Genders { get; } = new[]
    {
        new GenderOption(Gender.Male, "Gender_Male"),
        new GenderOption(Gender.Female, "Gender_Female"),
        new GenderOption(Gender.Unknown, "Gender_Unknown"),
    };

    /// <summary>Чи можна зберегти (немає помилок валідації).</summary>
    public bool CanSave => !HasErrors;

    /// <summary>
    /// Перевіряє й застосовує зміни. Повертає збережену особу або null, якщо є помилки.
    /// </summary>
    public Person? Commit()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            return null;
        }

        var person = _existing ?? new Person
        {
            LastName = LastName!,
            FirstName = FirstName!,
            Gender = SelectedGender!.Value,
        };

        person.LastName = LastName!.Trim();
        person.FirstName = FirstName!.Trim();
        person.Gender = SelectedGender!.Value;
        person.MiddleName = Normalize(MiddleName);
        person.MaidenName = Normalize(MaidenName);
        person.BirthDate = ToDateOnly(BirthDate);
        person.BirthPlace = Normalize(BirthPlace);
        person.DeathDate = ToDateOnly(DeathDate);
        person.Notes = Normalize(Notes);
        person.UpdatedAt = DateTime.UtcNow;

        Result = person;
        return person;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? ToDateTime(DateOnly? date) =>
        date is { } d ? d.ToDateTime(TimeOnly.MinValue) : null;

    private static DateOnly? ToDateOnly(DateTime? date) =>
        date is { } d ? DateOnly.FromDateTime(d) : null;
}
