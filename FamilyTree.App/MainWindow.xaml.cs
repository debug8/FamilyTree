using System.Windows;
using FamilyTree.App.ViewModels;

namespace FamilyTree.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
