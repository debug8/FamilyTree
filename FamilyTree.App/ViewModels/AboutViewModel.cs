namespace FamilyTree.App.ViewModels;

/// <summary>
/// Дані для вікна «Про програму». Значення читаються зі збірки один раз у
/// <see cref="AppInfo"/> (спільно зі штампом версії у файлі — B-65), тож завжди
/// відповідають реально зібраній версії; правити треба лише csproj
/// (Product / InformationalVersion / Company / Copyright).
/// </summary>
public sealed class AboutViewModel
{
    /// <summary>Назва продукту (бренд, мовно-нейтральна).</summary>
    public string ProductName => AppInfo.ProductName;

    /// <summary>Версія застосунку.</summary>
    public string Version => AppInfo.Version;

    /// <summary>Автор (csproj: Company). Порожньо — рядок у вікні ховається.</summary>
    public string Author => AppInfo.Author;

    /// <summary>Рядок копірайту (csproj: Copyright). Порожньо — рядок ховається.</summary>
    public string Copyright => AppInfo.Copyright;

    /// <summary>Назва ліцензії. Не читається зі збірки — фіксована для застосунку.</summary>
    public string License => "MIT";

    /// <summary>
    /// Сторінка проєкту. Як і <see cref="License"/>, зі збірки не читається: атрибута з
    /// URL там немає, а тягнути його через окремий <c>AssemblyMetadata</c> заради одного
    /// рядка не варто. Змінюється разом із репозиторієм — тут і в <c>README.md</c>.
    /// </summary>
    public string ProjectUrl => "https://github.com/debug8/FamilyTree";

    /// <summary>
    /// Останній випуск. Саме <c>/releases/latest</c>, а не конкретний тег: GitHub сам
    /// перекидає на найновіший, тож посилання не доведеться правити з кожним випуском.
    /// </summary>
    public string LatestReleaseUrl => ProjectUrl + "/releases/latest";
}
