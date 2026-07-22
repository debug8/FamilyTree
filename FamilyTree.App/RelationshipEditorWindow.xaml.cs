using System.Windows;

namespace FamilyTree.App;

public partial class RelationshipEditorWindow : Window
{
    public RelationshipEditorWindow()
    {
        InitializeComponent();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
