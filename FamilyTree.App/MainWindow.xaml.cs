using System.ComponentModel;
using System.Windows;
using FamilyTree.App.ViewModels;

namespace FamilyTree.App;

public partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
