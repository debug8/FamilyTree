namespace FamilyTree.Domain;

/// <summary>
/// Подія життя особи: коли, де і що про неї відомо. Народження й смерть —
/// <see cref="Person.Birth"/> та <see cref="Person.Death"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Навіщо окремий тип.</b> Кожна подія описується трьома полями (дата, місце, нотатка),
/// і поки вони лежали плоско в <see cref="Person"/>, кожна НОВА подія множилася на всі шари:
/// DTO, мапер в обидва боки, <c>FamilyMerger</c> (заповнення + конфлікт), імпорт і експорт
/// GEDCOM, редактор, resx ×2, тести. У 0.9.7 чотири такі поля коштували 24 зміни у файлах —
/// майже все механічне. З цим типом нова подія коштує один запис у <see cref="Person"/>,
/// один рядок у мапінгу й один блок у редакторі.
/// </para>
/// <para>
/// Це <b>record</b> без власного <c>Id</c> — за зразком <see cref="PersonFact"/>: у події
/// немає життя поза особою. Наслідки ті самі: рівність за значенням, зміна через <c>with</c>
/// або через фабрики нижче, а не присвоєнням властивості.
/// </para>
/// <para>
/// <b>Інваріант: порожньої події не існує.</b> Замість неї — <see langword="null"/>. Саме це
/// гарантують <see cref="Create"/> й усі <c>With*</c>, і на це спирається файл (порожній
/// об'єкт у нього не пишеться) та GEDCOM (<c>1 DEAT Y</c> пишеться лише коли підтегів немає).
/// Тому перевіряти «чи відомо щось про смерть» можна просто через <c>Death is not null</c>.
/// </para>
/// <para>
/// <b>Чого тут НЕМАЄ.</b> Прапорця «подія була, але про неї нічого не відомо» —
/// це <see cref="Person.Deceased"/>, і він лишається на особі. Причина не стилістична:
/// <c>bool</c> за замовчуванням <c>false</c>, тож відсутнє поле у старому файлі читається
/// як «живий»; загорнутий у подію, він мусив би зникати разом із нею.
/// </para>
/// </remarks>
public sealed record PersonEvent
{
    /// <summary>Коли — може бути неточною чи періодом (<see cref="FamilyDate"/>, T-5.2a).</summary>
    public FamilyDate? Date { get; init; }

    /// <summary>Де — вільний рядок (GEDCOM <c>PLAC</c>).</summary>
    public string? Place { get; init; }

    /// <summary>
    /// Нотатка про подію — GEDCOM <c>NOTE</c> ПІД подією, не <c>INDI.NOTE</c>. Саме сюди
    /// лягають обставини й причина смерті: реальні файли пишуть їх прозою тут, а не в
    /// передбачений стандартом <c>CAUS</c>.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// Подія без жодних даних. Такої в моделі не має існувати (див. інваріант у примітках
    /// до типу) — властивість потрібна самим фабрикам і перевіркам цілісності.
    /// </summary>
    public bool IsEmpty =>
        Date is null
        && string.IsNullOrWhiteSpace(Place)
        && string.IsNullOrWhiteSpace(Note);

    /// <summary>
    /// Створює подію або повертає <see langword="null"/>, якщо відомо порожньо.
    /// Рядки обрізаються, порожні зводяться до <see langword="null"/> — щоб пробіл,
    /// набраний у діалозі чи принесений із чужого файлу, не рахувався за дані.
    /// </summary>
    public static PersonEvent? Create(FamilyDate? date, string? place = null, string? note = null)
    {
        var candidate = new PersonEvent
        {
            Date = date,
            Place = Trim(place),
            Note = Trim(note),
        };

        return candidate.IsEmpty ? null : candidate;
    }

    /// <summary>Та сама подія з іншою датою (<see langword="null"/>, якщо решта теж порожня).</summary>
    public static PersonEvent? WithDate(PersonEvent? current, FamilyDate? date) =>
        Create(date, current?.Place, current?.Note);

    /// <summary>Та сама подія з іншим місцем.</summary>
    public static PersonEvent? WithPlace(PersonEvent? current, string? place) =>
        Create(current?.Date, place, current?.Note);

    /// <summary>Та сама подія з іншою нотаткою.</summary>
    public static PersonEvent? WithNote(PersonEvent? current, string? note) =>
        Create(current?.Date, current?.Place, note);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
