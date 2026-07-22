namespace FamilyTree.Storage;

/// <summary>
/// Абстракція сховища документа родини. Дозволяє за потреби замінити реалізацію
/// (напр. на SQLite) без змін у домені та UI.
/// </summary>
public interface IFamilyStorage
{
    /// <summary>Завантажує документ із файлу за шляхом.</summary>
    Task<FamilyDocument> LoadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Атомарно зберігає документ у файл за шляхом (з ротацією бекапів).</summary>
    Task SaveAsync(FamilyDocument document, string path, CancellationToken cancellationToken = default);
}
