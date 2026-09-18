using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private Point? _createStart;
    private Rectangle? _working;
    private Guid? _selectedId;
    private Point? _moveStart;
    private double _startX;
    private double _startY;

    public IReadOnlyList<RoiDefinition> Result => _rois;

    public RoiEditorWindow(IntPtr target, IEnumerable<RoiDefinition> existing)
    {
        InitializeComponent();
        _target = target;
        _rois = existing.Select(x => x.Clone()).ToList();
        SourceInitialized += (_, _) => AlignToTarget();
        Loaded += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
    }

    private void AlignToTarget()
    {
        if (!NativeMethods.TryGetClientScreenRect(_target, out var rect)) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_SHOWWINDOW);
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source != RoiCanvas) return;
        _selectedId = null;
        _createStart = e.GetPosition(RoiCanvas);
        _working = new Rectangle
        {
            Stroke = Brushes.Lime,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(45, 0, 255, 0))
        };
        RoiCanvas.Children.Add(_working);
        RoiCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_createStart is null || _working is null || e.LeftButton != MouseButtonState.Pressed) return;
        SetRect(_working, _createStart.Value, e.GetPosition(RoiCanvas));
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_createStart is null || _working is null) return;
        var end = e.GetPosition(RoiCanvas);
        var x = Math.Min(_createStart.Value.X, end.X);
        var y = Math.Min(_createStart.Value.Y, end.Y);
        var w = Math.Abs(end.X - _createStart.Value.X);
        var h = Math.Abs(end.Y - _createStart.Value.Y);
        RoiCanvas.ReleaseMouseCapture();
        _createStart = null;
        _working = null;

        if (w >= 20 && h >= 12 && RoiCanvas.ActualWidth > 1 && RoiCanvas.ActualHeight > 1)
        {
            var roi = new RoiDefinition
            {
                Name = $"ROI {_rois.Count + 1}",
                X = x / RoiCanvas.ActualWidth,
                Y = y / RoiCanvas.ActualHeight,
                Width = w / RoiCanvas.ActualWidth,
                Height = h / RoiCanvas.ActualHeight
            };
            _rois.Add(roi);
            _selectedId = roi.Id;
        }
        Redraw();
        e.Handled = true;
    }

    private void Roi_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle rect || rect.Tag is not Guid id) return;
        _selectedId = id;
        var roi = _rois.First(r => r.Id == id);
        _moveStart = e.GetPosition(RoiCanvas);
        _startX = roi.X;
        _startY = roi.Y;
        rect.CaptureMouse();
        e.Handled = true;
    }

    private void Roi_MouseMove(object sender, MouseEventArgs e)
    {
        if (_moveStart is null || e.LeftButton != MouseButtonState.Pressed || sender is not Rectangle rect || rect.Tag is not Guid id) return;
        var roi = _rois.First(r => r.Id == id);
        var p = e.GetPosition(RoiCanvas);
        var dx = (p.X - _moveStart.Value.X) / Math.Max(1, RoiCanvas.ActualWidth);
        var dy = (p.Y - _moveStart.Value.Y) / Math.Max(1, RoiCanvas.ActualHeight);
        roi.X = Math.Clamp(_startX + dx, 0, Math.Max(0, 1 - roi.Width));
        roi.Y = Math.Clamp(_startY + dy, 0, Math.Max(0, 1 - roi.Height));
        Canvas.SetLeft(rect, roi.X * RoiCanvas.ActualWidth);
        Canvas.SetTop(rect, roi.Y * RoiCanvas.ActualHeight);
        e.Handled = true;
    }

    private void Roi_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Rectangle rect && rect.IsMouseCaptured) rect.ReleaseMouseCapture();
        _moveStart = null;
        Redraw();
        e.Handled = true;
    }

    private void Resize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.Tag is not Guid id) return;
        var roi = _rois.First(r => r.Id == id);
        var minW = 20.0 / Math.Max(1, RoiCanvas.ActualWidth);
        var minH = 12.0 / Math.Max(1, RoiCanvas.ActualHeight);
        roi.Width = Math.Clamp(roi.Width + e.HorizontalChange / Math.Max(1, RoiCanvas.ActualWidth), minW, 1 - roi.X);
        roi.Height = Math.Clamp(roi.Height + e.VerticalChange / Math.Max(1, RoiCanvas.ActualHeight), minH, 1 - roi.Y);
        var rect = RoiCanvas.Children.OfType<Rectangle>().FirstOrDefault(x => x.Tag is Guid tag && tag == id);
        if (rect is not null)
        {
            rect.Width = roi.Width * RoiCanvas.ActualWidth;
            rect.Height = roi.Height * RoiCanvas.ActualHeight;
        }
        Canvas.SetLeft(thumb, Math.Max(0, (roi.X + roi.Width) * RoiCanvas.ActualWidth - 9));
        Canvas.SetTop(thumb, Math.Max(0, (roi.Y + roi.Height) * RoiCanvas.ActualHeight - 9));
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e) => DeleteSelected();
    private void Save_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back) DeleteSelected();
        else if (e.Key == Key.Enter) { DialogResult = true; Close(); }
        else if (e.Key == Key.Escape) { DialogResult = false; Close(); }
    }

    private void DeleteSelected()
    {
        if (_selectedId is null) return;
        _rois.RemoveAll(x => x.Id == _selectedId.Value);
        _selectedId = null;
        Redraw();
    }

    private void Redraw()
    {
        if (!IsLoaded || RoiCanvas.ActualWidth <= 1 || RoiCanvas.ActualHeight <= 1) return;
        RoiCanvas.Children.Clear();
        for (var i = 0; i < _rois.Count; i++) AddRoiVisual(_rois[i], i);
    }

    private void AddRoiVisual(RoiDefinition roi, int index)
    {
        var selected = roi.Id == _selectedId;
        var rect = new Rectangle
        {
            Tag = roi.Id,
            Stroke = selected ? Brushes.Lime : Brushes.DeepSkyBlue,
            StrokeThickness = selected ? 3 : 2,
            Fill = new SolidColorBrush(Color.FromArgb(selected ? (byte)55 : (byte)35, 0, 160, 255)),
            Width = roi.Width * RoiCanvas.ActualWidth,
            Height = roi.Height * RoiCanvas.ActualHeight,
            Cursor = Cursors.SizeAll
        };
        rect.MouseLeftButtonDown += Roi_MouseLeftButtonDown;
        rect.MouseMove += Roi_MouseMove;
        rect.MouseLeftButtonUp += Roi_MouseLeftButtonUp;
        Canvas.SetLeft(rect, roi.X * RoiCanvas.ActualWidth);
        Canvas.SetTop(rect, roi.Y * RoiCanvas.ActualHeight);
        RoiCanvas.Children.Add(rect);

        var label = new TextBlock
        {
            Tag = $"label:{roi.Id}",
            Text = $"{index + 1}. {roi.Name}",
            Foreground = Brushes.White,
            Background = selected ? Brushes.ForestGreen : Brushes.DodgerBlue,
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 12,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, roi.X * RoiCanvas.ActualWidth);
        Canvas.SetTop(label, Math.Max(0, roi.Y * RoiCanvas.ActualHeight - 22));
        RoiCanvas.Children.Add(label);

        if (selected)
        {
            var grip = new Thumb
            {
                Tag = roi.Id,
                Width = 18,
                Height = 18,
                Background = Brushes.DeepSkyBlue,
                Cursor = Cursors.SizeNWSE,
                ToolTip = "드래그하여 ROI 가로/세로 크기 변경"
            };
            grip.DragDelta += Resize_DragDelta;
            grip.DragCompleted += (_, _) => Redraw();
            Canvas.SetLeft(grip, Math.Max(0, (roi.X + roi.Width) * RoiCanvas.ActualWidth - 9));
            Canvas.SetTop(grip, Math.Max(0, (roi.Y + roi.Height) * RoiCanvas.ActualHeight - 9));
            RoiCanvas.Children.Add(grip);
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
