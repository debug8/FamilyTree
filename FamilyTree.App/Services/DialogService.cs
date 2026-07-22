using System.Linq;
using System.Windows;
using FamilyTree.App.ViewModels;

namespace FamilyTree.App.Services;

/// <inheritdoc />
public sealed class DialogService : IDialogService
{
    public bool ShowPersonEditor(PersonEditorViewModel viewModel)
    {
        var window = new PersonEditorWindow
        {
            DataContext = viewModel,
            Owner = ActiveWindow,
        };
        return window.ShowDialog() == true;
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(ActiveWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    private static Window? ActiveWindow =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current.MainWindow;
}
