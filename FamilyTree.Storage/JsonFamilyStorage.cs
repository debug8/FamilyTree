using System.IO;
using System.Text;
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

        var text = await ReadTextAsync(path, cancellationToken).ConfigureAwait(false);
        var root = ParseRoot(text);
        var version = ReadSchemaVersion(root);

        if (version > CurrentSchemaVersion)
        {
            throw new UnsupportedSchemaVersionException(version, CurrentSchemaVersion);
        }

        root = ApplyMigrations(root, version);

        FamilyFileDto? dto;
        try
        {
            dto = root.Deserialize<FamilyFileDto>(JsonOptions);
        }
        catch (JsonException ex)
        {
            // Тип поля не відповідає схемі (напр. "persons": 5 або "birthDate": "вчора").
            throw FamilyFileException.Create(FileErrorKeys.MalformedJson, ex, ex.Message);
        }

        if (dto is null)
        {
            throw FamilyFileException.Create(FileErrorKeys.MissingSection, inner: null, "root");
        }

        var document = DocumentMapper.ToDomain(dto);

        // Перевірка цілісності ДО повернення документа: раніше битий файл валив
        // застосунок аж у ToDictionary(p => p.Id) вже після SetDocument().
        document.RepairedIssues = DocumentIntegrity.Verify(document);

        return document;
    }

    /// <summary>
    /// Читає файл строго як UTF-8. BOM (UTF-8/UTF-16) розпізнається автоматично,
    /// а от файл, перезбережений у Notepad як «ANSI» (Windows-1251), раніше тихо
    /// декодувався з U+FFFD — кирилиця ставала крякозябрами й у такому вигляді
    /// зберігалася назад. Тепер це явна помилка.
    /// </summary>
    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DecoderFallbackException ex)
        {
            throw FamilyFileException.Create(FileErrorKeys.BadEncoding, ex);
        }
        catch (FileNotFoundException ex)
        {
            throw FamilyFileException.Create(FileErrorKeys.NotFound, ex, path);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw FamilyFileException.Create(FileErrorKeys.NotFound, ex, path);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw FamilyFileException.Create(FileErrorKeys.AccessDenied, ex, path);
        }
        catch (IOException ex)
        {
            throw FamilyFileException.Create(FileErrorKeys.Io, ex, path);
        }
    }

    private static JsonObject ParseRoot(string text)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            // Раніше JsonException летів «як є», і користувач бачив
            // "'x' is an invalid start of a value. LineNumber: 3 …".
            throw FamilyFileException.Create(FileErrorKeys.MalformedJson, ex, ex.Message);
        }

        return node as JsonObject
            ?? throw FamilyFileException.Create(FileErrorKeys.MalformedJson, inner: null, "root is not an object");
    }

    /// <summary>
    /// Читає schemaVersion толерантно до типу. Приведення <c>(int?)root["schemaVersion"]</c>
    /// кидало <see cref="InvalidOperationException"/>, якщо версія у файлі — рядок "1",
    /// дріб 1.5 або true; це виглядало як внутрішня помилка, а не як «файл невалідний».
    /// </summary>
    private static int ReadSchemaVersion(JsonObject root)
    {
        if (root["schemaVersion"] is not JsonValue value || !value.TryGetValue<int>(out var version))
        {
            throw FamilyFileException.Create(FileErrorKeys.BadSchemaVersion, inner: null);
        }

        if (version < 1)
        {
            throw FamilyFileException.Create(FileErrorKeys.BadSchemaVersion, inner: null);
        }

        return version;
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
