using System.Globalization;
using System.IO;
using System.Resources;
using System.Text.Json;
using System.Threading;

namespace FamilyTree.App.Localization;

/// <summary>
/// Реалізація <see cref="ILocalizationService"/>.
/// Вбудовані мови беруться з ресурсів (Strings.resx / Strings.en.resx),
/// додаткові — з JSON-файлів перекладів (мови/діалекти без власної CultureInfo).
/// Ідентифікація — за власним кодом; форматування дат/чисел — за FormattingCulture
/// з відкатом, якщо CultureInfo для мови не існує.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private const string DefaultCode = "uk";

    // Базове ім'я ресурсу = простір імен + шлях: FamilyTree.App.Resources.Strings
    private static readonly ResourceManager ResourceManager =
        new("FamilyTree.App.Resources.Strings", typeof(LocalizationService).Assembly);

    // Нейтральна культура — джерело відкату для відсутніх ключів (українська/базовий resx).
    private static readonly CultureInfo NeutralCulture = CultureInfo.GetCultureInfo(DefaultCode);

    private readonly Dictionary<string, LanguageOption> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CultureInfo> _builtInCultures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _customStrings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LanguageOption> _available = new();

    private LanguageOption _current;

    public LocalizationService()
        : this(DefaultCustomLanguagesDirectory())
    {
    }

    /// <param name="customLanguagesDirectory">
    /// Тека з JSON-файлами додаткових мов. Якщо null або не існує — використовуються лише вбудовані.
    /// </param>
    public LocalizationService(string? customLanguagesDirectory)
    {
        // 1. Вбудовані мови (мають реальну CultureInfo).
        RegisterBuiltIn("uk");
        RegisterBuiltIn("en");

        // 2. Додаткові мови з файлів (можуть не мати CultureInfo).
        LoadCustomLanguages(customLanguagesDirectory);

        // 3. Початкова мова — за замовчуванням; культура потоку виставляється одразу.
        _current = ResolveOption(DefaultCode);
        Apply(_current);
    }

    public LanguageOption CurrentLanguage => _current;

    public CultureInfo CurrentCulture => _current.FormattingCulture;

    public IReadOnlyList<LanguageOption> AvailableLanguages => _available;

    public event EventHandler? LanguageChanged;

    public string GetString(string key)
    {
        // Спершу — переклад додаткової мови (якщо активна саме вона й ключ присутній).
        if (_customStrings.TryGetValue(_current.Code, out var dict) &&
            dict.TryGetValue(key, out var custom))
        {
            return custom;
        }

        // Інакше — ресурси: культура вбудованої мови або нейтральна (відкат).
        var culture = _builtInCultures.TryGetValue(_current.Code, out var c) ? c : NeutralCulture;
        return ResourceManager.GetString(key, culture) ?? $"[{key}]";
    }

    public void SetLanguage(string code)
    {
        var option = ResolveOption(code);
        if (_current.Code.Equals(option.Code, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _current = option;
        Apply(option);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    // --- допоміжне -------------------------------------------------------

    private void RegisterBuiltIn(string code)
    {
        var culture = CultureInfo.GetCultureInfo(code);
        _builtInCultures[code] = culture;
        AddOption(new LanguageOption(code, NativeDisplayName(culture), culture));
    }

    private void LoadCustomLanguages(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var code = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(code) || _options.ContainsKey(code))
                {
                    continue; // порожнє ім'я або конфлікт із вбудованою — пропускаємо
                }

                var json = File.ReadAllText(file);
                var parsed = JsonSerializer.Deserialize<CustomLanguageFile>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                if (parsed is null)
                {
                    continue;
                }

                var display = string.IsNullOrWhiteSpace(parsed.DisplayName) ? code : parsed.DisplayName!;
                var formatting = ResolveFormattingCulture(parsed.FormattingCulture, NeutralCulture);

                _customStrings[code] = parsed.Strings;
                AddOption(new LanguageOption(code, display, formatting));
            }
            catch
            {
                // Битий файл перекладу не має ламати запуск — просто ігноруємо його.
            }
        }
    }

    private void AddOption(LanguageOption option)
    {
        _options[option.Code] = option;
        _available.Add(option);
    }

    private LanguageOption ResolveOption(string? code)
    {
        if (!string.IsNullOrWhiteSpace(code) && _options.TryGetValue(code, out var option))
        {
            return option;
        }

        // Невідомий код — тихий відкат на мову за замовчуванням.
        return _options[DefaultCode];
    }

    private static void Apply(LanguageOption option)
    {
        Thread.CurrentThread.CurrentCulture = option.FormattingCulture;
        Thread.CurrentThread.CurrentUICulture = option.FormattingCulture;
        CultureInfo.DefaultThreadCurrentCulture = option.FormattingCulture;
        CultureInfo.DefaultThreadCurrentUICulture = option.FormattingCulture;
    }

    /// <summary>Повертає CultureInfo за кодом; якщо код порожній/невідомий — запасну культуру (без винятку).</summary>
    private static CultureInfo ResolveFormattingCulture(string? code, CultureInfo fallback)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return fallback;
        }

        try
        {
            return CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return fallback;
        }
    }

    private static string NativeDisplayName(CultureInfo culture)
    {
        var native = culture.NativeName;
        return native.Length > 0
            ? char.ToUpper(native[0], culture) + native[1..]
            : culture.DisplayName;
    }

    private static string DefaultCustomLanguagesDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FamilyTree",
            "languages");
}
