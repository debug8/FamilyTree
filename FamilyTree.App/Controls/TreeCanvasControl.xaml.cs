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
    private double _translateStartX;
    private double _translateStartY;

    public TreeCanvasControl()
    {
        InitializeComponent();
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var oldScale = SceneScale.ScaleX;
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var newScale = Math.Clamp(oldScale * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - oldScale) < 0.0001)
        {
            return;
        }

        // Масштаб відносно позиції курсора: точка під курсором лишається на місці.
        var mouse = e.GetPosition(Viewport);
        var sceneX = (mouse.X - SceneTranslate.X) / oldScale;
        var sceneY = (mouse.Y - SceneTranslate.Y) / oldScale;

        SceneScale.ScaleX = SceneScale.ScaleY = newScale;
        SceneTranslate.X = mouse.X - sceneX * newScale;
        SceneTranslate.Y = mouse.Y - sceneY * newScale;
        e.Handled = true;
    }

    private void Viewport_PanStart(object sender, MouseButtonEventArgs e)
    {
        _panning = true;
        _panStart = e.GetPosition(Viewport);
        _translateStartX = SceneTranslate.X;
        _translateStartY = SceneTranslate.Y;
        Viewport.CaptureMouse();
    }

    private void Viewport_PanMove(object sender, MouseEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        var current = e.GetPosition(Viewport);
        SceneTranslate.X = _translateStartX + (current.X - _panStart.X);
        SceneTranslate.Y = _translateStartY + (current.Y - _panStart.Y);
    }

    private void Viewport_PanEnd(object sender, MouseButtonEventArgs e)
    {
        _panning = false;
        Viewport.ReleaseMouseCapture();
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

    private void Fit_Click(object sender, RoutedEventArgs e) => FitToView();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        SceneScale.ScaleX = SceneScale.ScaleY = 1;
        SceneTranslate.X = SceneTranslate.Y = 0;
    }

    private void FitToView()
    {
        if (DataContext is not TreeViewModel vm || vm.CanvasWidth <= 0 || vm.CanvasHeight <= 0)
        {
            return;
        }

        var availableWidth = Viewport.ActualWidth;
        var availableHeight = Viewport.ActualHeight;
        if (availableWidth <= 0 || availableHeight <= 0)
        {
            return;
        }

        var scale = Math.Min(availableWidth / (vm.CanvasWidth + 40), availableHeight / (vm.CanvasHeight + 40));
        scale = Math.Clamp(scale, MinScale, MaxScale);

        SceneScale.ScaleX = SceneScale.ScaleY = scale;
        SceneTranslate.X = (availableWidth - vm.CanvasWidth * scale) / 2;
        SceneTranslate.Y = (availableHeight - vm.CanvasHeight * scale) / 2;
    }
}
