using System.Linq;
using System.Windows;
using FamilyTree.App.ViewModels;
using Microsoft.Win32;

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

    public SaveChangesResult ConfirmSaveChanges(string message, string title) =>
        MessageBox.Show(ActiveWindow!, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning) switch
        {
            MessageBoxResult.Yes => SaveChangesResult.Save,
            MessageBoxResult.No => SaveChangesResult.Discard,
            _ => SaveChangesResult.Cancel,
        };

    public string? AskOpenPath(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(ActiveWindow) == true ? dialog.FileName : null;
    }

    public string? AskSavePath(string filter, string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = suggestedName,
            DefaultExt = ".familytree",
            AddExtension = true,
        };
        return dialog.ShowDialog(ActiveWindow) == true ? dialog.FileName : null;
    }

    private static bool ShowDialog(Window window)
    {
        window.Owner = ActiveWindow;
        return window.ShowDialog() == true;
    }

    private static Window? ActiveWindow =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current.MainWindow;
}
