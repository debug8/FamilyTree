using FamilyTree.Domain;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests.Domain;

/// <summary>
/// Прямі перевірки політики безпеки шляхів до фото (B-13). Тести написані під
/// поведінку Windows (цільова платформа): "\foo" і "/foo" там прив'язані до кореня.
/// </summary>
public sealed class PhotoPathPolicyTests
{
    [Theory]
    [InlineData("photo.png")]
    [InlineData("photos/ivanov.png")]
    [InlineData(@"photos\ivanov.png")]
    [InlineData("a/../b.png")]   // після нормалізації лишається всередині теки
    [InlineData("./photo.png")]
    public void Safe_relative_paths_are_accepted(string path) =>
        PhotoPathPolicy.IsSafeRelativePhotoPath(path).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\server\share\a.png")] // UNC (бекслеш)
    [InlineData("//server/share/a.png")]  // UNC (форвард-слеш)
    [InlineData("http://tracker/1.png")]  // URL
    [InlineData("file:///etc/passwd")]    // URL
    [InlineData(@"C:\Windows\x.png")]     // абсолютний з диском
    [InlineData("C:/Windows/x.png")]      // диск + двокрапка
    [InlineData("/etc/passwd")]           // прив'язаний до кореня
    [InlineData(@"\photo.png")]           // прив'язаний до кореня (бекслеш)
    [InlineData(@"..\..\secret.png")]     // обхід угору (бекслеш)
    [InlineData("../../secret.png")]      // обхід угору (форвард-слеш)
    [InlineData("a/../../b.png")]         // виходить за корінь після нормалізації
    [InlineData("photo.png:stream")]      // NTFS-потік (двокрапка)
    public void Unsafe_or_empty_paths_are_rejected(string? path) =>
        PhotoPathPolicy.IsSafeRelativePhotoPath(path).ShouldBeFalse();
}
