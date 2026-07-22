using FamilyTree.Storage;

namespace FamilyTree.App.Services;

/// <inheritdoc />
public sealed class DocumentSession : IDocumentSession
{
    public DocumentSession()
    {
        Current = FamilyDocument.CreateNew(string.Empty);
    }

    public FamilyDocument Current { get; private set; }

    public string? FilePath { get; set; }

    public event EventHandler? DocumentChanged;

    public event EventHandler? ContentChanged;

    public void NewDocument(string title)
    {
        Current = FamilyDocument.CreateNew(title);
        FilePath = null;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetDocument(FamilyDocument document, string? filePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        Current = document;
        FilePath = filePath;
        Current.IsDirty = false;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkContentChanged()
    {
        Current.IsDirty = true;
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }
}
