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
