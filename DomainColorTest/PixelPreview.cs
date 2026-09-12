using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

/// <summary>Native WPF preview: integer device-pixel zoom, alpha and scrolling.</summary>
public sealed class PixelPreview : Grid
{
    private readonly ScrollViewer _scroll;
    private readonly Surface _surface = new();
    private readonly TextBlock _empty;
    private Bitmap? _image;
    private double _zoom;
    private int _background;
    private double _relativeX, _relativeY;
    private bool _applyingScroll;
    private int _scrollRevision;
    public event EventHandler? ScrollPositionChanged;
    public (double X, double Y) ScrollPosition => (_relativeX, _relativeY);
    public double EffectiveZoom => _surface.Source is { } source ? _surface.ImageWidth * VisualTreeHelper.GetDpi(this).DpiScaleX / source.PixelWidth : 1;
    public Bitmap? Image
    {
        get => _image;
        set
        {
            _surface.Source = value is null ? null : ToBitmapSource(value);
            _image = value;
            _empty.Visibility = value is null ? Visibility.Visible : Visibility.Collapsed;
            UpdateScale();
        }
    }
    public string EmptyText { get => _empty.Text; set => _empty.Text = value; }
    public double Zoom { get => _zoom; set { _zoom = value; UpdateScale(); } }
    public int PreviewBackground { get => _background; set { _background = value; UpdateBackground(); } }

