using System.IO;

namespace FamilyTree.Domain;

/// <summary>
/// Політика безпеки для <see cref="Person.PhotoPath"/> (B-13). Домен документує
/// <c>PhotoPath</c> як <b>відносний</b> шлях усередині теки даних застосунку; усе інше —
/// потенційна атака з чужого <c>.familytree</c>-файлу, який потрапляє прямо в
/// <c>Image.Source</c> при показі картки особи:
/// <list type="bullet">
/// <item>UNC (<c>\\server\share\a.png</c>) → Windows іде на SMB-сервер атакувальника й
/// віддає NTLM-хендшейк (витік облікових даних лише від наведення на картку);</item>
/// <item>URL (<c>http://tracker/1.png</c>, <c>file://…</c>) → вихідний запит-трекер;</item>
/// <item>абсолютний або <c>..\..\</c>-шлях → читання/розкриття файлів поза текою даних.</item>
/// </list>
/// Перевірка суто рядкова (без звертань до файлової системи), тож дає той самий результат
/// і в шарі сховища (санітизація при завантаженні), і в шарі UI (резолвинг перед показом).
/// </summary>
public static class PhotoPathPolicy
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>
    /// <see langword="true"/>, лише якщо <paramref name="path"/> — непорожній безпечний
    /// <b>відносний</b> шлях, що не виходить за межі теки даних. Порожнє значення,
    /// абсолютні/UNC-шляхи, URL і обхід каталогів через <c>..</c> дають
    /// <see langword="false"/>.
    /// </summary>
    public static bool IsSafeRelativePhotoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // Двокрапка = схема URL (http:, file:), літера диска (C:) або NTFS-потік (:stream).
        // Жодне з цього не є відносним шляхом усередині теки даних.
        if (path.Contains(':'))
        {
            return false;
        }

        // UNC-шлях (\\server\share або //server/share) — зовнішній мережевий ресурс.
        if (path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        // Абсолютний або прив'язаний до кореня (у т.ч. "\photo.png", "/photo.png").
        if (Path.IsPathRooted(path))
        {
            return false;
        }

        // Обхід угору за межі теки: рахуємо глибину сегментів. Щойно ".." опускає
        // нижче кореня — шлях виходить назовні (напр. "..\..\Users\Public\secret.png").
        var depth = 0;
        foreach (var segment in path.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (--depth < 0)
                {
                    return false;
                }
            }
            else
            {
                depth++;
            }
        }

        return true;
    }
}
