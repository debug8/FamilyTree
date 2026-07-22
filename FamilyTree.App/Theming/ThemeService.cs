using System.Windows;

namespace FamilyTree.App.Theming;

/// <summary>
/// Реалізація <see cref="IThemeService"/>: тримає в
/// <see cref="Application.Current"/>.Resources.MergedDictionaries один тематичний
/// словник і підміняє його при перемиканні. Оскільки стилі та вікна посилаються
/// на пензлі через DynamicResource, зміна застосовується вживу, без перезапуску.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string DefaultCode = "light";

    private readonly Dictionary<string, ThemeOption> _options = new(StringComparer.OrdinalIgnoreCase)
    {
        ["light"] = new ThemeOption("light", "Theme_Light"),
        ["dark"] = new ThemeOption("dark", "Theme_Dark"),
    };

    private readonly List<ThemeOption> _available;
    private ThemeOption _current;
    private ResourceDictionary? _currentDictionary;

    public ThemeService()
    {
        _available = _options.Values.ToList();
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
