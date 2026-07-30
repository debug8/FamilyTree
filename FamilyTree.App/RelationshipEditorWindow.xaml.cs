using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using FamilyTree.App.ViewModels;

namespace FamilyTree.App;

public partial class RelationshipEditorWindow : Window
{
    public RelationshipEditorWindow()
    {
        InitializeComponent();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>
    /// Подвійний клік по кандидату = підтвердження. Клік по порожньому місцю списку
    /// ігноруємо: інакше діалог закривався б із випадковим (попереднім) вибором.
    /// </summary>
    private void Candidates_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindItem(source) is null)
        {
            return;
        }

        if (DataContext is RelationshipEditorViewModel { CanConfirm: true, CanPickCandidate: true })
        {
            DialogResult = true;
        }
    }

    /// <summary>
    /// Найближчий <see cref="ListBoxItem"/> вище по дереву. Джерелом події може бути
    /// не-Visual (наприклад, Run у TextBlock), тому для таких вузлів піднімаємось
    /// логічним деревом — VisualTreeHelper на них кидає виняток.
    /// </summary>
    private static ListBoxItem? FindItem(DependencyObject source)
    {
        for (var node = source; node is not null;)
        {
            if (node is ListBoxItem item)
            {
                return item;
            }

            node = node is Visual or Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }
}
