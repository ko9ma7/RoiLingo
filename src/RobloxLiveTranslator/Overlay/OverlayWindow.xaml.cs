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
    private sealed record OverlayEntry(RoiTranslationUpdate Update, DateTimeOffset ExpiresAt);

    private readonly IntPtr _target;
    private readonly AppSettings _settings;
    private readonly Action? _persist;
    private readonly Dictionary<Guid, OverlayEntry> _latest = new();
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
            var roi = _settings.Rois.FirstOrDefault(x => x.Id == update.RoiId);
            var hold = roi?.OverlayHoldSeconds ?? 20;
            if (roi?.EventMode == true && hold > 0) hold = Math.Max(hold, 30);
            var expires = hold <= 0 ? DateTimeOffset.MaxValue : DateTimeOffset.Now.AddSeconds(Math.Clamp(hold, 1, 300));
            _latest[update.RoiId] = new OverlayEntry(update, expires);
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

        var expired = PurgeExpired();
        var changed = !_hasLastRect || rect.Left != _lastRect.Left || rect.Top != _lastRect.Top ||
                      rect.Right != _lastRect.Right || rect.Bottom != _lastRect.Bottom;
        _lastRect = rect;
        _hasLastRect = true;
        if (forceRender || changed || expired) Render();
    }

    private bool PurgeExpired()
    {
        if (_editMode) return false;
        var now = DateTimeOffset.Now;
        var expired = _latest.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray();
        foreach (var id in expired) _latest.Remove(id);
        return expired.Length > 0;
    }

    private void Render()
    {
        if (!IsLoaded || RootCanvas.ActualWidth <= 1 || RootCanvas.ActualHeight <= 1) return;
        RootCanvas.Children.Clear();

        if (_editMode)
        {
            var hint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 30, 30, 30)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 5, 9, 5),
                Child = new TextBlock
                {
                    Text = "오버레이 편집: 왼쪽 위 이동바=이동 · 오른쪽=가로 · 아래=세로 · 우하단=가로+세로 · 설정은 자동 저장",
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
            RoiTranslationUpdate update;
            if (_latest.TryGetValue(roi.Id, out var entry))
            {
                update = entry.Update;
            }
            else if (_editMode)
            {
                update = new RoiTranslationUpdate(
                    roi.Id, roi.Name, "OCR 원문 미리보기", "번역 결과 미리보기",
                    "RoiLingo", 1, 1, DateTimeOffset.Now, []);
            }
            else
            {
                continue;
            }

            AddOverlayForRoi(roi, update);
        }
    }

    private void AddOverlayForRoi(RoiDefinition roi, RoiTranslationUpdate update)
    {
        var x = roi.X * RootCanvas.ActualWidth;
        var y = roi.Y * RootCanvas.ActualHeight;
        var roiHeight = roi.Height * RootCanvas.ActualHeight;
        var fontSize = Math.Clamp(roi.OverlayFontSize, 10, 42);

        var baseWidth = Math.Max(140, roi.Width * RootCanvas.ActualWidth);
        var autoWidth = Math.Clamp(baseWidth * Math.Clamp(roi.OverlayWidthScale, 0.25, 5.0), 80, RootCanvas.ActualWidth);
        var w = roi.OverlayWidth > 0
            ? Math.Clamp(roi.OverlayWidth, 80, RootCanvas.ActualWidth)
            : autoWidth;

        var stack = new StackPanel();
        if (roi.OverlayShowSource)
        {
            stack.Children.Add(new TextBlock
            {
                Text = update.SourceText,
                Foreground = Brushes.LightGray,
                FontSize = Math.Max(9, fontSize * 0.65),
                TextWrapping = TextWrapping.Wrap
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = update.TargetText,
            Foreground = Brushes.White,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
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
            Width = w,
            ClipToBounds = true
        };

        border.Measure(new Size(w, double.PositiveInfinity));
        var naturalH = Math.Max(36, border.DesiredSize.Height);
        var autoHeight = Math.Clamp(naturalH * Math.Clamp(roi.OverlayHeightScale, 0.35, 5.0), 36, RootCanvas.ActualHeight);
        var h = roi.OverlayHeight > 0
            ? Math.Clamp(roi.OverlayHeight, 36, RootCanvas.ActualHeight)
            : autoHeight;
        border.Height = h;

        var below = y + roiHeight + 4;
        var automaticY = below + h <= RootCanvas.ActualHeight ? below : Math.Max(0, y - h - 4);
        var finalX = Math.Clamp(x + roi.OverlayOffsetX, 0, Math.Max(0, RootCanvas.ActualWidth - w));
        var finalY = Math.Clamp(automaticY + roi.OverlayOffsetY, 0, Math.Max(0, RootCanvas.ActualHeight - h));

        if (!_editMode)
        {
            Canvas.SetLeft(border, finalX);
            Canvas.SetTop(border, finalY);
            RootCanvas.Children.Add(border);
            return;
        }

        var editor = new Grid { Width = w, Height = h };
        editor.Children.Add(border);

        var moveThumb = new Thumb
        {
            Width = Math.Min(150, Math.Max(80, w * 0.45)),
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(220, 0, 120, 185)),
            Cursor = Cursors.SizeAll,
            ToolTip = "드래그해서 오버레이 이동"
        };
        editor.Children.Add(moveThumb);
        editor.Children.Add(new TextBlock
        {
            Text = $"↕ {roi.Name} 이동",
            Foreground = Brushes.White,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(7, 4, 0, 0),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        });

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

        Canvas.SetLeft(editor, finalX);
        Canvas.SetTop(editor, finalY);
        RootCanvas.Children.Add(editor);

        moveThumb.DragDelta += (_, e) =>
        {
            roi.OverlayOffsetX = Math.Clamp(roi.OverlayOffsetX + e.HorizontalChange, -2000, 2000);
            roi.OverlayOffsetY = Math.Clamp(roi.OverlayOffsetY + e.VerticalChange, -1600, 1600);
            var left = Math.Clamp(Canvas.GetLeft(editor) + e.HorizontalChange, 0, Math.Max(0, RootCanvas.ActualWidth - editor.Width));
            var top = Math.Clamp(Canvas.GetTop(editor) + e.VerticalChange, 0, Math.Max(0, RootCanvas.ActualHeight - editor.Height));
            Canvas.SetLeft(editor, left);
            Canvas.SetTop(editor, top);
        };
        moveThumb.DragCompleted += (_, _) => _persist?.Invoke();

        void ResizeTo(double newWidth, double newHeight)
        {
            newWidth = Math.Clamp(newWidth, 80, Math.Max(80, RootCanvas.ActualWidth - Canvas.GetLeft(editor)));
            newHeight = Math.Clamp(newHeight, 36, Math.Max(36, RootCanvas.ActualHeight - Canvas.GetTop(editor)));
            roi.OverlayWidth = newWidth;
            roi.OverlayHeight = newHeight;
            editor.Width = newWidth;
            editor.Height = newHeight;
            border.Width = newWidth;
            border.Height = newHeight;
        }

        Thumb ResizeThumb(double width, double height, HorizontalAlignment hAlign, VerticalAlignment vAlign, Cursor cursor, string tip)
        {
            var thumb = new Thumb
            {
                Width = width,
                Height = height,
                HorizontalAlignment = hAlign,
                VerticalAlignment = vAlign,
                Cursor = cursor,
                Background = new SolidColorBrush(Color.FromArgb(210, 0, 170, 255)),
                Opacity = 0.9,
                ToolTip = tip
            };
            editor.Children.Add(thumb);
            return thumb;
        }

        var rightGrip = ResizeThumb(10, double.NaN, HorizontalAlignment.Right, VerticalAlignment.Stretch, Cursors.SizeWE, "가로 크기만 조절");
        rightGrip.DragDelta += (_, e) => ResizeTo(editor.Width + e.HorizontalChange, editor.Height);
        rightGrip.DragCompleted += (_, _) => _persist?.Invoke();

        var bottomGrip = ResizeThumb(double.NaN, 10, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Cursors.SizeNS, "세로 크기만 조절");
        bottomGrip.DragDelta += (_, e) => ResizeTo(editor.Width, editor.Height + e.VerticalChange);
        bottomGrip.DragCompleted += (_, _) => _persist?.Invoke();

        var cornerGrip = ResizeThumb(20, 20, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE, "가로와 세로를 동시에 조절");
        cornerGrip.DragDelta += (_, e) => ResizeTo(editor.Width + e.HorizontalChange, editor.Height + e.VerticalChange);
        cornerGrip.DragCompleted += (_, _) => _persist?.Invoke();

        editor.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                roi.OverlayOpacity = Math.Clamp(roi.OverlayOpacity + Math.Sign(e.Delta) * 0.05, 0.10, 1.0);
            else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                ResizeTo(editor.Width, editor.Height + Math.Sign(e.Delta) * 16);
            else if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
                roi.OverlayFontSize = Math.Clamp(roi.OverlayFontSize + Math.Sign(e.Delta), 10, 42);
            else
                ResizeTo(editor.Width + Math.Sign(e.Delta) * 24, editor.Height);
            _persist?.Invoke();
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) Render();
            e.Handled = true;
        };
    }
}
