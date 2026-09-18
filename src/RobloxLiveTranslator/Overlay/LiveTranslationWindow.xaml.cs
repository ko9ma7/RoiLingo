using System.Windows;
using RobloxLiveTranslator.Models;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Overlay;

public partial class LiveTranslationWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _persistSettings;
    private readonly ObservableCollection<LiveTranslationItem> _items = [];
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer;
    private bool _initialized;

    public LiveTranslationWindow(AppSettings settings, Action persistSettings)
    {
        InitializeComponent();
        _settings = settings;
        _persistSettings = persistSettings;
        TranslationList.ItemsSource = _items;

        _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            CaptureBounds();
            _persistSettings();
        };

        Loaded += (_, _) => { CaptureExclusion.Apply(this); ApplySettings(); };
        LocationChanged += (_, _) => ScheduleSave();
        SizeChanged += (_, _) => ScheduleSave();
        Closed += (_, _) =>
        {
            _saveTimer.Stop();
            CaptureBounds();
            _persistSettings();
        };
    }

    public void AddTranslation(RoiTranslationUpdate update)
    {
        Dispatcher.Invoke(() =>
        {
            _items.Insert(0, new LiveTranslationItem
            {
                Timestamp = update.Timestamp,
                RoiName = update.RoiName,
                Provider = update.Provider,
                SourceText = update.SourceText,
                TargetText = update.TargetText,
                TargetFontSize = _settings.LiveWindowFontSize,
                SourceVisibility = _settings.LiveWindowShowSource ? Visibility.Visible : Visibility.Collapsed
            });

            while (_items.Count > Math.Clamp(_settings.LiveWindowMaxItems, 10, 200))
                _items.RemoveAt(_items.Count - 1);
        });
    }

    private void ApplySettings()
    {
        Width = Math.Max(MinWidth, _settings.LiveWindowWidth);
        Height = Math.Max(MinHeight, _settings.LiveWindowHeight);
        Topmost = _settings.LiveWindowTopmost;
        TopmostCheck.IsChecked = _settings.LiveWindowTopmost;
        ShowSourceCheck.IsChecked = _settings.LiveWindowShowSource;
        FontSizeSlider.Value = Math.Clamp(_settings.LiveWindowFontSize, 12, 32);

        var left = _settings.LiveWindowLeft;
        var top = _settings.LiveWindowTop;
        if (left.HasValue && top.HasValue && IsOnVirtualScreen(left.Value, top.Value, Width, Height))
        {
            Left = left.Value;
            Top = top.Value;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Bottom - Height - 24;
        }

        _initialized = true;
        RefreshItemPresentation();
    }

    private static bool IsOnVirtualScreen(double left, double top, double width, double height)
    {
        var right = left + width;
        var bottom = top + height;
        return right >= SystemParameters.VirtualScreenLeft &&
               bottom >= SystemParameters.VirtualScreenTop &&
               left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
               top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
    }

    private void ScheduleSave()
    {
        if (!_initialized) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void CaptureBounds()
    {
        if (!_initialized || WindowState != WindowState.Normal) return;
        _settings.LiveWindowLeft = Left;
        _settings.LiveWindowTop = Top;
        _settings.LiveWindowWidth = ActualWidth;
        _settings.LiveWindowHeight = ActualHeight;
        _settings.LiveWindowTopmost = TopmostCheck.IsChecked == true;
        _settings.LiveWindowShowSource = ShowSourceCheck.IsChecked == true;
        _settings.LiveWindowFontSize = FontSizeSlider.Value;
    }

    private void TopmostCheck_Click(object sender, RoutedEventArgs e)
    {
        Topmost = TopmostCheck.IsChecked == true;
        _settings.LiveWindowTopmost = Topmost;
        ScheduleSave();
    }

    private void ShowSourceCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.LiveWindowShowSource = ShowSourceCheck.IsChecked == true;
        RefreshItemPresentation();
        ScheduleSave();
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _settings.LiveWindowFontSize = e.NewValue;
        RefreshItemPresentation();
        ScheduleSave();
    }

    private void RefreshItemPresentation()
    {
        foreach (var item in _items)
        {
            item.TargetFontSize = _settings.LiveWindowFontSize;
            item.SourceVisibility = _settings.LiveWindowShowSource ? Visibility.Visible : Visibility.Collapsed;
        }
        TranslationList.Items.Refresh();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e) => _items.Clear();

    private sealed class LiveTranslationItem
    {
        public DateTimeOffset Timestamp { get; init; }
        public string RoiName { get; init; } = string.Empty;
        public string Provider { get; init; } = string.Empty;
        public string SourceText { get; init; } = string.Empty;
        public string TargetText { get; init; } = string.Empty;
        public double TargetFontSize { get; set; }
        public Visibility SourceVisibility { get; set; }
    }
}
