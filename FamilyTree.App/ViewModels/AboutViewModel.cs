using System.Reflection;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Дані для вікна «Про програму»: назва продукту й версія читаються з атрибутів збірки,
/// тож завжди відповідають реальній зібраній версії (розд. csproj: Product/InformationalVersion).
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
    }

    /// <summary>Назва продукту (бренд, мовно-нейтральна).</summary>
    public string ProductName { get; }

    /// <summary>Версія застосунку.</summary>
    public string Version { get; }
}
