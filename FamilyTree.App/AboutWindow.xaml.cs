using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Navigation;
using Serilog;

namespace FamilyTree.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>
    /// Відкриває зовнішнє посилання системним браузером.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>UseShellExecute = true</c> обов'язковий.</b> У .NET (Core і далі) він за
    /// замовчуванням <c>false</c>, і <c>Process.Start</c> з URL кидає
    /// <see cref="Win32Exception"/> «The system cannot find the file specified» — саме
    /// на цьому ламається класичний приклад, скопійований із .NET Framework.
    /// </para>
    /// <para>
    /// Виняток назовні не випускаємо: відсутній браузер за замовчуванням, зламана
    /// асоціація чи політика, яка забороняє запуск процесів, — не привід валити вікно
    /// «Про програму» через глобальний обробник. Адреса лишається видимою в тултіпі,
    /// тож її можна прочитати й відкрити руками. У лог пишемо, бо тиха відмова, від
    /// якої ніде не лишилося сліду, — це те, з чим потім неможливо розібратися.
    /// </para>
    /// <para>
    /// Адреси беруться з <c>AboutViewModel</c> і є константами збірки, а не введенням
    /// користувача, тож підстановки чужої команди тут бути не може.
    /// </para>
    /// </remarks>
    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception
            or InvalidOperationException
            or ObjectDisposedException
            or FileNotFoundException
            or PlatformNotSupportedException)
        {
            Log.Warning(ex, "Не вдалося відкрити посилання {Uri}", e.Uri);
        }

        e.Handled = true;
    }
}
