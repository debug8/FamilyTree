using System.IO;
using Serilog;
using Serilog.Core;

namespace FamilyTree.App.Diagnostics;

/// <summary>
/// Централізоване налаштування логування (Serilog) у файл у теці даних застосунку.
/// Ініціалізується якнайраніше в <see cref="App"/>, щоб фіксувати навіть помилки старту.
/// Глобальний <see cref="Log.Logger"/> використовується статично — без залежності від DI,
/// бо логер має бути доступний ще до побудови хоста та у критичних обробниках помилок.
/// </summary>
public static class AppLog
{
    private static bool _initialized;

    /// <summary>Тека з файлами логів: <c>%AppData%\FamilyTree\logs</c>.</summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FamilyTree",
        "logs");

    /// <summary>
    /// Налаштовує глобальний <see cref="Log.Logger"/> (файловий лог із денною ротацією,
    /// зберігається останні 7 файлів). Безпечно викликати повторно — повторні виклики ігноруються.
    /// Помилка створення теки/файлу не має валити застосунок: у такому разі лишається "тихий" логер.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    path: Path.Combine(LogDirectory, "family-tree-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        catch
        {
            // Якщо файловий лог налаштувати не вдалося — не валимо застосунок:
            // лишається "тихий" логер, а глобальні обробники все одно покажуть повідомлення.
            Log.Logger = Logger.None;
        }

        _initialized = true;
    }

    /// <summary>Скидає буфери й закриває логер (виклик при виході із застосунку).</summary>
    public static void Shutdown() => Log.CloseAndFlush();
}
