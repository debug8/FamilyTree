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

    /// <summary>Подвійний клік по вузлу (напр. зробити коренем).</summary>
    public event EventHandler<TreeNodeViewModel>? NodeActivated;

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

        if (e.ClickCount == 2)
        {
            NodeActivated?.Invoke(this, node);
        }
        else
        {
            NodeSelected?.Invoke(this, node);
        }

        e.Handled = true;
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
