using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using RobloxLiveTranslator.Models;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Overlay;

public partial class OverlayWindow : Window
{
    private readonly IntPtr _target;
    private readonly AppSettings _settings;
    private readonly Dictionary<Guid, RoiTranslationUpdate> _latest = new();
    private readonly System.Windows.Threading.DispatcherTimer _timer;

    public OverlayWindow(IntPtr target, AppSettings settings)
    {
        InitializeComponent();
        _target = target;
        _settings = settings;
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => AlignAndRender();
        Loaded += (_, _) =>
        {
            MakeClickThrough();
            _timer.Start();
            AlignAndRender();
        };
        Closed += (_, _) => _timer.Stop();
    }

    public void UpdateTranslation(RoiTranslationUpdate update)
    {
        Dispatcher.Invoke(() =>
        {
            _latest[update.RoiId] = update;
            Render();
        });
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, style);
    }

    private void AlignAndRender()
    {
        if (!NativeMethods.TryGetClientScreenRect(_target, out var rect)) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        Render();
    }

    private void Render()
    {
        if (!IsLoaded || RootCanvas.ActualWidth <= 1 || RootCanvas.ActualHeight <= 1) return;
        RootCanvas.Children.Clear();

        foreach (var roi in _settings.Rois.Where(r => r.Enabled && r.ShowOverlay))
        {
            if (!_latest.TryGetValue(roi.Id, out var update)) continue;
            var x = roi.X * RootCanvas.ActualWidth;
            var y = roi.Y * RootCanvas.ActualHeight;
            var w = Math.Max(170, roi.Width * RootCanvas.ActualWidth);
            var roiHeight = roi.Height * RootCanvas.ActualHeight;

            var source = new TextBlock
            {
                Text = update.SourceText,
                Foreground = Brushes.LightGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = w - 16
            };
            var translated = new TextBlock
            {
                Text = update.TargetText,
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = w - 16,
                Margin = new Thickness(0, 2, 0, 0)
            };
            var meta = new TextBlock
            {
                Text = $"{update.Provider} · OCR {update.OcrConfidence:P0} · 교차일치 {update.AgreementScore:P0}",
                Foreground = Brushes.Gainsboro,
                FontSize = 9,
                Margin = new Thickness(0, 3, 0, 0)
            };
            var stack = new StackPanel();
            stack.Children.Add(source);
            stack.Children.Add(translated);
            stack.Children.Add(meta);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(205, 18, 18, 18)),
                BorderBrush = roi.EventMode ? Brushes.Orange : new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 6, 8, 6),
                Child = stack,
                MaxWidth = w
            };

            border.Measure(new Size(w, double.PositiveInfinity));
            var desiredH = border.DesiredSize.Height;
            var below = y + roiHeight + 4;
            var finalY = below + desiredH <= RootCanvas.ActualHeight ? below : Math.Max(0, y - desiredH - 4);
            Canvas.SetLeft(border, Math.Clamp(x, 0, Math.Max(0, RootCanvas.ActualWidth - w)));
            Canvas.SetTop(border, finalY);
            RootCanvas.Children.Add(border);
        }
    }
}
