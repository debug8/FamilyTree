using CommunityToolkit.Mvvm.ComponentModel;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Головна ViewModel. На етапі T-0.1 — заглушка, що доводить роботу DI та MVVM-каркаса.
/// Наповнюється в Етапі 2 (список осіб, пошук, документне меню).
/// </summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Family Tree";
}
