using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FamilyTree.App.Theming;

/// <summary>
/// Фарбує системний заголовок вікна (caption bar) під поточну тему через DWM.
///
/// Два рівні, бо Windows дає різні можливості:
/// 1) <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> — світлий/темний заголовок,
///    Windows 10 (1809+) і Windows 11;
/// 2) <c>DWMWA_CAPTION_COLOR</c> / <c>DWMWA_TEXT_COLOR</c> / <c>DWMWA_BORDER_COLOR</c> —
///    конкретний колір, ЛИШЕ Windows 11 (build 22000+).
///
/// Якщо тема задала колір, а система його не підтримує, виклик просто вертає
/// помилку — залишається світлий/темний заголовок із першого рівня. Градієнт у
/// системному заголовку неможливий: DWM приймає лише суцільний COLORREF.
/// </summary>
public static class TitleBarThemer
{
    // Значення атрибута змінювалося між збірками Windows 10.
    private const int DwmwaUseImmersiveDarkModeOld = 19; // 1809–1903
    private const int DwmwaUseImmersiveDarkMode = 20;    // 2004+ / Windows 11

    // Кольори заголовка — Windows 11 22000+.
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    // Просить DWM повернути стандартний колір замість заданого.
    private const int DwmwaColorDefault = unchecked((int)0xFFFFFFFF);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Застосовує оформлення заголовка конкретного вікна (якщо HWND уже створено).</summary>
    public static void Apply(Window window, ThemeOption theme)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // 1. Базовий світлий/темний режим — працює всюди від Windows 10 1809.
        var flag = theme.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref flag, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref flag, sizeof(int));
        }

        // 2. Точні кольори — лише Windows 11. Явно скидаємо на стандартні,
        // якщо тема кольору не задає: інакше при перемиканні тем колір
        // попередньої лишався б на вікні.
        SetColor(hwnd, DwmwaCaptionColor, theme.CaptionColor);
        SetColor(hwnd, DwmwaTextColor, theme.CaptionTextColor);
        SetColor(hwnd, DwmwaBorderColor, theme.CaptionColor);
    }

    /// <summary>
    /// Застосовує тему до заголовка вікна й тримає її синхронною з <see cref="IThemeService"/>:
    /// перше застосування — щойно з'явиться HWND (без «спалаху» світлого заголовка),
    /// далі — при кожній зміні теми. Підписка знімається при закритті вікна.
    /// </summary>
    public static void Track(Window window, IThemeService theme)
    {
        void ApplyCurrent() => Apply(window, theme.CurrentTheme);

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            ApplyCurrent();
        }
        else
        {
            window.SourceInitialized += OnSourceInitialized;
        }

        theme.ThemeChanged += OnThemeChanged;
        window.Closed += OnClosed;

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            ApplyCurrent();
        }

        void OnThemeChanged(object? sender, EventArgs e) => ApplyCurrent();

        void OnClosed(object? sender, EventArgs e)
        {
            theme.ThemeChanged -= OnThemeChanged;
            window.Closed -= OnClosed;
        }
    }

    private static void SetColor(IntPtr hwnd, int attribute, Color? color)
    {
        var value = color is { } c ? ToColorRef(c) : DwmwaColorDefault;
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
    }

    /// <summary>COLORREF для DWM — байти в порядку 0x00BBGGRR, а не 0x00RRGGBB.</summary>
    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);
}
