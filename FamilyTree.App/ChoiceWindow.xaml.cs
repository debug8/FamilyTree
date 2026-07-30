using System.Windows;
using FamilyTree.App.ViewModels;

namespace FamilyTree.App;

/// <summary>
/// Діалог із трьома відповідями: «Так», «Ні», «Пізніше». Результат пишеться
/// у <see cref="ChoiceViewModel.Choice"/>, тож викликач читає його з тієї ж VM.
/// </summary>
public partial class ChoiceWindow : Window
{
    public ChoiceWindow()
    {
        InitializeComponent();
    }

    private void Yes_Click(object sender, RoutedEventArgs e) => Finish(ThreeWayChoice.Yes);

    private void No_Click(object sender, RoutedEventArgs e) => Finish(ThreeWayChoice.No);

    private void Later_Click(object sender, RoutedEventArgs e) => Finish(ThreeWayChoice.Later);

    private void Finish(ThreeWayChoice choice)
    {
        if (DataContext is ChoiceViewModel vm)
        {
            vm.Choice = choice;
        }

        // true/false тут не несе сенсу — рішення передається через VM.
        DialogResult = choice != ThreeWayChoice.Later;
    }
}
