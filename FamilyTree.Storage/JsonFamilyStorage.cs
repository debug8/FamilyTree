using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FamilyTree.Storage.Serialization;

namespace FamilyTree.Storage;

/// <summary>
/// Сховище документа родини у файлі .familytree (JSON) за розд. 3.5:
/// атомарний запис (temp + File.Replace), версія схеми з ланцюжком міграторів,
/// ротація 5 резервних копій у підпапці .backups.
/// </summary>
public sealed class JsonFamilyStorage : IFamilyStorage
{
    /// <summary>Поточна підтримувана версія схеми файлу.</summary>
    public const int CurrentSchemaVersion = 1;

    private const int MaxBackups = 5;
    private const string BackupsFolderName = ".backups";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IReadOnlyList<IFormatMigration> _migrations;

    /// <summary>
    /// Тестовий гачок: викликається після запису тимчасового файлу, але ДО заміни
    /// цільового. Дозволяє змоделювати збій і перевірити, що наявний файл не псується.
    /// </summary>
    internal Action? FaultBeforePromote { get; set; }

    public JsonFamilyStorage()
        : this(Array.Empty<IFormatMigration>())
    {
    }

    public JsonFamilyStorage(IEnumerable<IFormatMigration> migrations)
    {
        _migrations = migrations.ToList();
    }

    public async Task<FamilyDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        if (JsonNode.Parse(text) is not JsonObject root)
        {
            throw new InvalidDataException("Файл не є коректним JSON-об'єктом.");
        }

        var version = (int?)root["schemaVersion"]
            ?? throw new InvalidDataException("У файлі відсутнє поле schemaVersion.");

        if (version > CurrentSchemaVersion)
        {
            throw new UnsupportedSchemaVersionException(version, CurrentSchemaVersion);
        }

        root = ApplyMigrations(root, version);

        var dto = root.Deserialize<FamilyFileDto>(JsonOptions)
            ?? throw new InvalidDataException("Не вдалося розібрати вміст файлу.");

        return DocumentMapper.ToDomain(dto);
    }

    public async Task SaveAsync(FamilyDocument document, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        document.Meta.UpdatedAt = DateTime.UtcNow;

        var dto = DocumentMapper.ToDto(document, CurrentSchemaVersion);
        // Серіалізація в пам'ять: якщо тут станеться помилка — цільовий файл не змінено.
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = fullPath + ".tmp";

        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);

            // Точка для тестування атомарності (симуляція збою до заміни файлу).
            FaultBeforePromote?.Invoke();

            if (File.Exists(fullPath))
            {
                BackupExisting(fullPath);
                File.Replace(tempPath, fullPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }
        catch
        {
            // Прибрати тимчасовий файл, лишивши цільовий недоторканим.
            TryDelete(tempPath);
            throw;
        }

        document.IsDirty = false;
    }

    private JsonObject ApplyMigrations(JsonObject root, int version)
    {
        while (version < CurrentSchemaVersion)
        {
            var migration = _migrations.FirstOrDefault(m => m.FromVersion == version)
                ?? throw new InvalidOperationException($"Немає міграції з версії схеми {version}.");

            root = migration.Migrate(root);
            version++;
            root["schemaVersion"] = version;
        }

        return root;
    }

    private static void BackupExisting(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        var fileName = Path.GetFileName(fullPath);
        var backupsDir = Path.Combine(directory, BackupsFolderName);
        Directory.CreateDirectory(backupsDir);

        // Унікальне ім'я з часовою міткою (тики) для сортування + GUID від колізій.
        var backupName = $"{fileName}.{DateTime.UtcNow.Ticks:D19}.{Guid.NewGuid():N}.bak";
        File.Copy(fullPath, Path.Combine(backupsDir, backupName), overwrite: false);

        RotateBackups(backupsDir, fileName);
    }

    private static void RotateBackups(string backupsDir, string fileName)
    {
        var backups = Directory.GetFiles(backupsDir, $"{fileName}.*.bak")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal) // тики у назві → новіші першими
            .ToList();

        foreach (var stale in backups.Skip(MaxBackups))
        {
            TryDelete(stale);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ігноруємо: невдале прибирання бекапу/temp не є критичним.
        }
    }
}
