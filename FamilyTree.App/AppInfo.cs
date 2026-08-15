using System.Reflection;

namespace FamilyTree.App;

/// <summary>
/// Єдине джерело даних про застосунок, прочитаних зі збірки (версія, продукт, автор,
/// копірайт). Усе береться з атрибутів збірки, тож завжди відповідає реально зібраній
/// версії, а правити треба лише csproj (Version / InformationalVersion / Product / Company / Copyright).
/// <para>
/// Раніше цю логіку мав лише <see cref="ViewModels.AboutViewModel"/>. Тепер версію бере
/// й сховище, щоб проставляти її в metadata файлу (B-65), тож читання винесено сюди,
/// в одне місце, щоб «Про програму» та штамп у файлі ніколи не розходилися.
/// </para>
/// </summary>
public static class AppInfo
{
    static AppInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();

        ProductName = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Family Tree";

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString() ?? string.Empty;

        // Відкидаємо метадані збірки (напр. "0.9.2+abcdef") — лишаємо семантичну версію.
        var plus = version.IndexOf('+');
        Version = plus >= 0 ? version[..plus] : version;

        Author = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;
        Copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;
    }

    /// <summary>Назва продукту (бренд, мовно-нейтральна).</summary>
    public static string ProductName { get; }

    /// <summary>Семантична версія застосунку (без метаданих збірки після «+»).</summary>
    public static string Version { get; }

    /// <summary>Автор (csproj: Company).</summary>
    public static string Author { get; }

    /// <summary>Рядок копірайту (csproj: Copyright).</summary>
    public static string Copyright { get; }
}
