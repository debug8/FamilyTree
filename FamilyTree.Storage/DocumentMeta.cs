namespace FamilyTree.Storage;

/// <summary>
/// Метадані документа родини (заголовок, аудит, версія застосунку).
/// </summary>
public sealed class DocumentMeta
{
    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Версія застосунку, що останньою зберегла файл.</summary>
    public string AppVersion { get; set; } = "1.0.0";
}
