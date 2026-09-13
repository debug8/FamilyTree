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

        // Розмір вікна виставляємо ТУТ, а не прив'язкою до ViewModel. Прив'язка на
        // Window.Height не працює: вікно бере Width/Height для створення нативного вікна ще
        // до того, як прив'язка встигає активуватися (її Status лишається Unattached), тож у
        // Height їде NaN — «авто», тобто типовий системний розмір. Та сама прив'язка, змінена
        // у ВЖЕ відкритому вікні, спрацьовує миттєво — елемент на той час живий; через це
        // розходження баг і виглядає загадковим.
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// У режимі редагування списку кандидатів немає — лишається один рядок «Шлюб з». Тому
    /// рядок сітки перестає тягнутися, а висоту рахує WPF за вмістом. Це чесніше за фіксоване
    /// число: додане згодом поле саме розсуне вікно, а не опиниться за його краєм.
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not RelationshipEditorViewModel { IsEditMode: true })
        {
            return;
        }

        CandidatesRow.Height = GridLength.Auto;
        SizeToContent = SizeToContent.Height;
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
