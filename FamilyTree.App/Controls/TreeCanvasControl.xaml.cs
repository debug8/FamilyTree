using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FamilyTree.App.ViewModels;

namespace FamilyTree.App.Controls;

public partial class TreeCanvasControl : UserControl
{
    private const double MinScale = 0.2;
    private const double MaxScale = 3.0;

    private readonly ScaleTransform _sceneScale = new();

    private bool _panning;
    private Point _panStart;
    private double _hOffsetStart;
    private double _vOffsetStart;

    public TreeCanvasControl()
    {
        InitializeComponent();

        Surface.LayoutTransform = _sceneScale; // зум сцени

        // Взаємодія полотна → команди ViewModel (полотно про VM не знає).
        Surface.NodeSelected += (_, node) => Vm?.SelectNode(node.PersonId);
        Surface.NodeActivated += (_, node) => Vm?.SetRoot(node.PersonId);
        Surface.NodePointerEntered += (_, node) => Vm?.HighlightChildrenOf(node.PersonId);
        Surface.CouplePointerEntered += (_, couple) => Vm?.HighlightChildrenOfCouple(couple.MemberA, couple.MemberB);
        Surface.EdgePointerEntered += (_, edge) => Vm?.HighlightEdge(edge);
        Surface.PointerExited += (_, _) => Vm?.ClearHighlight();
    }

    private TreeViewModel? Vm => DataContext as TreeViewModel;

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
        var oldScale = _sceneScale.ScaleX;
        var newScale = Math.Clamp(oldScale * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - oldScale) < 0.0001)
        {
            return;
        }

        // Точка контенту під курсором (у немасштабованих координатах).
        var contentX = (Scroller.HorizontalOffset + viewportPoint.X) / oldScale;
        var contentY = (Scroller.VerticalOffset + viewportPoint.Y) / oldScale;

        _sceneScale.ScaleX = _sceneScale.ScaleY = newScale;
        Surface.UpdateLayout();

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
        _sceneScale.ScaleX = _sceneScale.ScaleY = Math.Clamp(scale, MinScale, MaxScale);
        Surface.UpdateLayout();
        Scroller.ScrollToHorizontalOffset(0);
        Scroller.ScrollToVerticalOffset(0);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _sceneScale.ScaleX = _sceneScale.ScaleY = 1;
    }
}
