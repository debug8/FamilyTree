using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FamilyTree.App.Localization;
using FamilyTree.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

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

    // ---- Експорт дерева у PNG (T-4.5) -----------------------------------

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        var loc = App.Services.GetService<ILocalizationService>();
        string L(string key) => loc?.GetString(key) ?? key;

        if (DataContext is not TreeViewModel vm || vm.CanvasWidth <= 0 || vm.CanvasHeight <= 0)
        {
            MessageBox.Show(L("Export_Empty"), L("Export_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = L("Export_Png_Filter"),
            DefaultExt = ".png",
            AddExtension = true,
            FileName = "family-tree.png",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            // 1× / 2× / 3× — індекс 0..2 → множник 1..3 (за замовчуванням 2×).
            var requestedScale = (ExportScaleBox?.SelectedIndex ?? 1) + 1.0;
            var bitmap = RenderSceneToBitmap(vm, requestedScale);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using (var stream = File.Create(dialog.FileName))
            {
                encoder.Save(stream);
            }

            MessageBox.Show(
                string.Format(L("Export_Done"), dialog.FileName),
                L("Export_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, L("File_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Рендерить ПОВНУ сцену у растр. Використовує ОКРЕМЕ полотно поза деревом, а не те,
    /// що на екрані: результат не залежить від зуму, позиції прокрутки чи обрізання видимою
    /// областю й не має «зсуву» від Margin. Розмір береться з розкладки (CanvasWidth/Height),
    /// плюс поле навколо, щоб картки не впиралися в край. Застосовується суперсемплінг
    /// <paramref name="requestedScale"/>× з обмеженням площі, щоб не вичерпати пам'ять.
    /// </summary>
    private RenderTargetBitmap RenderSceneToBitmap(TreeViewModel vm, double requestedScale)
    {
        const double margin = 15.0; // поле навколо дерева (у пікселях сцени)

        var sceneWidth = Math.Max(1.0, vm.CanvasWidth);
        var sceneHeight = Math.Max(1.0, vm.CanvasHeight);

        // Незалежна копія полотна: ті самі джерела даних, без інтерактиву, з полем від краю.
        var surface = new FamilyGraphSurface
        {
            Interactive = false,
            NodesSource = vm.Nodes,
            EdgesSource = vm.Edges,
            CouplesSource = vm.Couples,
            BandsSource = vm.Bands,
            SceneWidth = sceneWidth,
            SceneHeight = sceneHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(margin),
        };

        // Контейнер із фоном теми, розміщений у (0,0) — прямий рендер без VisualBrush та офсету.
        var background = TryFindResource("BackgroundBrush") as Brush ?? Brushes.White;
        var host = new Grid
        {
            Width = sceneWidth + (2 * margin),
            Height = sceneHeight + (2 * margin),
            Background = background,
        };
        host.Children.Add(surface);

        var size = new Size(host.Width, host.Height);
        host.Measure(size);
        host.Arrange(new Rect(size));
        host.UpdateLayout();

        // Множник суперсемплінгу — заданий користувачем (1×/2×/3×), але не вище, ніж дозволяє
        // бюджет площі. RenderTargetBitmap із dpi = 96·scale і pixel = dip·scale дає різкий
        // текст без зміни розкладки.
        const long maxPixels = 40_000_000;
        var maxScale = Math.Sqrt(maxPixels / (size.Width * size.Height));
        var scale = Math.Clamp(Math.Min(requestedScale, maxScale), 0.25, requestedScale);

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * scale),
            (int)Math.Ceiling(size.Height * scale),
            96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);

        bitmap.Render(host);
        bitmap.Freeze();
        return bitmap;
    }
}
