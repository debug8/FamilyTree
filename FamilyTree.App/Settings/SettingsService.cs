using System.IO;
using System.Text.Json;

namespace FamilyTree.App.Settings;

/// <summary>
/// Реалізація <see cref="ISettingsService"/>: зберігає settings.json у
/// %AppData%\FamilyTree\. Запис атомарний (тимчасовий файл + заміна).
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;
    private AppSettings _current = new();

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FamilyTree");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
    }

    public AppSettings Current => _current;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            else
            {
                _current = new AppSettings();
            }
        }
        catch
        {
            // Пошкоджений файл налаштувань не має ламати запуск — беремо значення за замовчуванням.
            _current = new AppSettings();
        }

        return _current;
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_current, JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(_filePath))
        {
            File.Replace(tempPath, _filePath, null);
        }
        else
        {
            File.Move(tempPath, _filePath);
        }
    }
}
