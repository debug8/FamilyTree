using System.Reflection;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Дані для вікна «Про програму». Усе читається з атрибутів збірки, тож завжди
/// відповідає реально зібраній версії, а правити треба лише csproj
/// (Product / InformationalVersion / Company / Copyright).
/// </summary>
public sealed class AboutViewModel
{
    public AboutViewModel()
    {
        var assembly = Assembly.GetExecutingAssembly();

        ProductName = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Family Tree";

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString() ?? string.Empty;

        // Відкидаємо метадані збірки (напр. "0.9.0+abcdef") — показуємо лише семантичну версію.
        var plus = version.IndexOf('+');
        Version = plus >= 0 ? version[..plus] : version;

        Author = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;
        Copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;
    }

    /// <summary>Назва продукту (бренд, мовно-нейтральна).</summary>
    public string ProductName { get; }

    /// <summary>Версія застосунку.</summary>
    public string Version { get; }

    /// <summary>Автор (csproj: Company). Порожньо — рядок у вікні ховається.</summary>
    public string Author { get; }

    /// <summary>Рядок копірайту (csproj: Copyright). Порожньо — рядок ховається.</summary>
    public string Copyright { get; }

    /// <summary>Назва ліцензії. Не читається зі збірки — фіксована для застосунку.</summary>
    public string License => "MIT";
}
