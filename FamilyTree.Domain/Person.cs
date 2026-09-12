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

    /// <summary>
    /// Нотатка при народженні — GEDCOM <c>BIRT.NOTE</c>. Окремо від <see cref="Notes"/>
    /// (<c>INDI.NOTE</c>): у файлі це різні теги, і злиття їх в одне поле означало б, що
    /// при зворотному експорті текст переїде в чужий тег.
    /// </summary>
    public string? BirthNote { get; set; }

    /// <summary>Дата смерті (null — дата невідома або особа жива; див. <see cref="Deceased"/>).</summary>
    public FamilyDate? DeathDate { get; set; }

    /// <summary>Місце смерті — GEDCOM <c>DEAT.PLAC</c>. Такий самий вільний рядок, як <see cref="BirthPlace"/>.</summary>
    public string? DeathPlace { get; set; }

    /// <summary>
    /// Нотатка при смерті — GEDCOM <c>DEAT.NOTE</c>. Саме сюди лягають обставини й причина
    /// смерті: реальні файли пишуть їх прозою в <c>NOTE</c>, а не в передбачений стандартом
    /// <c>CAUS</c> (окремого поля під причину тому й немає — див. CHANGELOG).
    /// </summary>
    public string? DeathNote { get; set; }

    /// <summary>
    /// Явна позначка, що особа померла, навіть коли дата смерті невідома
    /// (у діалозі знято галочку «Живий», але дату не вказано; у GEDCOM — <c>1 DEAT</c>
    /// без <c>DATE</c>). Без цього прапорця «життя» трималося лише на
    /// <see cref="DeathDate"/>, тож стан «помер, дата невідома» неможливо було
    /// зберегти — особа мовчки лишалася живою.
    /// <para>
    /// Прапорець позитивний («помер»), а не «живий», навмисне: <c>bool</c> за
    /// замовчуванням <c>false</c>, тож відсутнє поле у старих файлах читається як
    /// «живий» — правильна поведінка без міграції. Те саме рішення, що й у
    /// <see cref="SpouseLink.Divorced"/>.
    /// </para>
    /// </summary>
    public bool Deceased { get; set; }

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

    /// <summary>
    /// Обчислюване: особа жива, якщо немає дати смерті І її не позначено померлою.
    /// Перевірка <c>DeathDate is null</c> лишена першою для зворотної сумісності зі
    /// старими файлами, де смерть виражалася лише датою (поля <see cref="Deceased"/>
    /// там немає → false).
    /// </summary>
    public bool IsAlive => DeathDate is null && !Deceased;

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
        BirthNote = BirthNote,
        DeathDate = DeathDate,
        DeathPlace = DeathPlace,
        DeathNote = DeathNote,
        Deceased = Deceased,
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
