namespace FamilyTree.Storage;

/// <summary>
/// Метадані документа родини (заголовок, аудит, версія застосунку).
/// </summary>
public sealed class DocumentMeta
{
    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Версія застосунку, що останньою зберегла файл. Порожньо, доки файл не збережено:
    /// значення проставляє лише той, хто пише (див. <see cref="JsonFamilyStorage.SaveAsync"/>).
    /// Раніше тут стояв літерал «1.0.0», який лише круговертівся з файлу назад у файл
    /// і брехав про автора (B-65).
    /// </summary>
    public string AppVersion { get; set; } = string.Empty;
}
