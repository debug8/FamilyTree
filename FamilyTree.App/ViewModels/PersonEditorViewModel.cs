using System.ComponentModel.DataAnnotations;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyTree.App.Localization;
using FamilyTree.App.Services;
using FamilyTree.Domain;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// ViewModel діалогу створення/редагування особи (розд. 6.2).
/// Валідація через <see cref="ObservableValidator"/>: без прізвища/імені/статі зберегти не можна.
/// </summary>
public partial class PersonEditorViewModel : ObservableValidator
{
    private readonly Person? _existing;
    private readonly IDialogService? _dialogs;
    private readonly IPhotoStore? _photos;
    private readonly ILocalizationService? _localization;

    // Шлях до ЩОЙНО вибраного файлу (абсолютний, з диска користувача). Копіювання в
    // сховище відкладене до Commit() навмисно: інакше «вибрав фото — передумав —
    // Скасувати» лишало б у теці даних файл-сироту, на який ніхто не посилається.
    private string? _pickedSourcePath;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(PersonEditorViewModel), nameof(ValidateNamePresence))]
    private string? _lastName;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(PersonEditorViewModel), nameof(ValidateNamePresence))]
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
    private FamilyDate? _birthDate;

    [ObservableProperty]
    private string? _birthPlace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDeathDate))]
    private bool _isAlive = true;

    [ObservableProperty]
    private FamilyDate? _deathDate;

    [ObservableProperty]
    private string? _notes;

    // Відносний шлях, збережений в особі (null — фото немає або його прибрали).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhotoPreview))]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    [NotifyPropertyChangedFor(nameof(HasNoPhoto))]
    private string? _photoPath;

    /// <summary>Повідомлення про невдале додавання фото (null — усе гаразд).</summary>
    [ObservableProperty]
    private string? _photoError;

    /// <param name="dialogs">Потрібен для вибору файлу фото; без нього кнопка вимкнена.</param>
    /// <param name="photos">Сховище фото; без нього кнопка вимкнена.</param>
    /// <param name="localization">Тексти фільтра діалогу й повідомлення про помилку.</param>
    public PersonEditorViewModel(
        Person? existing = null,
        IDialogService? dialogs = null,
        IPhotoStore? photos = null,
        ILocalizationService? localization = null)
    {
        _existing = existing;
        _dialogs = dialogs;
        _photos = photos;
        _localization = localization;

        if (existing is not null)
        {
            _lastName = existing.LastName;
            _firstName = existing.FirstName;
            _middleName = existing.MiddleName;
            _maidenName = existing.MaidenName;
            _selectedGender = Genders.FirstOrDefault(g => g.Value == existing.Gender);
            _birthDate = existing.BirthDate;
            _birthPlace = existing.BirthPlace;
            _deathDate = existing.DeathDate;
            _isAlive = existing.DeathDate is null;
            _notes = existing.Notes;
            _photoPath = existing.PhotoPath;
        }

        // CanSave залежить від наявності помилок — оновлюємо його при зміні помилок.
        ErrorsChanged += (_, _) => OnPropertyChanged(nameof(CanSave));

        // Одразу перевіряємо, щоб у режимі створення Save був заблокований до заповнення.
        ValidateAllProperties();
    }

    public bool IsEditMode => _existing is not null;

    /// <summary>Показувати поле дати смерті (лише коли особа не жива).</summary>
    public bool ShowDeathDate => !IsAlive;

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
    /// Абсолютний шлях для показу прев'ю: щойно вибраний файл, інакше — той, що вже
    /// збережений в особі. Null — фото немає (або файл зник із теки даних).
    /// </summary>
    public string? PhotoPreview => _pickedSourcePath ?? _photos?.Resolve(PhotoPath);

    /// <summary>Чи є що показувати (керує видимістю прев'ю та кнопки «Прибрати»).</summary>
    public bool HasPhoto => PhotoPreview is not null;

    /// <summary>Зворотне до <see cref="HasPhoto"/> — для заглушки «фото немає».
    /// Окрема властивість, бо інвертувального конвертера в проєкті немає.</summary>
    public bool HasNoPhoto => !HasPhoto;

    /// <summary>Вибір фото доступний лише коли VM створено з сервісами (не в тестах/дизайнері).</summary>
    public bool CanPickPhoto => _dialogs is not null && _photos is not null;

    /// <summary>
    /// Вибирає файл фото. Сам файл копіюється у сховище лише при збереженні особи —
    /// див. коментар до <c>_pickedSourcePath</c>.
    /// </summary>
    [RelayCommand]
    private void PickPhoto()
    {
        if (_dialogs is null || _photos is null)
        {
            return;
        }

        var filter = _localization?.GetString("Photo_Filter") ?? "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp";
        if (_dialogs.AskOpenPath(filter) is not { } path)
        {
            return;
        }

        PhotoError = null;
        _pickedSourcePath = path;
        OnPropertyChanged(nameof(PhotoPreview));
        OnPropertyChanged(nameof(HasPhoto));
        OnPropertyChanged(nameof(HasNoPhoto));
    }

    /// <summary>
    /// Прибирає фото з особи. Сам файл у теці даних не видаляємо: на нього може
    /// посилатися інша особа, а місця він займає небагато.
    /// </summary>
    [RelayCommand]
    private void RemovePhoto()
    {
        _pickedSourcePath = null;
        PhotoError = null;
        PhotoPath = null;
    }

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

        var lastName = LastName?.Trim() ?? string.Empty;
        var firstName = FirstName?.Trim() ?? string.Empty;

        var person = _existing ?? new Person
        {
            LastName = lastName,
            FirstName = firstName,
            Gender = SelectedGender!.Value,
        };

        person.LastName = lastName;
        person.FirstName = firstName;
        person.Gender = SelectedGender!.Value;
        person.MiddleName = Normalize(MiddleName);
        person.MaidenName = Normalize(MaidenName);
        person.BirthDate = BirthDate;
        person.BirthPlace = Normalize(BirthPlace);
        person.DeathDate = IsAlive ? null : DeathDate;
        person.Notes = Normalize(Notes);
        person.PhotoPath = CommitPhoto();
        person.UpdatedAt = DateTime.UtcNow;

        Result = person;
        return person;
    }

    /// <summary>
    /// Переносить щойно вибраний файл у сховище й повертає відносний шлях. Якщо копіювання
    /// не вдалося (немає прав, файл зник, диск заповнений), особа зберігається БЕЗ нового
    /// фото зі старим значенням: втратити всі решту правок через фото було б непропорційно.
    /// </summary>
    private string? CommitPhoto()
    {
        if (_pickedSourcePath is null || _photos is null)
        {
            return PhotoPath;
        }

        try
        {
            PhotoPath = _photos.Import(_pickedSourcePath);
            _pickedSourcePath = null;
            return PhotoPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            PhotoError = _localization?.GetString("Photo_ImportFailed") ?? ex.Message;
            return PhotoPath;
        }
    }

    partial void OnIsAliveChanged(bool value)
    {
        if (value)
        {
            DeathDate = null;
        }
    }

    // Прізвище та ім'я перевіряються спільно: зміна одного має оновити помилку іншого.
    partial void OnLastNameChanged(string? value) => ValidateProperty(FirstName, nameof(FirstName));

    partial void OnFirstNameChanged(string? value) => ValidateProperty(LastName, nameof(LastName));

    /// <summary>
    /// Валідно, якщо заповнене хоча б одне з полів — прізвище або ім'я.
    /// </summary>
    public static ValidationResult? ValidateNamePresence(string? value, ValidationContext context)
    {
        var vm = (PersonEditorViewModel)context.ObjectInstance;
        if (!string.IsNullOrWhiteSpace(vm.LastName) || !string.IsNullOrWhiteSpace(vm.FirstName))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult("Person_Validation_NameRequired");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
