using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FamilyTree.App.Theming;

/// <summary>
/// Робить системний заголовок вікна (caption bar) темним у темній темі через DWM-атрибут
/// <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c>. Працює на Windows 10 (1809+) та Windows 11;
/// на старіших системах виклик просто ігнорується (без винятків).
/// </summary>
public static class TitleBarThemer
{
    // Значення атрибута змінювалося між збірками Windows 10.
    private const int DwmwaUseImmersiveDarkModeOld = 19; // 1809–1903
    private const int DwmwaUseImmersiveDarkMode = 20;    // 2004+ / Windows 11

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Застосовує темний/світлий заголовок до конкретного вікна (якщо HWND уже створено).</summary>
    public static void Apply(Window window, bool isDark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var flag = isDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref flag, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref flag, sizeof(int));
        }
    }

    /// <summary>
    /// Застосовує тему до заголовка вікна й тримає її синхронною з <see cref="IThemeService"/>:
    /// перше застосування — щойно з'явиться HWND (без «спалаху» світлого заголовка),
    /// далі — при кожній зміні теми. Підписка знімається при закритті вікна.
    /// </summary>
    public static void Track(Window window, IThemeService theme)
    {
        void ApplyCurrent() => Apply(window, IsDark(theme));

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

    private static bool IsDark(IThemeService theme) =>
        string.Equals(theme.CurrentTheme.Code, "dark", StringComparison.OrdinalIgnoreCase);
}
