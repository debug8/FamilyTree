using System.Globalization;
using System.Text.Json.Nodes;

namespace FamilyTree.Storage.Serialization;

/// <summary>
/// Міграція формату файлу v1 → v2 (T-5.2a): дати з ISO-рядка (<c>"1980-03-14"</c>) стають
/// об'єктом <c>FamilyDate</c> виду <c>{"kind":"exact","y":1980,"m":3,"d":14}</c>. Працює на
/// рівні JSON-вузла, тож не залежить від форми DTO. Без утрат: непарсовний рядок (теоретично
/// у ручному файлі) зберігається як <c>{"kind":"phrase","text":…}</c>, порожні/відсутні поля —
/// без змін.
/// </summary>
internal sealed class FormatMigrationV1ToV2 : IFormatMigration
{
    public int FromVersion => 1;

    public JsonObject Migrate(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        MigrateItems(document["persons"], "birthDate", "deathDate");
        MigrateItems(document["spouseLinks"], "marriageDate", "divorceDate");

        return document;
    }

    private static void MigrateItems(JsonNode? node, params string[] dateFields)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        foreach (var item in array)
        {
            if (item is not JsonObject obj)
            {
                continue;
            }

            foreach (var field in dateFields)
            {
                ConvertField(obj, field);
            }
        }
    }

    private static void ConvertField(JsonObject obj, string field)
    {
        if (!obj.TryGetPropertyValue(field, out var value) || value is null)
        {
            return;
        }

        // Лише рядок (форма v1). Якщо це вже об'єкт — нічого не робимо.
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var iso))
        {
            return;
        }

        obj[field] = DateOnly.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? Exact(date)
            : Phrase(iso);
    }

    private static JsonObject Exact(DateOnly date) => new()
    {
        ["kind"] = "exact",
        ["y"] = date.Year,
        ["m"] = date.Month,
        ["d"] = date.Day,
    };

    private static JsonObject Phrase(string text) => new()
    {
        ["kind"] = "phrase",
        ["text"] = text,
    };
}
