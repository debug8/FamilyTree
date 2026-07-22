using System.Windows;
using FamilyTree.App.Localization;
using FamilyTree.App.Settings;
using FamilyTree.App.Theming;
using FamilyTree.App.ViewModels;
using FamilyTree.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FamilyTree.App;

/// <summary>
/// Точка входу застосунку. Композиційний корінь: будує Generic Host,
/// реєструє сервіси та ViewModel-и, виставляє мову й показує головне вікно.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) => ConfigureServices(services))
            .Build();
    }

    /// <summary>Глобальний доступ до контейнера для XAML-DataContext за потреби.</summary>
    public static IServiceProvider Services =>
        ((App)Current)._host.Services;

    private static void ConfigureServices(IServiceCollection services)
    {
        // Сервіси застосунку
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // Сховище документа
        services.AddSingleton<IFamilyStorage, JsonFamilyStorage>();

        // ViewModel-и
        services.AddSingleton<MainViewModel>();

        // Вікна
        services.AddSingleton<MainWindow>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        // 1. Завантажити налаштування та виставити збережену мову.
        //    Невідомий/битий код обробляється всередині SetLanguage (тихий відкат на uk).
        var settings = _host.Services.GetRequiredService<ISettingsService>();
        settings.Load();

        var localization = _host.Services.GetRequiredService<ILocalizationService>();
        localization.SetLanguage(settings.Current.Language);

        // 1b. Застосувати збережену тему (невідомий код — тихий відкат на світлу).
        var theme = _host.Services.GetRequiredService<IThemeService>();
        theme.SetTheme(settings.Current.Theme);

        // 2. Ініціалізувати XAML-проксі локалізації ДО створення вікна
        //    (markup extension {loc:Localize} звертається до LocalizationSource.Instance).
        LocalizationSource.Initialize(localization);

        // 3. Показати головне вікно.
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
