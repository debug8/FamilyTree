using System.IO;
using FamilyTree.Domain;

namespace FamilyTree.App.Services;

/// <inheritdoc />
public sealed class PhotoStore : IPhotoStore
{
    /// <summary>Підтека сховища всередині теки даних. Входить у збережений відносний шлях.</summary>
    public const string FolderName = "photos";

    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    private readonly string _dataRoot;

    /// <param name="dataRoot">
    /// Тека даних застосунку. За замовчуванням — <c>%AppData%\FamilyTree</c>, той самий
    /// корінь, від якого рахує шлях <c>PersonCard.ResolvePhoto</c> при показі картки.
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
