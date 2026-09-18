using System.Windows;
using System.Windows.Controls;
using RobloxLiveTranslator.Models;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Overlay;

public partial class OverlaySettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _persist;
    private readonly Action _refreshOverlay;
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer;
    private bool _loading;

    public OverlaySettingsWindow(AppSettings settings, Action persist, Action refreshOverlay)
    {
        InitializeComponent();
        _settings = settings;
        _persist = persist;
        _refreshOverlay = refreshOverlay;

        RoiCombo.ItemsSource = _settings.Rois;
        _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _persist();
        };

        Loaded += (_, _) =>
        {
            CaptureExclusion.Apply(this);
            if (RoiCombo.Items.Count > 0) RoiCombo.SelectedIndex = 0;
            else SetControlsEnabled(false);
        };
        Closed += (_, _) =>
        {
            _saveTimer.Stop();
            _persist();
        };
    }

    private RoiDefinition? SelectedRoi => RoiCombo.SelectedItem as RoiDefinition;

    private void RoiCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadSelected();

    private void LoadSelected()
    {
        _loading = true;
        try
        {
            var roi = SelectedRoi;
            SetControlsEnabled(roi is not null);
            if (roi is null) return;

            OffsetXSlider.Value = Math.Clamp(roi.OverlayOffsetX, -1000, 1000);
            OffsetYSlider.Value = Math.Clamp(roi.OverlayOffsetY, -800, 800);
            WidthSlider.Value = Math.Clamp(roi.OverlayWidth, 0, 1600);
            HeightSlider.Value = Math.Clamp(roi.OverlayHeight, 0, 900);
            HoldSecondsSlider.Value = Math.Clamp(roi.OverlayHoldSeconds, 0, 120);
            OpacitySlider.Value = Math.Clamp(roi.OverlayOpacity, 0.10, 1.0);
            FontSizeSlider.Value = Math.Clamp(roi.OverlayFontSize, 10, 42);
            ShowSourceCheck.IsChecked = roi.OverlayShowSource;
            ShowMetaCheck.IsChecked = roi.OverlayShowMeta;
            UpdateValueLabels();
        }
        finally
        {
            _loading = false;
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        OffsetXSlider.IsEnabled = enabled;
        OffsetYSlider.IsEnabled = enabled;
        WidthSlider.IsEnabled = enabled;
        HeightSlider.IsEnabled = enabled;
        HoldSecondsSlider.IsEnabled = enabled;
        OpacitySlider.IsEnabled = enabled;
        FontSizeSlider.IsEnabled = enabled;
        ShowSourceCheck.IsEnabled = enabled;
        ShowMetaCheck.IsEnabled = enabled;
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyControls();
    private void CheckBox_Click(object sender, RoutedEventArgs e) => ApplyControls();

    private void ApplyControls()
    {
        if (_loading || SelectedRoi is not { } roi) return;

        roi.OverlayOffsetX = OffsetXSlider.Value;
        roi.OverlayOffsetY = OffsetYSlider.Value;
        roi.OverlayWidth = WidthSlider.Value < 50 ? 0 : WidthSlider.Value;
        roi.OverlayHeight = HeightSlider.Value < 25 ? 0 : HeightSlider.Value;
        roi.OverlayHoldSeconds = HoldSecondsSlider.Value;
        roi.OverlayOpacity = OpacitySlider.Value;
        roi.OverlayFontSize = FontSizeSlider.Value;
        roi.OverlayShowSource = ShowSourceCheck.IsChecked == true;
        roi.OverlayShowMeta = ShowMetaCheck.IsChecked == true;

        UpdateValueLabels();
        _refreshOverlay();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void UpdateValueLabels()
    {
        OffsetXText.Text = $"{OffsetXSlider.Value:0}px";
        OffsetYText.Text = $"{OffsetYSlider.Value:0}px";
        WidthText.Text = WidthSlider.Value < 50 ? "자동" : $"{WidthSlider.Value:0}px";
        HeightText.Text = HeightSlider.Value < 25 ? "자동" : $"{HeightSlider.Value:0}px";
        HoldSecondsText.Text = HoldSecondsSlider.Value <= 0 ? "계속" : $"{HoldSecondsSlider.Value:0}초";
        OpacityText.Text = $"{OpacitySlider.Value:P0}";
        FontSizeText.Text = $"{FontSizeSlider.Value:0}";
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRoi is not { } roi) return;
        roi.OverlayOffsetX = 0;
        roi.OverlayOffsetY = 0;
        roi.OverlayWidthScale = 1;
        roi.OverlayHeightScale = 1;
        roi.OverlayWidth = 0;
        roi.OverlayHeight = 0;
        roi.OverlayHoldSeconds = 20;
        roi.OverlayOpacity = 0.82;
        roi.OverlayFontSize = 17;
        roi.OverlayShowSource = true;
        roi.OverlayShowMeta = true;
        LoadSelected();
        _refreshOverlay();
        _persist();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
