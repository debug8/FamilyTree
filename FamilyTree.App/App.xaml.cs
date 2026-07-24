using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FamilyTree.App.Diagnostics;
using FamilyTree.App.Localization;
using FamilyTree.App.Services;
using FamilyTree.App.Settings;
using FamilyTree.App.Theming;
using FamilyTree.App.ViewModels;
using FamilyTree.Domain.Kinship;
using FamilyTree.Domain.Layout;
using FamilyTree.Domain.Validation;
using FamilyTree.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FamilyTree.App;

/// <summary>
/// Точка входу застосунку. Композиційний корінь: будує Generic Host,
/// реєструє сервіси та ViewModel-и, виставляє мову й показує головне вікно.
/// Також налаштовує логування (Serilog) та глобальні обробники необроблених помилок.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        // Логування налаштовуємо якнайраніше — щоб зафіксувати навіть помилки старту.
        AppLog.Initialize();
        RegisterGlobalExceptionHandlers();

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) => ConfigureServices(services))
                .Build();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Не вдалося побудувати хост застосунку");
            throw;
        }
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
        services.AddSingleton<FamilyMerger>();

        // Ядро родства
        services.AddSingleton<CommonAncestorFinder>();
        services.AddSingleton<IKinshipFormatter, Kinship.CultureKinshipFormatter>();
        services.AddSingleton<KinshipCalculator>();
        services.AddSingleton<KinshipPathExplainer>();

        // Валідація зв'язків
        services.AddSingleton<RelationshipValidator>();

        // Сесія документа та діалоги
        services.AddSingleton<IDocumentSession, DocumentSession>();
        services.AddSingleton<IDialogService, DialogService>();

        // Візуалізація дерева
        services.AddSingleton<TreeLayoutEngine>();
        services.AddSingleton<TreeViewModel>();
        services.AddSingleton<WhoIsWhoViewModel>();

        // ViewModel-и
        services.AddSingleton<MainViewModel>();

        // Вікна
        services.AddSingleton<MainWindow>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        Log.Information("Застосунок запускається");
        await _host.StartAsync();

        // 1. Завантажити налаштування та виставити збережену мову.
        //    Невідомий/битий код обробляється всередині SetLanguage (тихий відкат на uk).
        var settings = _host.Services.GetRequiredService<ISettingsService>();
        settings.Load();

        var localization = _host.Services.GetRequiredService<ILocalizationService>();
        localization.SetLanguage(settings.Current.Language);

        // 2. Ініціалізувати XAML-проксі локалізації ДО створення будь-яких сервісів/вікон
        //    (markup extension {loc:Localize} і LocalizedOption звертаються до LocalizationSource.Instance).
        LocalizationSource.Initialize(localization);

        // 3. Застосувати збережену тему (невідомий код — тихий відкат на світлу).
        var theme = _host.Services.GetRequiredService<IThemeService>();
        theme.SetTheme(settings.Current.Theme);

        // 4. Застосувати збережений стиль назв родства.
        var formatter = _host.Services.GetRequiredService<IKinshipFormatter>();
        formatter.Style = settings.Current.KinshipNamingStyle == "detailed"
            ? KinshipNamingStyle.Detailed
            : KinshipNamingStyle.Standard;

        // 5. Показати головне вікно.
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("Застосунок завершує роботу (код виходу {ExitCode})", e.ApplicationExitCode);
        await _host.StopAsync();
        _host.Dispose();
        AppLog.Shutdown();
        base.OnExit(e);
    }

    // --- Глобальна обробка помилок --------------------------------------

    /// <summary>
    /// Підписується на три канали необроблених винятків: UI-потік (Dispatcher),
    /// домен застосунку (фатальні) та фонові задачі (unobserved). Мета — записати
    /// деталі в лог і показати дружнє повідомлення замість "тихого" крешу.
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        // Виняток у UI-потоці: логуємо, показуємо повідомлення й не даємо застосунку впасти.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Фатальний виняток поза UI-потоком: застосунок, найпевніше, завершиться —
        // фіксуємо якомога більше деталей.
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        // Необроблений виняток у фоновій задачі: логуємо й позначаємо як опрацьований,
        // щоб не звалити процес під час фіналізації задачі.
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Необроблена помилка в UI-потоці");
        ShowFriendlyError();
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Log.Fatal(ex, "Критична необроблена помилка (IsTerminating={IsTerminating})", e.IsTerminating);
        Log.CloseAndFlush();
        ShowFriendlyError();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Необроблена помилка у фоновій задачі");
        e.SetObserved();
    }

    /// <summary>
    /// Показує локалізоване повідомлення про неочікувану помилку зі шляхом до журналу.
    /// Максимально захищене: ані відсутність локалізації, ані повторний виняток під час
    /// показу не мають призвести до каскаду помилок.
    /// </summary>
    private void ShowFriendlyError()
    {
        try
        {
            string title;
            string message;
            try
            {
                // Якщо хост не побудувався, звертання кине виняток — його ловить зовнішній catch → FallbackError.
                var loc = _host.Services.GetService<ILocalizationService>();
                if (loc is not null)
                {
                    title = loc.GetString("Error_Unexpected_Title");
                    message = string.Format(loc.GetString("Error_Unexpected_Message"), AppLog.LogDirectory);
                }
                else
                {
                    (title, message) = FallbackError();
                }
            }
            catch
            {
                // Локалізація могла бути ще не готова або сама спричинити виняток — останній рубіж.
                (title, message) = FallbackError();
            }

            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // Навіть показ повідомлення не повинен спричинити повторний виток винятків.
        }
    }

    /// <summary>
    /// Аварійний текст, якщо локалізація недоступна (напр. збій на старті до її ініціалізації).
    /// Це єдиний допустимий "зашитий" рядок — останній рубіж перед мовчазним крешем.
    /// </summary>
    private static (string Title, string Message) FallbackError() =>
        ("Family Tree", $"An unexpected error occurred. Details were saved to the log:{Environment.NewLine}{AppLog.LogDirectory}");
}
