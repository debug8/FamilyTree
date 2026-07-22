using FamilyTree.Storage;

namespace FamilyTree.App.Services;

/// <summary>
/// Поточний відкритий документ родини в пам'яті. ViewModel-и працюють через нього;
/// сховище викликається лише на New/Open/Save.
/// </summary>
public interface IDocumentSession
{
    /// <summary>Поточний документ.</summary>
    FamilyDocument Current { get; }

    /// <summary>Шлях до файлу поточного документа (null — ще не збережений).</summary>
    string? FilePath { get; set; }

    /// <summary>Спрацьовує, коли документ замінено (New/Open) — UI перечитує дані.</summary>
    event EventHandler? DocumentChanged;

    /// <summary>Спрацьовує при зміні вмісту (додавання/редагування/видалення) — UI оновлює список.</summary>
    event EventHandler? ContentChanged;

    /// <summary>Створює новий порожній документ.</summary>
    void NewDocument(string title);

    /// <summary>Замінює поточний документ завантаженим.</summary>
    void SetDocument(FamilyDocument document, string? filePath);

    /// <summary>Позначає незбережені зміни та сповіщає UI про зміну вмісту.</summary>
    void MarkContentChanged();
}
