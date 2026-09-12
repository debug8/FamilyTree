using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FamilyTree.App.ViewModels;

namespace FamilyTree.App.Controls;

/// <summary>
/// Полотно родинного графа: рендерить рамки-пари, ребра та вузли (той самий вигляд
/// у дереві та в «Хто кому»). Дані передаються через <see cref="NodesSource"/> /
/// <see cref="EdgesSource"/> / <see cref="CouplesSource"/>, а взаємодія віддається
/// назовні подіями — сам контрол не знає про конкретну ViewModel.
/// </summary>
public partial class FamilyGraphSurface : UserControl
{
    public static readonly DependencyProperty NodesSourceProperty = DependencyProperty.Register(
        nameof(NodesSource), typeof(IEnumerable), typeof(FamilyGraphSurface));

    public static readonly DependencyProperty EdgesSourceProperty = DependencyProperty.Register(
        nameof(EdgesSource), typeof(IEnumerable), typeof(FamilyGraphSurface));

    public static readonly DependencyProperty CouplesSourceProperty = DependencyProperty.Register(
        nameof(CouplesSource), typeof(IEnumerable), typeof(FamilyGraphSurface));

    public static readonly DependencyProperty BandsSourceProperty = DependencyProperty.Register(
        nameof(BandsSource), typeof(IEnumerable), typeof(FamilyGraphSurface));

    public static readonly DependencyProperty SceneWidthProperty = DependencyProperty.Register(
        nameof(SceneWidth), typeof(double), typeof(FamilyGraphSurface), new PropertyMetadata(0.0));

    public static readonly DependencyProperty SceneHeightProperty = DependencyProperty.Register(
        nameof(SceneHeight), typeof(double), typeof(FamilyGraphSurface), new PropertyMetadata(0.0));

    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(
        nameof(Interactive), typeof(bool), typeof(FamilyGraphSurface), new PropertyMetadata(true));

    public FamilyGraphSurface()
    {
        InitializeComponent();
    }

    /// <summary>Одиночний клік по вузлу.</summary>
    public event EventHandler<TreeNodeViewModel>? NodeSelected;

    /// <summary>Меню вузла: «Побудувати дерево від особи».</summary>
    public event EventHandler<TreeNodeViewModel>? NodeSetRootRequested;

    /// <summary>Меню вузла: «Редагувати».</summary>
    public event EventHandler<TreeNodeViewModel>? NodeEditRequested;

    /// <summary>Меню вузла: «Додати подружжя».</summary>
    public event EventHandler<TreeNodeViewModel>? NodeAddSpouseRequested;

    /// <summary>Меню вузла: «Видалити» (підтвердження — на боці виконавця).</summary>
    public event EventHandler<TreeNodeViewModel>? NodeDeleteRequested;

    /// <summary>Наведення на вузол.</summary>
    public event EventHandler<TreeNodeViewModel>? NodePointerEntered;

    /// <summary>Наведення на рамку подружжя.</summary>
    public event EventHandler<CoupleBoxViewModel>? CouplePointerEntered;

    /// <summary>Наведення на ребро.</summary>
    public event EventHandler<TreeEdgeViewModel>? EdgePointerEntered;

    /// <summary>Курсор залишив вузол/ребро/рамку.</summary>
    public event EventHandler? PointerExited;

    public IEnumerable? NodesSource
    {
        get => (IEnumerable?)GetValue(NodesSourceProperty);
        set => SetValue(NodesSourceProperty, value);
    }

    public IEnumerable? EdgesSource
    {
        get => (IEnumerable?)GetValue(EdgesSourceProperty);
        set => SetValue(EdgesSourceProperty, value);
    }

    public IEnumerable? CouplesSource
    {
        get => (IEnumerable?)GetValue(CouplesSourceProperty);
        set => SetValue(CouplesSourceProperty, value);
    }

    /// <summary>Смуги-фони поколінь (малюються позаду всього).</summary>
    public IEnumerable? BandsSource
    {
        get => (IEnumerable?)GetValue(BandsSourceProperty);
        set => SetValue(BandsSourceProperty, value);
    }

    public double SceneWidth
    {
        get => (double)GetValue(SceneWidthProperty);
        set => SetValue(SceneWidthProperty, value);
    }

    public double SceneHeight
    {
        get => (double)GetValue(SceneHeightProperty);
        set => SetValue(SceneHeightProperty, value);
    }

    /// <summary>Чи інтерактивне полотно (тултіпи, кліки, підсвітка). Для статичної схеми — false.</summary>
    public bool Interactive
    {
        get => (bool)GetValue(InteractiveProperty);
        set => SetValue(InteractiveProperty, value);
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!Interactive || sender is not FrameworkElement { DataContext: TreeNodeViewModel node })
        {
            return;
        }

        NodeSelected?.Invoke(this, node);
        e.Handled = true;
    }

    /// <summary>
    /// Правий клік по картці: виділяємо вузол, щоб було видно, над ким відкриється меню.
    /// Подію НЕ позначаємо обробленою — саме меню WPF відкриває на відпусканні правої
    /// кнопки, і робить це лише для необробленого ланцюжка.
    /// </summary>
    private void Node_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Interactive && sender is FrameworkElement { DataContext: TreeNodeViewModel node })
        {
            NodeSelected?.Invoke(this, node);
        }
    }

    private void NodeMenuSetRoot_Click(object sender, RoutedEventArgs e) => RaiseNodeMenu(sender, NodeSetRootRequested);

    private void NodeMenuEdit_Click(object sender, RoutedEventArgs e) => RaiseNodeMenu(sender, NodeEditRequested);

    private void NodeMenuAddSpouse_Click(object sender, RoutedEventArgs e) => RaiseNodeMenu(sender, NodeAddSpouseRequested);

    private void NodeMenuDelete_Click(object sender, RoutedEventArgs e) => RaiseNodeMenu(sender, NodeDeleteRequested);

    /// <summary>
    /// Спільна частина пунктів меню: вузол береться з DataContext пункту (ContextMenu,
    /// оголошене в шаблоні вузла, успадковує його DataContext), далі — відповідна подія.
    /// </summary>
    private void RaiseNodeMenu(object sender, EventHandler<TreeNodeViewModel>? handler)
    {
        if (Interactive && sender is FrameworkElement { DataContext: TreeNodeViewModel node })
        {
            handler?.Invoke(this, node);
        }
    }

    private void Node_MouseEnter(object sender, MouseEventArgs e)
    {
        if (Interactive && sender is FrameworkElement { DataContext: TreeNodeViewModel node })
        {
            NodePointerEntered?.Invoke(this, node);
        }
    }

    private void Couple_MouseEnter(object sender, MouseEventArgs e)
    {
        if (Interactive && sender is FrameworkElement { DataContext: CoupleBoxViewModel couple })
        {
            CouplePointerEntered?.Invoke(this, couple);
        }
    }

    private void Edge_MouseEnter(object sender, MouseEventArgs e)
    {
        if (Interactive && sender is FrameworkElement { DataContext: TreeEdgeViewModel edge })
        {
            EdgePointerEntered?.Invoke(this, edge);
        }
    }

    private void Surface_PointerLeave(object sender, MouseEventArgs e)
    {
        if (Interactive)
        {
            PointerExited?.Invoke(this, EventArgs.Empty);
        }
    }
}
