using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        if (await vm.PromptSaveIfDirtyAsync())
        {
            _forceClose = true;
            Close();
        }
    }
}
