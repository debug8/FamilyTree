namespace FamilyTree.Domain;

/// <summary>
/// Вид життєвого факту особи. Відповідає атрибутам GEDCOM 5.5.1:
/// <c>OCCU</c> (професія) і <c>RESI</c> (місце проживання).
/// </summary>
/// <remarks>
/// <see cref="Other"/> — не «різне», а запобіжник сумісності: файл, записаний
/// новішою збіркою з видом, якого ця ще не знає, має читатися без втрат і без
/// падіння. Оригінальна назва виду при цьому лежить у <see cref="PersonFact.Label"/>
/// і повертається у файл незміненою. Саме тому вид у JSON — рядок, а не enum
/// через <c>JsonStringEnumConverter</c>: конвертер кинув би <c>JsonException</c>,
/// і користувач побачив би «файл пошкоджено».
/// </remarks>
public enum PersonFactKind
{
    /// <summary>Професія, рід занять — GEDCOM <c>OCCU</c>.</summary>
    Occupation,

    /// <summary>Місце проживання — GEDCOM <c>RESI</c>.</summary>
    Residence,

    /// <summary>Вид, невідомий цій збірці; оригінальна назва — у <see cref="PersonFact.Label"/>.</summary>
    Other,
}

/// <summary>
/// Життєвий факт особи: що, коли й де. На відміну від дати народження чи смерті,
/// факт повторюваний — людина міняє професію й місце проживання, і кожен період
/// має власний запис.
/// </summary>
/// <remarks>
/// <para>
/// Це <b>record</b>, а не клас-сутність: у факта немає власного <c>Id</c> і власного
/// життя поза особою. Наслідки, про які варто пам'ятати: рівність — за значенням
/// (два однакові факти в списку не розрізняються, і <c>List.Remove</c> прибере
/// перший), а зміна робиться через <c>with</c>, а не присвоєнням властивості.
/// Відсутність Id — свідома економія: 36 символів Guid на факт у файлі, який
/// позиціонується як людиночитний.
/// </para>
/// <para>
/// Розподіл полів: <see cref="Value"/> — суть факту (для професії — сама професія;
/// для проживання зазвичай порожнє, бо все значення несе <see cref="Place"/>),
/// <see cref="Place"/> — місце, <see cref="Date"/> — коли (той самий
/// <see cref="FamilyDate"/>, що й у дат народження та смерті, тож підтримуються
/// неточні дати й періоди).
/// </para>
/// </remarks>
public sealed record PersonFact
{
    /// <summary>Вид факту.</summary>
    public required PersonFactKind Kind { get; init; }

    /// <summary>
    /// Оригінальна назва виду для <see cref="PersonFactKind.Other"/>; для відомих
    /// видів — <see langword="null"/>. Зберігається, щоб запис назад у файл був
    /// точним (див. коментар до <see cref="PersonFactKind.Other"/>).
    /// </summary>
    public string? Label { get; init; }

    /// <summary>Суть факту: професія. Для проживання зазвичай порожнє.</summary>
    public string? Value { get; init; }

    /// <summary>Коли — може бути неточною чи періодом.</summary>
    public FamilyDate? Date { get; init; }

    /// <summary>Де.</summary>
    public string? Place { get; init; }

    /// <summary>
    /// Факт без жодних даних. Такий запис не несе інформації, тому редактор його
    /// не додає, а <c>DocumentIntegrity</c> прибирає з чужого файлу.
    /// </summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Value)
        && string.IsNullOrWhiteSpace(Place)
        && Date is null;
}
