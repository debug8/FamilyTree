namespace FamilyTree.App.Services;

/// <summary>
/// Сховище фотографій осіб: копіює вибраний файл у теку даних застосунку й повертає
/// <b>відносний</b> шлях, який зберігається в <c>Person.PhotoPath</c>.
/// <para>
/// Чому копіюємо, а не запам'ятовуємо шлях користувача: домен документує
/// <c>PhotoPath</c> як відносний шлях усередині теки даних, і <c>PhotoPathPolicy</c>
/// (B-13) відкидає абсолютні, UNC- та URL-шляхи — інакше чужий <c>.familytree</c>
/// міг би змусити застосунок піти на SMB-сервер атакувальника при показі картки.
/// </para>
/// </summary>
public interface IPhotoStore
{
    /// <summary>
    /// Копіює файл у сховище й повертає відносний шлях (напр. <c>photos\a1b2….jpg</c>).
    /// Кидає виняток, якщо формат не підтримується або скопіювати не вдалося.
    /// </summary>
    string Import(string sourcePath);

    /// <summary>
    /// Абсолютний шлях до наявного файлу фото або <see langword="null"/>, якщо шлях
    /// небезпечний (див. <c>PhotoPathPolicy</c>), порожній чи файлу немає на диску.
    /// </summary>
    string? Resolve(string? relativePath);

    /// <summary>Розширення, які приймає <see cref="Import"/> (для фільтра діалогу).</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Зменшена JPEG-копія фото для вбудовування у файл документа, або
    /// <see langword="null"/>, якщо фото немає чи файл не вдалося прочитати як зображення.
    /// </summary>
    /// <param name="maxSide">Найбільша сторона мініатюри в пікселях.</param>
    byte[]? CreateThumbnail(string? relativePath, int maxSide = PhotoStore.ThumbnailMaxSide);
}
