using System.Text.Json.Nodes;

namespace FamilyTree.Storage.Serialization;

/// <summary>
/// Міграція формату файлу v2 → v3: шість плоских полів особи стають двома вкладеними
/// подіями. <c>birthDate</c>/<c>birthPlace</c>/<c>birthNote</c> → <c>birth</c>,
/// <c>deathDate</c>/<c>deathPlace</c>/<c>deathNote</c> → <c>death</c>. Працює на рівні
/// JSON-вузла, тож не залежить від форми DTO — як і <see cref="FormatMigrationV1ToV2"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Чому міграція взагалі є.</b> `IDEAS.md` припускав, що реальних `.familytree` поза
/// <c>samples/</c> немає, а <c>samples/</c> перегенерує <c>tools/SeedGenerator</c>. Це
/// виявилося правдою лише для файлів <c>rodyna-*</c>: поруч лежать рукописні, яких ніщо не
/// відтворить, а застосунок уже виходив інсталятором — тобто файли можуть бути й у чужих
/// руках. Сорок рядків тут дешевші за будь-яку з цих утрат.
/// </para>
/// <para>
/// <b>Без утрат.</b> Порожні поля просто не переносяться; якщо порожні всі три — об'єкт
/// події не створюється взагалі (інваріант <c>PersonEvent</c>: порожньої події не буває).
/// <c>deceased</c> лишається полем ОСОБИ, а не події — див. коментар до
/// <c>Person.Deceased</c>. Незнайомі поля особи не чіпаються: вони доїдуть через
/// <c>[JsonExtensionData]</c>, і серед них можуть бути <c>birth*</c>-подібні назви з
/// новіших збірок, які цій міграції не належать.
/// </para>
/// <para>
/// <b>Ідемпотентність.</b> Якщо об'єкт <c>birth</c> у файлі вже є (гібрид після ручної
/// правки), міграція його не чіпає й плоскі поля не переносить — краще лишити видимий
/// дубль, ніж мовчки перезаписати те, що людина вже зробила руками.
/// </para>
/// </remarks>
internal sealed class FormatMigrationV2ToV3 : IFormatMigration
{
    public int FromVersion => 2;

    public JsonObject Migrate(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document["persons"] is not JsonArray persons)
        {
            return document;
        }

        foreach (var item in persons)
        {
            if (item is not JsonObject person)
            {
                continue;
            }

            Fold(person, "birth", "birthDate", "birthPlace", "birthNote");
            Fold(person, "death", "deathDate", "deathPlace", "deathNote");
        }

        return document;
    }

    private static void Fold(JsonObject person, string target, string dateField, string placeField, string noteField)
    {
        // Плоскі поля прибираємо в будь-якому разі — навіть якщо вкладений об'єкт уже є
        // (інакше вони лишилися б у файлі назавжди як незнайомі й возилися транзитом).
        var date = Detach(person, dateField);
        var place = Detach(person, placeField);
        var note = Detach(person, noteField);

        if (person.ContainsKey(target))
        {
            return;
        }

        if (date is null && !HasContent(place) && !HasContent(note))
        {
            return;
        }

        var node = new JsonObject();
        if (date is not null)
        {
            node["date"] = date;
        }

        if (HasContent(place))
        {
            node["place"] = place;
        }

        if (HasContent(note))
        {
            node["note"] = note;
        }

        person[target] = node;
    }

    /// <summary>
    /// Чи несе вузол щось. Порожній або пробільний РЯДОК — ні (такий не мав би лежати й у
    /// v2-файлі). Значення іншого типу (число, об'єкт — ручна правка) вважається вмістом і
    /// переноситься як є: хай далі впаде явною помилкою формату, як падало й до v3, аніж
    /// зникне мовчки.
    /// </summary>
    private static bool HasContent(JsonNode? node) =>
        node is not null && !string.IsNullOrWhiteSpace(AsString(node) ?? "-");

    /// <summary>
    /// Знімає поле з об'єкта й повертає його вузол, ВІДЧЕПЛЕНИЙ від батька. Без
    /// <c>Remove</c> перед повторним присвоєнням <c>System.Text.Json</c> кидає
    /// «node already has a parent»: у JsonNode кожен вузол належить одному батьку.
    /// </summary>
    private static JsonNode? Detach(JsonObject obj, string field)
    {
        if (!obj.TryGetPropertyValue(field, out var value) || value is null)
        {
            obj.Remove(field);
            return null;
        }

        obj.Remove(field);
        return value;
    }

    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
