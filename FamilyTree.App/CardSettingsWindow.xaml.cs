using System.Windows;

namespace FamilyTree.App;

public partial class CardSettingsWindow : Window
{
    public CardSettingsWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
