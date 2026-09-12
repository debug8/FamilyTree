using System.Text.Json;

namespace FamilyTree.Storage;

/// <summary>
/// Поля файлу, яких ця збірка не знає, збережені «як є» на час життя документа в пам'яті
/// й повернені у файл незміненими при наступному записі.
/// </summary>
/// <remarks>
/// <para>
/// <b>Навіщо.</b> Формат росте адитивно: кожне нове поле опційне, схема лишається v2,
/// міграції не треба. Напрямок «нова збірка читає старий файл» від цього працює бездоганно,
/// а зворотний — ні: стара збірка, відкривши новіший файл, мовчки викидала незнайомі поля,
/// і наступне збереження стирало їх з диска. Поки застосунком користується одна людина з
/// однією збіркою, це теорія; щойно <c>.familytree</c> полетить родичу зі старішою версією —
/// це втрата даних без жодного повідомлення.
/// </para>
/// <para>
/// Це та сама вимога, яку в проєкті вже виконано на рівень нижче: невідомий ВИД факту
/// читається як <c>PersonFactKind.Other</c> зі збереженням оригіналу й повертається у файл
/// незміненим. Тут те саме, але для невідомого ПОЛЯ.
/// </para>
/// <para>
/// <b>Чому це краще за бамп версії схеми.</b> Гейт <c>version &gt; CurrentSchemaVersion</c>
/// лише ЗАБОРОНЯЄ відкрити файл; розширення його ЗБЕРІГАЄ. Для адитивних змін друге строго
/// корисніше. Бамп лишається для того, чим він і є насправді, — зміни змісту або структури
/// наявного поля (як v1→v2 для дат).
/// </para>
/// <para>
/// <b>Чому тут, а не в доменних сутностях.</b> <c>FamilyTree.Domain</c> не має жодної
/// залежності, і <c>Dictionary&lt;string, JsonElement&gt;</c> у <c>Person</c> був би
/// подробицею формату зберігання всередині домену. Натомість транзит живе там, де й решта
/// незбережуваного стану завантаження (пор. <see cref="FamilyDocument.RepairedIssues"/>),
/// а зіставлення з сутностями йде за <c>Id</c>.
/// </para>
/// <para>
/// <b>Ціна рішення.</b> Незнайомі поля перестають бути видимими помилками: описка
/// <c>"birthPlce"</c> у руками правленому файлі возитиметься вічно замість того, щоб тихо
/// зникнути. <c>DocumentIntegrity</c> цей вміст свідомо не перевіряє й не чистить — це
/// навмисно непрозорий транзит.
/// </para>
/// <para>
/// <b>Сироти не накопичуються у файлі.</b> Видалення особи не прибирає її запис зі словника,
/// але запис у файл іде лише за <c>Id</c> сутностей, які в документі справді є, — тож на диск
/// сирота не потрапляє. У пам'яті вона живе до закриття документа; це дешевше, ніж чіпляти
/// прибирання до кожного видалення.
/// </para>
/// </remarks>
internal sealed class DocumentExtras
{
    /// <summary>Незнайомі секції кореня файлу (сусіди <c>persons</c>, <c>meta</c> тощо).</summary>
    public Dictionary<string, JsonElement>? Root { get; set; }

    /// <summary>Незнайомі поля секції <c>meta</c>.</summary>
    public Dictionary<string, JsonElement>? Meta { get; set; }

    /// <summary>Незнайомі поля осіб за <c>Person.Id</c>.</summary>
    public Dictionary<Guid, Dictionary<string, JsonElement>> Persons { get; } = new();

    /// <summary>Незнайомі поля зв'язків «батько–дитина» за <c>ParentChildLink.Id</c>.</summary>
    public Dictionary<Guid, Dictionary<string, JsonElement>> ParentChildLinks { get; } = new();

    /// <summary>Незнайомі поля подружніх зв'язків за <c>SpouseLink.Id</c>.</summary>
    public Dictionary<Guid, Dictionary<string, JsonElement>> SpouseLinks { get; } = new();

    /// <summary>
    /// Чи не було у файлі жодного незнайомого поля. Для файлу, записаного цією ж збіркою,
    /// мусить бути <c>true</c>: інакше якесь поле DTO перестало збігатися з ключем у файлі.
    /// </summary>
    public bool IsEmpty =>
        Root is null or { Count: 0 } &&
        Meta is null or { Count: 0 } &&
        Persons.Count == 0 &&
        ParentChildLinks.Count == 0 &&
        SpouseLinks.Count == 0;

    /// <summary>
    /// Запам'ятовує незнайомі поля сутності. Ключ — <c>Id</c> ГОТОВОЇ доменної сутності, а не
    /// той, що у файлі: зв'язок без <c>"id"</c> отримує новий <c>Guid</c> у мапері, і за
    /// <c>Guid.Empty</c> його потім ніхто б не знайшов.
    /// </summary>
    public static void Remember(
        Dictionary<Guid, Dictionary<string, JsonElement>> bucket,
        Guid id,
        Dictionary<string, JsonElement>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return;
        }

        // TryAdd, а не індексатор: при збігу Id (биті чи рукописні файли) лишається запис
        // ПЕРШОЇ сутності — тієї, яку в такому файлі залишає й DocumentIntegrity.
        bucket.TryAdd(id, fields);
    }

    /// <summary>Повертає незнайомі поля сутності або <c>null</c>, якщо їх не було.</summary>
    public static Dictionary<string, JsonElement>? Lookup(
        Dictionary<Guid, Dictionary<string, JsonElement>> bucket,
        Guid id) =>
        bucket.TryGetValue(id, out var fields) ? fields : null;
}
