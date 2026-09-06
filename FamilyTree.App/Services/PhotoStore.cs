using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FamilyTree.Domain;

namespace FamilyTree.App.Services;

/// <inheritdoc />
public sealed class PhotoStore : IPhotoStore
{
    /// <summary>Підтека сховища всередині теки даних. Входить у збережений відносний шлях.</summary>
    public const string FolderName = "photos";

    /// <summary>
    /// Найбільша сторона мініатюри, що вбудовується у файл документа. 100 px вистачає
    /// для картки 92×112 і дає ~4–5 КБ на особу; кожен зайвий крок розміру множиться
    /// на кількість осіб і на 4/3 через base64.
    /// </summary>
    public const int ThumbnailMaxSide = 100;

    /// <summary>Якість JPEG для мініатюри: нижче — помітні артефакти на обличчях.</summary>
    private const int ThumbnailQuality = 78;

    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    private readonly string _dataRoot;

    /// <param name="dataRoot">
    /// Тека даних застосунку. За замовчуванням — <c>%AppData%\FamilyTree</c>, той самий
    /// корінь, від якого рахує шлях <c>PersonPhoto.Resolve</c> при показі картки.
    /// Параметр існує заради тестів і портативних збірок.
    /// </param>
    public PhotoStore(string? dataRoot = null) =>
        _dataRoot = dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FamilyTree");

    public IReadOnlyList<string> SupportedExtensions => Extensions;

    public string Import(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var extension = Path.GetExtension(sourcePath);
        if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Непідтримуване розширення файлу фото: '{extension}'.");
        }

        var folder = Path.Combine(_dataRoot, FolderName);
        Directory.CreateDirectory(folder);

        // Ім'я генеруємо самі: ім'я користувацького файлу може містити що завгодно
        // (кирилицю, пробіли, крапки, задовгий шлях), а воно потрапляє у файл документа.
        var name = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        File.Copy(sourcePath, Path.Combine(folder, name));

        return Path.Combine(FolderName, name);
    }

    public byte[]? CreateThumbnail(string? relativePath, int maxSide = ThumbnailMaxSide)
    {
        var full = Resolve(relativePath);
        if (full is null)
        {
            return null;
        }

        try
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.UriSource = new Uri(full);
            source.CacheOption = BitmapCacheOption.OnLoad;  // не тримати файл відкритим
            source.EndInit();
            source.Freeze();

            var longest = Math.Max(source.PixelWidth, source.PixelHeight);
            var scale = longest > maxSide ? maxSide / (double)longest : 1.0;

            BitmapSource bitmap = scale < 1.0
                ? new TransformedBitmap(source, new ScaleTransform(scale, scale))
                : source;

            var encoder = new JpegBitmapEncoder { QualityLevel = ThumbnailQuality };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or UriFormatException)
        {
            // Файл зник, це не зображення або кодек його не знає — просто без мініатюри.
            // Ламати через це експорт усього документа було б непропорційно.
            return null;
        }
    }

    public string? Resolve(string? relativePath)
    {
        if (!PhotoPathPolicy.IsSafeRelativePhotoPath(relativePath))
        {
            return null;
        }

        var full = Path.Combine(_dataRoot, relativePath!);
        return File.Exists(full) ? full : null;
    }
}
