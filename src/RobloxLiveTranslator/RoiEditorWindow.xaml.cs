using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using RobloxLiveTranslator.Models;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator;

public partial class RoiEditorWindow : Window
{
    private readonly IntPtr _target;
    private readonly List<RoiDefinition> _rois;
    private Point? _start;
    private Rectangle? _working;

    public IReadOnlyList<RoiDefinition> Result => _rois;

    public RoiEditorWindow(IntPtr target, IEnumerable<RoiDefinition> existing)
    {
        InitializeComponent();
        _target = target;
        _rois = existing.Select(x => x.Clone()).ToList();
        SourceInitialized += (_, _) => AlignToTarget();
        Loaded += (_, _) => Redraw();
    }

    private void AlignToTarget()
    {
        if (!NativeMethods.TryGetClientScreenRect(_target, out var rect)) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_SHOWWINDOW);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(RoiCanvas);
        _working = new Rectangle
        {
            Stroke = Brushes.Lime,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(45, 0, 255, 0))
        };
        RoiCanvas.Children.Add(_working);
        CaptureMouse();
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_start is null || _working is null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(RoiCanvas);
        SetRect(_working, _start.Value, p);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is null || _working is null) return;
        var end = e.GetPosition(RoiCanvas);
        var x = Math.Min(_start.Value.X, end.X);
        var y = Math.Min(_start.Value.Y, end.Y);
        var w = Math.Abs(end.X - _start.Value.X);
        var h = Math.Abs(end.Y - _start.Value.Y);
        ReleaseMouseCapture();
        _start = null;
        _working = null;

        if (w >= 20 && h >= 12 && RoiCanvas.ActualWidth > 1 && RoiCanvas.ActualHeight > 1)
        {
            _rois.Add(new RoiDefinition
            {
                Name = $"ROI {_rois.Count + 1}",
                X = x / RoiCanvas.ActualWidth,
                Y = y / RoiCanvas.ActualHeight,
                Width = w / RoiCanvas.ActualWidth,
                Height = h / RoiCanvas.ActualHeight
            });
        }
        Redraw();
        e.Handled = true;
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_rois.Count > 0) _rois.RemoveAt(_rois.Count - 1);
        Redraw();
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
            Close();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void Redraw()
    {
        RoiCanvas.Children.Clear();
        for (var i = 0; i < _rois.Count; i++)
        {
            var roi = _rois[i];
            var rect = new Rectangle
            {
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(35, 0, 160, 255)),
                Width = roi.Width * RoiCanvas.ActualWidth,
                Height = roi.Height * RoiCanvas.ActualHeight
            };
            Canvas.SetLeft(rect, roi.X * RoiCanvas.ActualWidth);
            Canvas.SetTop(rect, roi.Y * RoiCanvas.ActualHeight);
            RoiCanvas.Children.Add(rect);

            var label = new System.Windows.Controls.TextBlock
            {
                Text = $"{i + 1}. {roi.Name}",
                Foreground = Brushes.White,
                Background = Brushes.DodgerBlue,
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 12
            };
            Canvas.SetLeft(label, roi.X * RoiCanvas.ActualWidth);
            Canvas.SetTop(label, Math.Max(0, roi.Y * RoiCanvas.ActualHeight - 22));
            RoiCanvas.Children.Add(label);
        }
    }

    private static void SetRect(Rectangle rect, Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        rect.Width = Math.Abs(b.X - a.X);
        rect.Height = Math.Abs(b.Y - a.Y);
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
    }
}
