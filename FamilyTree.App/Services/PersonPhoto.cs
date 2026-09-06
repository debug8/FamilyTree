using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FamilyTree.Domain;

namespace FamilyTree.App.Services;

/// <summary>
/// Завантаження фото особи для показу: спершу оригінал із теки даних, а якщо його там
/// немає — вбудована мініатюра з документа. Саме завдяки цьому порядку файл, надісланий
/// родичу, показує людей з обличчями, а на власній машині видно повну якість.
/// </summary>
public static class PersonPhoto
{
    /// <summary>
    /// Повертає зображення, декодоване не ширшим за <paramref name="maxWidth"/>,
    /// або <see langword="null"/>, якщо фото немає чи його не вдалося прочитати.
    /// </summary>
    /// <param name="maxWidth">
    /// Стеля ширини декодування. Менше за оригінал — зменшуємо (картка 92×112 не потребує
    /// 8 мегапікселів у пам'яті); більше — НЕ збільшуємо, інакше маленьке фото розтягнулося б
    /// із втратою різкості й зайвою пам'яттю.
    /// </param>
    public static ImageSource? Load(Person person, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(person);

        if (Resolve(person.PhotoPath) is { } full && File.Exists(full))
        {
            if (FromFile(full, maxWidth) is { } fromFile)
            {
                return fromFile;
            }
        }

        return person.PhotoThumbnail is { Length: > 0 } bytes ? FromBytes(bytes, maxWidth) : null;
    }

    /// <summary>
    /// Абсолютний шлях до фото в теці даних або <see langword="null"/>, якщо шлях порожній
    /// чи небезпечний. Приймає ЛИШЕ безпечний відносний шлях усередині теки даних:
    /// абсолютні, UNC та URL відкидаються (захист у глибину до санітизації у сховищі, B-13),
    /// тож показ картки ніколи не звертається до стороннього ресурсу.
    /// <para>
    /// Свідомо статичний і без залежностей: цим шляхом ходить показ у картках і на вкладці
    /// «Особа», де DI немає. <c>PhotoStore.Resolve</c> робить те саме для сервісного коду,
    /// але вміє ще й підмінювати корінь теки даних (тести, портативна збірка).
    /// </para>
    /// </summary>
    public static string? Resolve(string? relativePath)
    {
        if (!PhotoPathPolicy.IsSafeRelativePhotoPath(relativePath))
        {
            return null;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FamilyTree",
            relativePath!);
    }

    private static ImageSource? FromFile(string path, int maxWidth) =>
        Decode(
            () => BitmapDecoder.Create(
                new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None),
            image => image.UriSource = new Uri(path),
            maxWidth);

    private static ImageSource? FromBytes(byte[] bytes, int maxWidth) =>
        Decode(
            () => BitmapDecoder.Create(
                new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None),
            image => image.StreamSource = new MemoryStream(bytes),
            maxWidth);

    /// <summary>
    /// Двокрокове читання: спершу лише метадані (розмір), потім саме декодування.
    /// Перший крок потрібен, щоб дізнатися ширину оригіналу, не втягуючи його в пам'ять:
    /// <c>DecodePixelWidth</c> більший за оригінал не зменшує, а РОЗТЯГУЄ зображення.
    /// </summary>
    private static ImageSource? Decode(
        Func<BitmapDecoder> openMetadata, Action<BitmapImage> setSource, int maxWidth)
    {
        try
        {
            var sourceWidth = openMetadata().Frames[0].PixelWidth;

            var image = new BitmapImage();
            image.BeginInit();
            setSource(image);
            image.CacheOption = BitmapCacheOption.OnLoad;   // не тримати файл/потік відкритим
            if (sourceWidth > maxWidth)
            {
                image.DecodePixelWidth = maxWidth;
            }

            image.EndInit();
            image.Freeze();                                 // можна показувати з будь-якого потоку
            return image;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or UriFormatException
            or OverflowException)
        {
            // Биті чи чужі байти зображення не повинні валити показ картки.
            return null;
        }
    }
}