    public PixelPreview()
    {
        ClipToBounds = true;
        _scroll = new ScrollViewer { Content = _surface, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        Children.Add(_scroll);
        _empty = new TextBlock { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16), IsHitTestVisible = false };
        Children.Add(_empty);
        SizeChanged += (_, _) => UpdateScale();
        _scroll.ScrollChanged += (_, e) =>
        {
            if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0 || e.ExtentWidthChange != 0 || e.ExtentHeightChange != 0)
            {
                UpdateScale();
                ScrollToRelativePosition(_relativeX, _relativeY);
                return;
            }
            if (_applyingScroll || (e.HorizontalChange == 0 && e.VerticalChange == 0)) return;
            if (_scroll.ScrollableWidth > 0) _relativeX = _scroll.HorizontalOffset / _scroll.ScrollableWidth;
            if (_scroll.ScrollableHeight > 0) _relativeY = _scroll.VerticalOffset / _scroll.ScrollableHeight;
            ScrollPositionChanged?.Invoke(this, EventArgs.Empty);
        };
        UpdateBackground();
    }

    public void ScrollToRelativePosition(double x, double y)
    {
        _relativeX = double.IsFinite(x) ? Math.Clamp(x, 0, 1) : 0;
        _relativeY = double.IsFinite(y) ? Math.Clamp(y, 0, 1) : 0;
        _applyingScroll = true;
        int revision = ++_scrollRevision;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (revision != _scrollRevision) return;
            _scroll.ScrollToHorizontalOffset(_relativeX * _scroll.ScrollableWidth);
            _scroll.ScrollToVerticalOffset(_relativeY * _scroll.ScrollableHeight);
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (revision == _scrollRevision) _applyingScroll = false;
            }));
        }));
    }

    public Point ImagePointAt(Point viewportPoint)
    {
        double left = Math.Max(0, (_surface.ActualWidth - _surface.ImageWidth) / 2);
        double top = Math.Max(0, (_surface.ActualHeight - _surface.ImageHeight) / 2);
        return new Point(
            Math.Clamp((_scroll.HorizontalOffset + viewportPoint.X - left) / Math.Max(1, _surface.ImageWidth), 0, 1),
            Math.Clamp((_scroll.VerticalOffset + viewportPoint.Y - top) / Math.Max(1, _surface.ImageHeight), 0, 1));
    }

    public void PlaceImagePointAt(Point imagePoint, Point viewportPoint)
    {
        UpdateLayout();
        double left = Math.Max(0, (_surface.ActualWidth - _surface.ImageWidth) / 2);
        double top = Math.Max(0, (_surface.ActualHeight - _surface.ImageHeight) / 2);
        ScrollToRelativePosition(
            _scroll.ScrollableWidth > 0 ? (left + imagePoint.X * _surface.ImageWidth - viewportPoint.X) / _scroll.ScrollableWidth : _relativeX,
            _scroll.ScrollableHeight > 0 ? (top + imagePoint.Y * _surface.ImageHeight - viewportPoint.Y) / _scroll.ScrollableHeight : _relativeY);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateScale();
    }

    private void UpdateScale()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        bool fit = _zoom <= 0;
        _scroll.HorizontalScrollBarVisibility = _scroll.VerticalScrollBarVisibility = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        double availableWidth = Math.Max(1, _scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : ActualWidth);
        double availableHeight = Math.Max(1, _scroll.ViewportHeight > 0 ? _scroll.ViewportHeight : ActualHeight);
        if (_surface.Source is { } source)
        {
            double scale = fit ? Math.Min(Math.Max(1, availableWidth * dpi.DpiScaleX - 16) / source.PixelWidth, Math.Max(1, availableHeight * dpi.DpiScaleY - 16) / source.PixelHeight) : _zoom;
            if (fit && scale >= 1) scale = Math.Floor(scale);
            _surface.ImageWidth = Math.Max(1, Math.Round(source.PixelWidth * scale)) / dpi.DpiScaleX;
            _surface.ImageHeight = Math.Max(1, Math.Round(source.PixelHeight * scale)) / dpi.DpiScaleY;
        }
        else _surface.ImageWidth = _surface.ImageHeight = 0;
        // Let the scroll presenter stretch small images without feeding its viewport back into the extent.
        _surface.MinWidth = _surface.ImageWidth;
        _surface.MinHeight = _surface.ImageHeight;
        _surface.InvalidateVisual();
    }

    private void UpdateBackground()
    {
        var dark = new SolidColorBrush(Color.FromRgb(17, 17, 17));
        if (_background == 0)
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
            var light = new SolidColorBrush(Color.FromRgb(29, 29, 29));
            drawing.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
            drawing.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(12, 12, 12, 12))));
            var checker = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 24, 24), Stretch = Stretch.None };
            checker.Freeze();
            Background = checker;
        }
        else Background = _background == 1 ? dark : new SolidColorBrush(Color.FromRgb(224, 224, 224));
        _empty.Foreground = _background == 2 ? Brushes.DimGray : new SolidColorBrush(Color.FromRgb(170, 170, 170));
    }

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        // Copy BGRA directly; HBITMAP conversion can lose alpha and requires GDI handle ownership.
        using var converted = bitmap.Clone(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bits = converted.LockBits(new System.Drawing.Rectangle(0, 0, converted.Width, converted.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int stride = checked(converted.Width * 4);
            byte[] pixels = new byte[checked(stride * converted.Height)];
            for (int y = 0; y < converted.Height; y++) Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * stride, stride);
            var source = BitmapSource.Create(converted.Width, converted.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            source.Freeze();
            return source;
        }
        finally { converted.UnlockBits(bits); }
    }

    private sealed class Surface : FrameworkElement
    {
        public BitmapSource? Source { get; set; }
        public double ImageWidth { get; set; }
        public double ImageHeight { get; set; }
        public Surface() { RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor); SnapsToDevicePixels = true; }
        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Source is null) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            double x = Math.Round(Math.Max(0, (ActualWidth - ImageWidth) / 2) * dpi.DpiScaleX) / dpi.DpiScaleX;
            double y = Math.Round(Math.Max(0, (ActualHeight - ImageHeight) / 2) * dpi.DpiScaleY) / dpi.DpiScaleY;
            drawingContext.DrawImage(Source, new Rect(x, y, ImageWidth, ImageHeight));
        }
    }
}
