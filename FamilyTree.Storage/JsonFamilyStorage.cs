using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FamilyTree.Storage.Serialization;

namespace FamilyTree.Storage;

/// <summary>
/// Сховище документа родини у файлі .familytree (JSON) за розд. 3.5:
/// атомарний запис (унікальний temp + скидання на диск + File.Replace з фолбеком),
/// версія схеми з ланцюжком міграторів, 5 нумерованих резервних копій у підпапці
/// <c>.backups</c> (<c>.1.bak</c> — найновіша), перевірка цілісності при завантаженні.
/// </summary>
public sealed class JsonFamilyStorage : IFamilyStorage, IDisposable
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

        // Без цього кирилиця писалася escape-послідовностями («Тест»):
        // файл роздувався ~втричі й перестав бути придатним для читання, diff-у та grep-у,
        // хоч формат позиціонується як людиночитний JSON. Безпечно — вивід не йде в HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IReadOnlyList<IFormatMigration> _migrations;

    // Серіалізує збереження в межах процесу (див. коментар у SaveAsync).
    // Міжпроцесний захист (два запущені екземпляри застосунку) — окреме питання.
    private readonly SemaphoreSlim _saveGate = new(1, 1);

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

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw FamilyFileException.Create(FileErrorKeys.Io, inner: null, path);

        var savedAt = DateTime.UtcNow;
        var dto = DocumentMapper.ToDto(document, CurrentSchemaVersion);

        // UpdatedAt ставимо в DTO, а не в документ: інакше після НЕВДАЛОГО збереження
        // документ у пам'яті мав час, якому на диску ніщо не відповідає.
        if (dto.Meta is { } meta)
        {
            meta.UpdatedAt = savedAt;
        }

        // Одне збереження за раз. Сховище зареєстроване як singleton і не мало локу,
        // тож два паралельні SaveAsync (Ctrl+S під час збереження при закритті, автосейв)
        // відкривали той самий temp з FileMode.Create й обрізали дані одне одного.
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);

            // Унікальне ім'я temp — друга половина захисту від того самого сценарію:
            // раніше воно було фіксованим (fullPath + ".tmp"), і catch одного збереження
            // видаляв temp іншого.
            var tempPath = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await WriteJsonAsync(tempPath, dto, cancellationToken).ConfigureAwait(false);

                // Точка для тестування атомарності (симуляція збою до заміни файлу).
                FaultBeforePromote?.Invoke();

                Promote(tempPath, fullPath);
            }
            catch
            {
                // Прибрати тимчасовий файл, лишивши цільовий недоторканим.
                TryDelete(tempPath);
                throw;
            }
        }
        finally
        {
            _saveGate.Release();
        }

        document.Meta.UpdatedAt = savedAt;
        document.IsDirty = false;
    }

    /// <summary>
    /// Пише JSON у тимчасовий файл із примусовим скиданням на диск.
    /// Без цього <see cref="File.Replace(string, string, string?)"/> був атомарним лише
    /// щодо метаданих: <c>WriteAllTextAsync</c> закриває дескриптор, але не робить
    /// <c>FlushFileBuffers</c>, тож при зникненні живлення NTFS могла зафіксувати
    /// перейменування, а блоки даних — ні, і цільовий файл ставав нульовим чи обрізаним.
    /// </summary>
    private static async Task WriteJsonAsync(string tempPath, FamilyFileDto dto, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
        };

        var stream = new FileStream(tempPath, options);
        await using (stream.ConfigureAwait(false))
        {
            // SerializeAsync замість Serialize у рядок: без проміжної копії всього
            // документа в пам'яті й без CPU-роботи на потоці викликача (тобто на UI).
            await JsonSerializer.SerializeAsync(stream, dto, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>Замінює цільовий файл підготованим тимчасовим.</summary>
    private static void Promote(string tempPath, string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            // overwrite: true — між File.Exists і перейменуванням файл міг з'явитися
            // (інший екземпляр, синхронізація OneDrive/Dropbox). Раніше це давало
            // IOException, а catch видаляв свіжозаписаний temp — робота користувача зникала.
            File.Move(tempPath, fullPath, overwrite: true);
            return;
        }

        // Резервна копія — «приємно мати». Раніше її провал (напр. задовгий шлях)
        // валив УСЕ збереження, хоч сам документ був цілком записуваний.
        TryBackup(fullPath);

        try
        {
            File.Replace(tempPath, fullPath, destinationBackupFileName: null);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
        {
            // ReplaceFile не універсальний: падає на FAT32/exFAT-флешках, частині
            // SMB-шар і в деяких синхронізованих теках. Фолбеку не було, тож
            // збереження в такі місця не працювало ніколи — хоч звичайне
            // перейменування з перезаписом там проходить.
            File.Move(tempPath, fullPath, overwrite: true);
        }
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

    private static void TryBackup(string fullPath)
    {
        try
        {
            RotateAndBackup(fullPath);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PathTooLongException
            or NotSupportedException)
        {
            // Не вдалося зробити копію — це не привід не зберегти документ.
        }
    }

    /// <summary>
    /// Нумеровані слоти: <c>.1.bak</c> — найновіша копія. Перед копіюванням зсуваємо
    /// <c>.4→.5</c>, <c>.3→.4</c> … <c>.1→.2</c>, найстаршу видаляємо.
    /// <para>
    /// Раніше ім'я містило тики (19 символів) і GUID (32) — разом +66 символів до шляху,
    /// через що збереження в глибокій (напр. синхронізованій) теці падало цілком.
    /// А сортування за іменем при однаковій часовій мітці порівнювало випадковий GUID —
    /// гранулярність <c>DateTime.UtcNow</c> у Windows ≈15,6 мс, тож кілька збережень
    /// підряд отримували однакові тики й видалятися могла новіша копія.
    /// </para>
    /// </summary>
    private static void RotateAndBackup(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        var fileName = Path.GetFileName(fullPath);
        var backupsDir = Path.Combine(directory, BackupsFolderName);
        Directory.CreateDirectory(backupsDir);

        string Slot(int index) => Path.Combine(backupsDir, $"{fileName}.{index}.bak");

        TryDelete(Slot(MaxBackups));
        for (var index = MaxBackups - 1; index >= 1; index--)
        {
            var from = Slot(index);
            if (File.Exists(from))
            {
                File.Move(from, Slot(index + 1), overwrite: true);
            }
        }

        File.Copy(fullPath, Slot(1), overwrite: true);
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

    public void Dispose() => _saveGate.Dispose();
}
