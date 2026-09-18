using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using RobloxLiveTranslator.Models;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Overlay;

public partial class OverlayWindow : Window
{
    private readonly IntPtr _target;
    private readonly AppSettings _settings;
    private readonly Action? _persist;
    private readonly Dictionary<Guid, RoiTranslationUpdate> _latest = new();
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private bool _editMode;
    private NativeMethods.RECT _lastRect;
    private bool _hasLastRect;

    public bool EditMode => _editMode;

    public OverlayWindow(IntPtr target, AppSettings settings, Action? persist = null)
    {
        InitializeComponent();
        _target = target;
        _settings = settings;
        _persist = persist;
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += (_, _) => AlignAndRender();
        Loaded += (_, _) =>
        {
            CaptureExclusion.Apply(this);
            ApplyClickThrough();
            _timer.Start();
            AlignAndRender(forceRender: true);
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

    public void ClearTranslation(Guid roiId)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ClearTranslation(roiId));
            return;
        }

        if (_latest.Remove(roiId)) Render();
    }

    public void RefreshSettings()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(RefreshSettings);
            return;
        }

        AlignAndRender(forceRender: true);
    }

    public void SetEditMode(bool enabled)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetEditMode(enabled));
            return;
        }
        _editMode = enabled;
        ApplyClickThrough();
        Render();
    }

    private void ApplyClickThrough()
    {
        if (!IsLoaded) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        if (_editMode) style &= ~NativeMethods.WS_EX_TRANSPARENT;
        else style |= NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, style);
        RootCanvas.IsHitTestVisible = _editMode;
    }

    private void AlignAndRender(bool forceRender = false)
    {
        if (!NativeMethods.TryGetClientScreenRect(_target, out var rect)) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        var changed = !_hasLastRect || rect.Left != _lastRect.Left || rect.Top != _lastRect.Top ||
                      rect.Right != _lastRect.Right || rect.Bottom != _lastRect.Bottom;
        _lastRect = rect;
        _hasLastRect = true;
        if (forceRender || changed) Render();
    }

    private void Render()
    {
        if (!IsLoaded || RootCanvas.ActualWidth <= 1 || RootCanvas.ActualHeight <= 1) return;
        RootCanvas.Children.Clear();

        if (_editMode)
        {
            var hint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(225, 30, 30, 30)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 5, 9, 5),
                Child = new TextBlock
                {
                    Text = "오버레이 편집: 박스=이동 · 파란 핸들=가로/세로 독립 크기 · 휠=가로 · Shift+휠=세로 · Ctrl+휠=투명도",
                    Foreground = Brushes.White,
                    FontSize = 12
                }
            };
            Canvas.SetLeft(hint, 10);
            Canvas.SetTop(hint, 10);
            RootCanvas.Children.Add(hint);
        }

        foreach (var roi in _settings.Rois.Where(r => r.Enabled && r.ShowOverlay))
        {
            if (!_latest.TryGetValue(roi.Id, out var update)) continue;
            var x = roi.X * RootCanvas.ActualWidth;
            var y = roi.Y * RootCanvas.ActualHeight;
            var widthScale = Math.Clamp(roi.OverlayWidthScale, 0.25, 5.0);
            var heightScale = Math.Clamp(roi.OverlayHeightScale, 0.35, 5.0);
            var baseWidth = Math.Max(140, roi.Width * RootCanvas.ActualWidth);
            var w = Math.Clamp(baseWidth * widthScale, 80, RootCanvas.ActualWidth);
            var roiHeight = roi.Height * RootCanvas.ActualHeight;
            var fontSize = Math.Clamp(roi.OverlayFontSize, 10, 42);

            var stack = new StackPanel();
            if (roi.OverlayShowSource)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = update.SourceText,
                    Foreground = Brushes.LightGray,
                    FontSize = Math.Max(9, fontSize * 0.65),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = w - 16
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = update.TargetText,
                Foreground = Brushes.White,
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = w - 16,
                Margin = new Thickness(0, roi.OverlayShowSource ? 2 : 0, 0, 0)
            });

            if (roi.OverlayShowMeta)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"{update.Provider} · OCR {update.OcrConfidence:P0} · 교차일치 {update.AgreementScore:P0}",
                    Foreground = Brushes.Gainsboro,
                    FontSize = Math.Max(8, fontSize * 0.52),
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Round(255 * Math.Clamp(roi.OverlayOpacity, 0.10, 1.0)), 18, 18, 18)),
                BorderBrush = _editMode ? Brushes.DeepSkyBlue :
                    (roi.EventMode ? Brushes.Orange : new SolidColorBrush(Color.FromArgb(180, 255, 255, 255))),
                BorderThickness = new Thickness(_editMode ? 2 : 1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 6, 8, 6),
                Child = stack,
                MaxWidth = w,
                Width = w,
                ClipToBounds = true,
                Cursor = _editMode ? Cursors.SizeAll : Cursors.Arrow
            };

            border.Measure(new Size(w, double.PositiveInfinity));
            var naturalH = Math.Max(36, border.DesiredSize.Height);
            var desiredH = Math.Clamp(naturalH * heightScale, 36, RootCanvas.ActualHeight);
            border.Height = desiredH;
            var below = y + roiHeight + 4;
            var automaticY = below + desiredH <= RootCanvas.ActualHeight ? below : Math.Max(0, y - desiredH - 4);
            var finalX = Math.Clamp(x + roi.OverlayOffsetX, 0, Math.Max(0, RootCanvas.ActualWidth - w));
            var finalY = Math.Clamp(automaticY + roi.OverlayOffsetY, 0, Math.Max(0, RootCanvas.ActualHeight - desiredH));

            FrameworkElement visual = border;
            if (_editMode)
            {
                var editor = new Grid { Width = w, Height = desiredH };
                editor.Children.Add(border);

                // Explicit controls are shown in edit mode so opacity/font sizing does not depend
                // on remembering modifier-wheel shortcuts.
                var toolbar = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(4)
                };
                Button MakeToolButton(string label, string tip, Action action)
                {
                    var button = new Button
                    {
                        Content = label,
                        ToolTip = tip,
                        Padding = new Thickness(5, 2, 5, 2),
                        Margin = new Thickness(2, 0, 0, 0),
                        FontSize = 10,
                        Opacity = 0.92
                    };
                    button.Click += (_, e) =>
                    {
                        action();
                        _persist?.Invoke();
                        Render();
                        e.Handled = true;
                    };
                    return button;
                }
                toolbar.Children.Add(MakeToolButton("투명-", "배경을 더 투명하게", () => roi.OverlayOpacity = Math.Clamp(roi.OverlayOpacity - 0.08, 0.10, 1.0)));
                toolbar.Children.Add(MakeToolButton("투명+", "배경을 더 진하게", () => roi.OverlayOpacity = Math.Clamp(roi.OverlayOpacity + 0.08, 0.10, 1.0)));
                toolbar.Children.Add(MakeToolButton("글자-", "번역 글자 작게", () => roi.OverlayFontSize = Math.Clamp(roi.OverlayFontSize - 1, 10, 42)));
                toolbar.Children.Add(MakeToolButton("글자+", "번역 글자 크게", () => roi.OverlayFontSize = Math.Clamp(roi.OverlayFontSize + 1, 10, 42)));
                editor.Children.Add(toolbar);

                var grip = new Thumb
                {
                    Width = 18,
                    Height = 18,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Cursor = Cursors.SizeNWSE,
                    Background = Brushes.DeepSkyBlue,
                    ToolTip = "드래그: 가로/세로 크기를 독립적으로 조절"
                };
                editor.Children.Add(grip);
                grip.DragDelta += (_, e) =>
                {
                    roi.OverlayWidthScale = Math.Clamp(roi.OverlayWidthScale + e.HorizontalChange / Math.Max(80, baseWidth), 0.25, 5.0);
                    roi.OverlayHeightScale = Math.Clamp(roi.OverlayHeightScale + e.VerticalChange / Math.Max(36, naturalH), 0.35, 5.0);
                    _persist?.Invoke();
                    Render();
                };

                Point dragStart = default;
                double startX = 0, startY = 0;
                border.MouseLeftButtonDown += (_, e) =>
                {
                    dragStart = e.GetPosition(RootCanvas);
                    startX = roi.OverlayOffsetX;
                    startY = roi.OverlayOffsetY;
                    border.CaptureMouse();
                    e.Handled = true;
                };
                border.MouseMove += (_, e) =>
                {
                    if (!border.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
                    var current = e.GetPosition(RootCanvas);
                    roi.OverlayOffsetX = Math.Clamp(startX + current.X - dragStart.X, -2000, 2000);
                    roi.OverlayOffsetY = Math.Clamp(startY + current.Y - dragStart.Y, -1600, 1600);
                    Canvas.SetLeft(editor, Math.Clamp(x + roi.OverlayOffsetX, 0, Math.Max(0, RootCanvas.ActualWidth - w)));
                    Canvas.SetTop(editor, Math.Clamp(automaticY + roi.OverlayOffsetY, 0, Math.Max(0, RootCanvas.ActualHeight - desiredH)));
                    e.Handled = true;
                };
                border.MouseLeftButtonUp += (_, e) =>
                {
                    if (border.IsMouseCaptured) border.ReleaseMouseCapture();
                    _persist?.Invoke();
                    e.Handled = true;
                };
                editor.PreviewMouseWheel += (_, e) =>
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                        roi.OverlayOpacity = Math.Clamp(roi.OverlayOpacity + Math.Sign(e.Delta) * 0.05, 0.10, 1.0);
                    else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                        roi.OverlayHeightScale = Math.Clamp(roi.OverlayHeightScale + Math.Sign(e.Delta) * 0.08, 0.35, 5.0);
                    else if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
                        roi.OverlayFontSize = Math.Clamp(roi.OverlayFontSize + Math.Sign(e.Delta), 10, 42);
                    else
                        roi.OverlayWidthScale = Math.Clamp(roi.OverlayWidthScale + Math.Sign(e.Delta) * 0.08, 0.25, 5.0);
                    _persist?.Invoke();
                    Render();
                    e.Handled = true;
                };
                visual = editor;
            }

            Canvas.SetLeft(visual, finalX);
            Canvas.SetTop(visual, finalY);
            RootCanvas.Children.Add(visual);
        }
    }
}
