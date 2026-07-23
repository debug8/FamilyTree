using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FamilyTree.App.ViewModels;

namespace FamilyTree.App.Controls;

public partial class TreeCanvasControl : UserControl
{
    private const double MinScale = 0.2;
    private const double MaxScale = 3.0;

    private bool _panning;
    private Point _panStart;
    private double _hOffsetStart;
    private double _vOffsetStart;

    public TreeCanvasControl()
    {
        InitializeComponent();
    }

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+колесо — зум до курсора; Shift+колесо — горизонтальний скрол;
        // звичайне колесо — вертикальний скрол (обробляє ScrollViewer сам).
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Zoom(e.Delta > 0 ? 1.1 : 1 / 1.1, e.GetPosition(Scroller));
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void Zoom(double factor, Point viewportPoint)
    {
        var oldScale = SceneScale.ScaleX;
        var newScale = Math.Clamp(oldScale * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - oldScale) < 0.0001)
        {
            return;
        }

        // Точка контенту під курсором (у немасштабованих координатах).
        var contentX = (Scroller.HorizontalOffset + viewportPoint.X) / oldScale;
        var contentY = (Scroller.VerticalOffset + viewportPoint.Y) / oldScale;

        SceneScale.ScaleX = SceneScale.ScaleY = newScale;
        Scene.UpdateLayout();

        Scroller.ScrollToHorizontalOffset(contentX * newScale - viewportPoint.X);
        Scroller.ScrollToVerticalOffset(contentY * newScale - viewportPoint.Y);
    }

    private void Pan_Start(object sender, MouseButtonEventArgs e)
    {
        _panning = true;
        _panStart = e.GetPosition(Scroller);
        _hOffsetStart = Scroller.HorizontalOffset;
        _vOffsetStart = Scroller.VerticalOffset;
        Scroller.CaptureMouse();
    }

    private void Pan_Move(object sender, MouseEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        var current = e.GetPosition(Scroller);
        Scroller.ScrollToHorizontalOffset(_hOffsetStart - (current.X - _panStart.X));
        Scroller.ScrollToVerticalOffset(_vOffsetStart - (current.Y - _panStart.Y));
    }

    private void Pan_End(object sender, MouseButtonEventArgs e)
    {
        _panning = false;
        Scroller.ReleaseMouseCapture();
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeNodeViewModel node } && DataContext is TreeViewModel vm)
        {
            if (e.ClickCount == 2)
            {
                vm.SetRoot(node.PersonId);
            }
            else
            {
                vm.SelectNode(node.PersonId);
            }

            e.Handled = true;
        }
    }

    private void Node_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeNodeViewModel node } && DataContext is TreeViewModel vm)
        {
            vm.HighlightChildrenOf(node.PersonId);
        }
    }

    private void Couple_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CoupleBoxViewModel couple } && DataContext is TreeViewModel vm)
        {
            vm.HighlightChildrenOfCouple(couple.MemberA, couple.MemberB);
        }
    }

    private void Edge_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeEdgeViewModel edge } && DataContext is TreeViewModel vm)
        {
            vm.HighlightEdge(edge);
        }
    }

    private void ClearHighlight(object sender, MouseEventArgs e)
    {
        if (DataContext is TreeViewModel vm)
        {
            vm.ClearHighlight();
        }
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TreeViewModel vm || vm.CanvasWidth <= 0 || vm.CanvasHeight <= 0)
        {
            return;
        }

        var viewportWidth = Scroller.ViewportWidth;
        var viewportHeight = Scroller.ViewportHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        var scale = Math.Min(viewportWidth / (vm.CanvasWidth + 40), viewportHeight / (vm.CanvasHeight + 40));
        SceneScale.ScaleX = SceneScale.ScaleY = Math.Clamp(scale, MinScale, MaxScale);
        Scene.UpdateLayout();
        Scroller.ScrollToHorizontalOffset(0);
        Scroller.ScrollToVerticalOffset(0);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        SceneScale.ScaleX = SceneScale.ScaleY = 1;
    }
}
