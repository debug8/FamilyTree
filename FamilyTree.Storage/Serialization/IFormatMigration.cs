using System.Text.Json.Nodes;

namespace FamilyTree.Storage.Serialization;

/// <summary>
/// Міграція формату файлу з версії <see cref="FromVersion"/> на наступну.
/// Працює на рівні JSON-вузла, тож не залежить від поточної форми DTO.
/// </summary>
public interface IFormatMigration
{
    /// <summary>Версія схеми, з якої ця міграція піднімає документ (на +1).</summary>
    int FromVersion { get; }

    /// <summary>Перетворює JSON документа на наступну версію схеми.</summary>
    JsonObject Migrate(JsonObject document);
}
