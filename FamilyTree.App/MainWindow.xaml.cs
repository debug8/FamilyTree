using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FamilyTree.App.ViewModels;

namespace FamilyTree.App;

public partial class MainWindow : Window
{
    // Індекс вкладки «Дерево» у MainTabs (0 — «Особа», 1 — «Дерево», 2 — «Хто кому»).
    private const int TreeTabIndex = 1;

    private bool _forceClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>
    /// Вмикає побудову дерева лише коли активна вкладка «Дерево» (B-07): поки відкрито
    /// «Особа»/«Хто кому», гортання списку й зміни вмісту не будують невидиме дерево.
    /// </summary>
    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Лише зміна вкладки верхнього TabControl, а не вкладених списків/комбо.
        if (!ReferenceEquals(e.OriginalSource, sender))
        {
            return;
        }

        if (sender is TabControl tabs && DataContext is MainViewModel vm)
        {
            vm.Tree.IsActive = tabs.SelectedIndex == TreeTabIndex;
        }
    }

    /// <summary>
    /// Дозволяє закрити вікно без повторного запиту в <see cref="OnClosing"/>.
    /// Викликається з <c>App.SessionEnding</c> (B-05) після того, як питання про
    /// збереження вже опрацьовано синхронно — інакше під час shutdown Windows
    /// користувача перепитали б удруге.
    /// </summary>
    public void AllowClose() => _forceClose = true;

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_forceClose || DataContext is not MainViewModel vm || !vm.HasUnsavedChanges)
        {
            return;
        }

        // Є незбережені зміни — питаємо й, за потреби, зберігаємо перед закриттям.
        e.Cancel = true;

        if (!await vm.PromptSaveIfDirtyAsync())
        {
            return; // користувач скасував закриття
        }

        _forceClose = true;

        // Close() НЕ можна кликати зсередини Closing: поки обробник не завершився,
        // вікно вважається таким, що вже закривається, і повторне закриття кидає
        // InvalidOperationException («Cannot set Visibility to Visible or call Show,
        // ShowDialog, Close … while a Window is closing»).
        //
        // Через `async void` це не теорія. На гілці «Не зберігати» PromptSaveIfDirtyAsync
        // не має жодної справжньої асинхронної операції (лише модальний MessageBox) і
        // завершується СИНХРОННО — тож продовження після await виконується прямо в стеку
        // Closing. На гілці «Зберегти» реальний запис на диск повертає керування пізніше,
        // і там воно спрацьовувало; звідси й враження, що баг «плаваючий».
        //
        // InvokeAsync із фоновим пріоритетом відкладає закриття до моменту, коли поточна
        // послідовність Closing уже розгорнулася.
        _ = Dispatcher.InvokeAsync(Close, DispatcherPriority.Background);
    }
}
