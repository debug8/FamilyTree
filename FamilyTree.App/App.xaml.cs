using System.IO;
using System.Linq;
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
        services.AddSingleton<IPhotoStore, PhotoStore>();

        // Сховище документа. Передаємо реальну версію збірки, щоб вона проставлялася
        // в metadata кожного збереженого файлу (B-65) — Storage навмисно не знає про WPF.
        services.AddSingleton<IFamilyStorage>(_ => new JsonFamilyStorage(AppInfo.Version));
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

        // 1b. Синхронізувати WPF-мову (xml:lang) з мовою UI ДО створення будь-якого вікна:
        //     інакше DatePicker парсив би дати за en-US незалежно від мови (B-01). Живе
        //     перемикання проставляє мову на вже відкриті вікна (метадані — лише раз).
        UiLanguage.Initialize(localization.CurrentCulture);
        localization.LanguageChanged += (_, _) => UiLanguage.Apply(localization.CurrentCulture);

        // 2. Ініціалізувати XAML-проксі локалізації ДО створення будь-яких сервісів/вікон
        //    (markup extension {loc:Localize} і LocalizedOption звертаються до LocalizationSource.Instance).
        LocalizationSource.Initialize(localization);

        // 3. Застосувати збережену тему (невідомий код — тихий відкат на основну, фірмову).
        var theme = _host.Services.GetRequiredService<IThemeService>();
        theme.SetTheme(settings.Current.Theme);

        // 4. Застосувати збережений стиль назв родства.
        var formatter = _host.Services.GetRequiredService<IKinshipFormatter>();
        formatter.Style = settings.Current.KinshipNamingStyle == "detailed"
            ? KinshipNamingStyle.Detailed
            : KinshipNamingStyle.Standard;

        // 4b. Застосувати збережену глибину дерева за замовчуванням.
        _host.Services.GetRequiredService<TreeViewModel>().Depth = settings.Current.DefaultTreeDepth;

        // 4c. Розмір шрифта імені особи — з налаштувань у ресурс, який читає
        //     PersonNameTextStyle. Запис прямо в Application.Resources перекриває
        //     значення зі злитого Controls.xaml (прямі ключі мають перевагу над
        //     MergedDictionaries), а DynamicResource у стилі підхоплює його вживу.
        //     Екрана налаштувань для цього поля ще немає — правиться в settings.json.
        Resources[PersonNameFontSizeKey] =
            AppSettings.ClampPersonNameFontSize(settings.Current.PersonNameFontSize);

        // 5. Показати головне вікно (із синхронізацією теми системного заголовка).
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        Theming.TitleBarThemer.Track(mainWindow, theme);
        mainWindow.Show();

        // 5b. Завершення/вихід із сеансу Windows (перезавантаження, вихід користувача) НЕ
        //     проходить через OnClosing — перехоплюємо SessionEnding і синхронно питаємо про
        //     збереження, інакше незбережене дерево тихо гине (B-05).
        SessionEnding += OnSessionEnding;

        // 6. Якщо застосунок запущено з файлом (асоціація .familytree) — відкрити його.
        var startupFile = e.Args.FirstOrDefault(arg =>
            arg.EndsWith(".familytree", StringComparison.OrdinalIgnoreCase) && File.Exists(arg));
        if (startupFile is not null)
        {
            await _host.Services.GetRequiredService<MainViewModel>().OpenFileAsync(startupFile);
        }

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

    /// <summary>Ключ ресурсу з розміром шрифта імені особи (див. Styles/Controls.xaml).</summary>
    private const string PersonNameFontSizeKey = "PersonNameFontSize";

    /// <summary>
    /// B-05: синхронний запит про збереження при завершенні сеансу Windows. Якщо користувач
    /// скасовує — скасовуємо й завершення сеансу; інакше дозволяємо вікну закритися без
    /// повторного запиту в OnClosing під час подальшого shutdown.
    /// </summary>
    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        var vm = _host.Services.GetService<MainViewModel>();
        if (vm is null)
        {
            return;
        }

        if (vm.HasUnsavedChanges && !vm.PromptSaveIfDirtyBlocking())
        {
            e.Cancel = true;
            return;
        }

        _host.Services.GetService<MainWindow>()?.AllowClose();
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
