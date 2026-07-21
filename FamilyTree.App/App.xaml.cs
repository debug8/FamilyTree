using System.Windows;
using FamilyTree.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FamilyTree.App;

/// <summary>
/// Точка входу застосунку. Композиційний корінь: будує Generic Host,
/// реєструє сервіси та ViewModel-и, показує головне вікно.
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
        // ViewModel-и
        services.AddSingleton<MainViewModel>();

        // Вікна
        services.AddSingleton<MainWindow>();

        // TODO (T-0.2): ILocalizationService
        // TODO (Етап 1): IFamilyStorage, доменні сервіси
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

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
