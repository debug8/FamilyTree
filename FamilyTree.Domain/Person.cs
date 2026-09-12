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

    /// <summary>Дата народження (може бути невідома; неточна — див. <see cref="FamilyDate"/>, T-5.2a).</summary>
    public FamilyDate? BirthDate { get; set; }

    /// <summary>Місце народження.</summary>
    public string? BirthPlace { get; set; }

    /// <summary>Дата смерті (null — особа вважається живою; неточна — <see cref="FamilyDate"/>, T-5.2a).</summary>
    public FamilyDate? DeathDate { get; set; }

    /// <summary>Відносний шлях до фото у папці даних застосунку.</summary>
    public string? PhotoPath { get; set; }

    /// <summary>
    /// Зменшена копія фото (JPEG, ~100 px по більшій стороні), яка зберігається
    /// ВСЕРЕДИНІ файлу документа — щоб надісланий родичу `.familytree` показував людей
    /// із обличчями, а не порожніми рамками.
    /// <para>
    /// У звичайному файлі поля немає: його заповнює лише команда «Зберегти копію з фото».
    /// Причина — розмір: base64 роздуває дані на третину, і мініатюра на 5 КБ дає ~7 000
    /// символів, тоді як уся решта даних про особу — близько 700 байт. Постійно тримати
    /// це у файлі означало б втратити читаний, придатний до diff і grep JSON.
    /// </para>
    /// <para>
    /// Оригінал фото лишається у теці даних у повній якості; мініатюра — лише запасний
    /// варіант для показу, коли файлу поруч немає.
    /// </para>
    /// </summary>
    public byte[]? PhotoThumbnail { get; set; }

    /// <summary>Довільні нотатки.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Життєві факти: професія й місце проживання (GEDCOM <c>OCCU</c>/<c>RESI</c>).
    /// Порядок значущий — його задає файл-джерело; сортування за датою робить UI.
    /// <para>
    /// Список, а не пара полів, бо обидва теги в GEDCOM повторювані: людина міняє
    /// професію й переїжджає, і кожен період має власну дату. Порожній список у файл
    /// не пишеться (див. <c>DocumentMapper</c>).
    /// </para>
    /// </summary>
    public List<PersonFact> Facts { get; init; } = new();

    /// <summary>Час створення запису (аудит).</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Час останнього оновлення запису (аудит).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Обчислюване: особа жива, якщо не вказано дату смерті.</summary>
    public bool IsAlive => DeathDate is null;

    /// <summary>
    /// Копія особи з тим самим <see cref="Entity.Id"/> — для експортних копій документа,
    /// які не мають зачіпати відкритий у застосунку документ.
    /// </summary>
    public Person Copy() => new()
    {
        Id = Id,
        LastName = LastName,
        FirstName = FirstName,
        Gender = Gender,
        MiddleName = MiddleName,
        MaidenName = MaidenName,
        BirthDate = BirthDate,
        BirthPlace = BirthPlace,
        DeathDate = DeathDate,
        PhotoPath = PhotoPath,
        PhotoThumbnail = PhotoThumbnail,
        Notes = Notes,

        // Новий список, а не та сама посилання: інакше правка фактів у копії
        // зачіпала б відкритий документ. Самі факти — record, тож незмінні
        // й копіювати їх поелементно не треба.
        Facts = [.. Facts],

        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };

    /// <summary>Зручне повне ім'я «Прізвище Ім'я По батькові» (для UI).</summary>
    public string FullName =>
        string.Join(' ', new[] { LastName, FirstName, MiddleName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
}
