using System.Linq;
using System.Windows;
using FamilyTree.App.ViewModels;

namespace FamilyTree.App.Services;

/// <inheritdoc />
public sealed class DialogService : IDialogService
{
    public bool ShowPersonEditor(PersonEditorViewModel viewModel) =>
        ShowDialog(new PersonEditorWindow { DataContext = viewModel });

    public bool ShowRelationshipEditor(RelationshipEditorViewModel viewModel) =>
        ShowDialog(new RelationshipEditorWindow { DataContext = viewModel });

    public bool Confirm(string message, string title) =>
        MessageBox.Show(ActiveWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    public void ShowMessage(string message, string title) =>
        MessageBox.Show(ActiveWindow!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private static bool ShowDialog(Window window)
    {
        window.Owner = ActiveWindow;
        return window.ShowDialog() == true;
    }

    private static Window? ActiveWindow =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current.MainWindow;
}
