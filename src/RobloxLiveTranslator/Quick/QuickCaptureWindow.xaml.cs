using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Quick;

public partial class QuickCaptureWindow : Window
{
    private readonly DrawingBitmap _snapshot;
    private Point? _start;

    public DrawingBitmap? SelectedBitmap { get; private set; }
    public Rect? SelectedScreenRect { get; private set; }

    public QuickCaptureWindow(DrawingBitmap snapshot)
    {
        InitializeComponent();
        _snapshot = snapshot;
        ScreenshotImage.Source = ToBitmapSource(snapshot);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Focus();
        Cursor = Cursors.Cross;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Root);
        SelectionRect.Visibility = Visibility.Visible;
        SizePanel.Visibility = Visibility.Visible;
        CaptureMouse();
        UpdateSelection(_start.Value, _start.Value);
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_start is null || e.LeftButton != MouseButtonState.Pressed) return;
        UpdateSelection(_start.Value, e.GetPosition(Root));
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is null) return;
        var end = e.GetPosition(Root);
        ReleaseMouseCapture();
        var rect = Normalize(_start.Value, end);
        _start = null;
        if (rect.Width < 8 || rect.Height < 8)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            SizePanel.Visibility = Visibility.Collapsed;
            return;
        }

        var scaleX = _snapshot.Width / Math.Max(1.0, Root.ActualWidth);
        var scaleY = _snapshot.Height / Math.Max(1.0, Root.ActualHeight);
        var x = Math.Clamp((int)Math.Round(rect.X * scaleX), 0, _snapshot.Width - 1);
        var y = Math.Clamp((int)Math.Round(rect.Y * scaleY), 0, _snapshot.Height - 1);
        var w = Math.Clamp((int)Math.Round(rect.Width * scaleX), 1, _snapshot.Width - x);
        var h = Math.Clamp((int)Math.Round(rect.Height * scaleY), 1, _snapshot.Height - y);
        SelectedBitmap = _snapshot.Clone(new DrawingRectangle(x, y, w, h), PixelFormat.Format32bppArgb);
        SelectedScreenRect = new Rect(Left + rect.X, Top + rect.Y, rect.Width, rect.Height);
        DialogResult = true;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel();
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e) => Cancel();

    private void Cancel()
    {
        DialogResult = false;
        Close();
    }

    private void UpdateSelection(Point a, Point b)
    {
        var rect = Normalize(a, b);
        Canvas.SetLeft(SelectionRect, rect.X);
        Canvas.SetTop(SelectionRect, rect.Y);
        SelectionRect.Width = rect.Width;
        SelectionRect.Height = rect.Height;
        SizeText.Text = $"{Math.Round(rect.Width):0} × {Math.Round(rect.Height):0}";
        Canvas.SetLeft(SizePanel, Math.Min(Math.Max(0, rect.X), Math.Max(0, Root.ActualWidth - 120)));
        Canvas.SetTop(SizePanel, Math.Min(Math.Max(0, rect.Bottom + 6), Math.Max(0, Root.ActualHeight - 35)));
    }

    private static Rect Normalize(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    protected override void OnClosed(EventArgs e)
    {
        _snapshot.Dispose();
        base.OnClosed(e);
    }

    private static BitmapSource ToBitmapSource(DrawingBitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }
}
