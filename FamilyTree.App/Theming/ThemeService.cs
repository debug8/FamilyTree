using System.Windows;
using System.Windows.Media;

namespace FamilyTree.App.Theming;

/// <summary>
/// Реалізація <see cref="IThemeService"/>: тримає в
/// <see cref="Application.Current"/>.Resources.MergedDictionaries один тематичний
/// словник і підміняє його при перемиканні. Оскільки стилі та вікна посилаються
/// на пензлі через DynamicResource, зміна застосовується вживу, без перезапуску.
/// </summary>
public sealed class ThemeService : IThemeService
{
    /// <summary>Основна тема: фірмові кольори з іконки застосунку.</summary>
    private const string DefaultCode = "brand";

    // Код теми = ім'я файлу Styles\Theme.{Code}.xaml (див. ApplyDictionary),
    // тому «brand» вимагає саме Theme.Brand.xaml.
    //
    // captionColor/captionTextColor фарбують системний заголовок вікна й діють
    // лише на Windows 11; там, де вони не задані (світла/темна), заголовок
    // залишається стандартним для свого режиму.
    private readonly Dictionary<string, ThemeOption> _options = new(StringComparer.OrdinalIgnoreCase)
    {
        ["brand"] = new ThemeOption(
            "brand",
            "Theme_Brand",
            captionColor: Color.FromRgb(0x15, 0x55, 0x6B),
            captionTextColor: Colors.White),
        ["light"] = new ThemeOption("light", "Theme_Light"),
        ["dark"] = new ThemeOption("dark", "Theme_Dark", isDark: true),
    };

    private readonly List<ThemeOption> _available;
    private ThemeOption _current;
    private ResourceDictionary? _currentDictionary;

    // Порядок у списку задаємо явно, а не покладаємось на перебір Dictionary:
    // це те, що бачить користувач у комбобоксі тем.
    private static readonly string[] DisplayOrder = ["brand", "light", "dark"];

    public ThemeService()
    {
        _available = DisplayOrder.Select(code => _options[code]).ToList();
        _current = _options[DefaultCode];
        ApplyDictionary(_current);
    }

    public ThemeOption CurrentTheme => _current;

    public IReadOnlyList<ThemeOption> AvailableThemes => _available;

    public event EventHandler? ThemeChanged;

    public void SetTheme(string code)
    {
        var option = Resolve(code);
        if (_current.Code.Equals(option.Code, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _current = option;
        ApplyDictionary(option);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyDictionary(ThemeOption option)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Styles/Theme.{Capitalize(option.Code)}.xaml", UriKind.Absolute),
        };

        var merged = app.Resources.MergedDictionaries;
        if (_currentDictionary is not null)
        {
            merged.Remove(_currentDictionary);
        }

        merged.Add(dict);
        _currentDictionary = dict;
    }

    private ThemeOption Resolve(string? code) =>
        !string.IsNullOrWhiteSpace(code) && _options.TryGetValue(code, out var option)
            ? option
            : _options[DefaultCode];

    private static string Capitalize(string code) =>
        code.Length > 0 ? char.ToUpperInvariant(code[0]) + code[1..] : code;
}
