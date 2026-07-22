using System.Windows;
using FamilyTree.App.ViewModels;

namespace FamilyTree.App;

public partial class PersonEditorWindow : Window
{
    public PersonEditorWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PersonEditorViewModel vm && vm.Commit() is not null)
        {
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
