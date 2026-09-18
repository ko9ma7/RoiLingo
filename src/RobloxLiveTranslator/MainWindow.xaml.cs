using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Web.WebView2.Wpf;
using RobloxLiveTranslator.Models;
using RobloxLiveTranslator.Monitoring;
using RobloxLiveTranslator.Native;
using RobloxLiveTranslator.Overlay;
using RobloxLiveTranslator.Services;
using RobloxLiveTranslator.Translation;
using RobloxLiveTranslator.Translation.Api;
using RobloxLiveTranslator.Translation.Web;

namespace RobloxLiveTranslator;

public partial class MainWindow : Window
{
    private const double CompactWindowWidth = 860;
    private const double CompactWindowHeight = 190;
    private const double AdvancedWindowMinWidth = 1120;
    private const double AdvancedWindowMinHeight = 760;
    private readonly AppSettings _settings;
    private readonly HistoryStore _historyStore;
    private readonly WebTranslatorRuntime _webRuntime = new();
    private readonly ApiUsageStore _apiUsage;
    private readonly ObservableCollection<TranslationHistoryRecord> _history = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastEventSound = new();
    private readonly string _sessionId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
    private ApiSecrets _secrets;

    private IntPtr _targetHwnd;
    private MonitorEngine? _monitor;
    private OverlayWindow? _overlay;
    private LiveTranslationWindow? _liveWindow;
    private OverlaySettingsWindow? _overlaySettingsWindow;
    private TranslationCache? _translationCache;
    private TesseractOcrService? _ocr;
    private bool _closing;
    private bool _webInitialized;
    private bool _logPersistenceWarningShown;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.LoadSettings();
        _historyStore = new HistoryStore(SettingsStore.AppDirectory);
        _apiUsage = new ApiUsageStore(SettingsStore.AppDirectory);
        _secrets = SecretStore.Load();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CaptureExclusion.Apply(this);
        LoadUi();
        HistoryGrid.ItemsSource = _history;
        CollectionViewSource.GetDefaultView(_history).Filter = HistoryFilter;
        await LoadHistoryAsync();
        RefreshApiUsageText();
        StatusText.Text = UiText.Get(_settings.UiLanguage, "ready");

        // Keep startup lightweight. WebView2 is initialized lazily when translation starts
        // or when the user explicitly opens/tests the web translator settings.
        if (_settings.WebProviders.Papago || _settings.WebProviders.Google || _settings.WebProviders.DeepL)
            AddLog("WEB     웹 번역기는 실시간 번역 시작 시 준비됩니다.");
    }

    private void LoadUi()
    {
        SelectComboByTag(UiLanguageCombo, _settings.UiLanguage);
        OcrLanguagesBox.Text = _settings.OcrLanguages;
        SelectComboByTag(OcrModeCombo, _settings.OcrMode);
        SelectComboByTag(CaptureModeCombo, _settings.CaptureMode);
        SelectComboByTag(SourceLanguageCombo, _settings.SourceLanguage);
        SelectComboByTag(TargetLanguageCombo, _settings.TargetLanguage);
        OcrLanguageFollowsSourceCheck.IsChecked = _settings.OcrLanguageFollowsSource;
        SmartMixedTextCheck.IsChecked = _settings.SmartMixedText;
        RefreshOcrLanguageButton();
        PollIntervalBox.Text = _settings.PollIntervalMs.ToString();
        ChangeThresholdBox.Text = _settings.ChangeThreshold.ToString("0.000");
        AutoSaveHistoryCheck.IsChecked = _settings.AutoSaveHistory;
        LiveWindowEnabledCheck.IsChecked = _settings.ShowLiveWindow;

        PapagoWebEnabled.IsChecked = _settings.WebProviders.Papago;
        GoogleWebEnabled.IsChecked = _settings.WebProviders.Google;
        DeepLWebEnabled.IsChecked = _settings.WebProviders.DeepL;
        ClipboardFallbackCheck.IsChecked = _settings.AllowClipboardFallback;
        WebTimeoutBox.Text = _settings.WebTranslationTimeoutMs.ToString();

        SelectComboByTag(TranslationStrategyCombo, _settings.TranslationStrategy);
        SelectComboByContent(PreferredProviderCombo, _settings.PreferredProvider);
        ApiTimeoutBox.Text = _settings.ApiTimeoutMs.ToString();

        PapagoApiEnabled.IsChecked = _settings.ApiProviders.Papago.Enabled;
        GoogleApiEnabled.IsChecked = _settings.ApiProviders.Google.Enabled;
        DeepLApiEnabled.IsChecked = _settings.ApiProviders.DeepL.Enabled;
        LibreApiEnabled.IsChecked = _settings.ApiProviders.LibreTranslate.Enabled;
        DeepLFreeEndpointCheck.IsChecked = _settings.ApiProviders.DeepLUseFreeEndpoint;
        LibreEndpointBox.Text = _settings.ApiProviders.LibreTranslateEndpoint;

        PapagoDailyLimitBox.Text = _settings.ApiProviders.Papago.DailyRequestLimit.ToString();
        PapagoMonthlyLimitBox.Text = _settings.ApiProviders.Papago.MonthlyCharacterLimit.ToString();
        GoogleDailyLimitBox.Text = _settings.ApiProviders.Google.DailyRequestLimit.ToString();
        GoogleMonthlyLimitBox.Text = _settings.ApiProviders.Google.MonthlyCharacterLimit.ToString();
        DeepLDailyLimitBox.Text = _settings.ApiProviders.DeepL.DailyRequestLimit.ToString();
        DeepLMonthlyLimitBox.Text = _settings.ApiProviders.DeepL.MonthlyCharacterLimit.ToString();
        LibreDailyLimitBox.Text = _settings.ApiProviders.LibreTranslate.DailyRequestLimit.ToString();
        LibreMonthlyLimitBox.Text = _settings.ApiProviders.LibreTranslate.MonthlyCharacterLimit.ToString();

        PapagoClientIdBox.Password = _secrets.PapagoClientId;
        PapagoClientSecretBox.Password = _secrets.PapagoClientSecret;
        GoogleApiKeyBox.Password = _secrets.GoogleApiKey;
        DeepLApiKeyBox.Password = _secrets.DeepLApiKey;
        LibreApiKeyBox.Password = _secrets.LibreTranslateApiKey;
        RefreshRoiGrid();
        ApplyUiLanguage();
    }

    private void UiLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.UiLanguage = UiText.NormalizeLocale((UiLanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString());
        ApplyUiLanguage();
        SaveAll();
    }

    private void ApplyUiLanguage()
    {
        var locale = UiText.NormalizeLocale(_settings.UiLanguage);
        PickWindowButton.Content = UiText.Get(locale, "target");
        EditRoiButton.Content = UiText.Get(locale, "roi");
        StartButton.Content = UiText.Get(locale, "start");
        StopButton.Content = UiText.Get(locale, "stop");
        QuickOverlayEditButton.Content = UiText.Get(locale, "overlay");
        LiveWindowButton.Content = UiText.Get(locale, "live");
        AdvancedSettingsButton.Content = UiText.Get(locale, "settings");
        SourceLabel.Text = UiText.Get(locale, "source");
        TargetLabel.Text = UiText.Get(locale, "translate");
        ModeLabel.Text = UiText.Get(locale, "mode");
        AdvancedTitleText.Text = UiText.Get(locale, "advanced");
        AdvancedSaveButton.Content = UiText.Get(locale, "save");
        AdvancedCloseButton.Content = UiText.Get(locale, "close");
        GeneralTab.Header = UiText.Get(locale, "tab.general");
        WebTab.Header = UiText.Get(locale, "tab.web");
        ApiTab.Header = UiText.Get(locale, "tab.api");
        HistoryTab.Header = UiText.Get(locale, "tab.history");

        if (_monitor is null) StatusText.Text = UiText.Get(locale, "ready");
        LocalizeLanguageCombo(SourceLanguageCombo, locale);
        LocalizeLanguageCombo(TargetLanguageCombo, locale);
        var strategyKeys = new[] { "strategy.web", "strategy.hybrid", "strategy.api", "strategy.max" };
        for (var i = 0; i < TranslationStrategyCombo.Items.Count && i < strategyKeys.Length; i++)
            if (TranslationStrategyCombo.Items[i] is ComboBoxItem item) item.Content = UiText.Get(locale, strategyKeys[i]);
        LocalizeTree(this, locale);
        // Language/strategy items have semantic Tags, so re-apply them after literal traversal.
        LocalizeLanguageCombo(SourceLanguageCombo, locale);
        LocalizeLanguageCombo(TargetLanguageCombo, locale);
        for (var i = 0; i < TranslationStrategyCombo.Items.Count && i < strategyKeys.Length; i++)
            if (TranslationStrategyCombo.Items[i] is ComboBoxItem item) item.Content = UiText.Get(locale, strategyKeys[i]);
    }

    private static void LocalizeTree(DependencyObject root, string locale)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject obj) continue;
            if (obj is TextBlock tb) tb.Text = UiText.TranslateLiteral(locale, tb.Text);
            if (obj is HeaderedContentControl hc && hc.Header is string hs) hc.Header = UiText.TranslateLiteral(locale, hs);
            if (obj is ContentControl cc && cc.Content is string cs) cc.Content = UiText.TranslateLiteral(locale, cs);
            LocalizeTree(obj, locale);
        }
    }

    private static void LocalizeLanguageCombo(ComboBox combo, string locale)
    {
        foreach (var entry in combo.Items.OfType<ComboBoxItem>())
        {
            var tag = entry.Tag?.ToString();
            if (!string.IsNullOrWhiteSpace(tag)) entry.Content = UiText.Get(locale, tag!);
        }
    }

    private void SyncUiToSettings()
    {
        RoiGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RoiGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _settings.UiLanguage = UiText.NormalizeLocale((UiLanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString());
        _settings.SourceLanguage = TranslationLanguages.Normalize((SourceLanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString());
        _settings.OcrLanguageFollowsSource = OcrLanguageFollowsSourceCheck.IsChecked == true;
        _settings.OcrLanguages = string.IsNullOrWhiteSpace(OcrLanguagesBox.Text) ? "eng+kor" : OcrLanguagesBox.Text.Trim();
        if (_settings.OcrLanguageFollowsSource && _settings.SourceLanguage != "auto")
        {
            _settings.OcrLanguages = TranslationLanguages.ToTesseract(_settings.SourceLanguage);
            OcrLanguagesBox.Text = _settings.OcrLanguages;
        }
        _settings.OcrMode = (OcrModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Balanced";
        _settings.CaptureMode = (CaptureModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "BackgroundFirst";
        _settings.TargetLanguage = TranslationLanguages.Normalize((TargetLanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), false);
        _settings.SmartMixedText = SmartMixedTextCheck.IsChecked == true;
        RefreshOcrLanguageButton();
        if (int.TryParse(PollIntervalBox.Text, out var poll)) _settings.PollIntervalMs = Math.Clamp(poll, 100, 2000);
        if (double.TryParse(ChangeThresholdBox.Text, out var threshold)) _settings.ChangeThreshold = Math.Clamp(threshold, 0.005, 0.25);
        if (int.TryParse(WebTimeoutBox.Text, out var webTimeout)) _settings.WebTranslationTimeoutMs = Math.Clamp(webTimeout, 3000, 25000);
        if (int.TryParse(ApiTimeoutBox.Text, out var apiTimeout)) _settings.ApiTimeoutMs = Math.Clamp(apiTimeout, 2000, 30000);

        _settings.AutoSaveHistory = AutoSaveHistoryCheck.IsChecked == true;
        _settings.ShowLiveWindow = LiveWindowEnabledCheck.IsChecked == true;
        _settings.WebProviders.Papago = PapagoWebEnabled.IsChecked == true;
        _settings.WebProviders.Google = GoogleWebEnabled.IsChecked == true;
        _settings.WebProviders.DeepL = DeepLWebEnabled.IsChecked == true;
        _settings.AllowClipboardFallback = ClipboardFallbackCheck.IsChecked == true;
        _settings.TranslationStrategy = (TranslationStrategyCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "WebOnly";
        _settings.PreferredProvider = (PreferredProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto";

        SyncBudget(_settings.ApiProviders.Papago, PapagoApiEnabled, PapagoDailyLimitBox, PapagoMonthlyLimitBox);
        SyncBudget(_settings.ApiProviders.Google, GoogleApiEnabled, GoogleDailyLimitBox, GoogleMonthlyLimitBox);
        SyncBudget(_settings.ApiProviders.DeepL, DeepLApiEnabled, DeepLDailyLimitBox, DeepLMonthlyLimitBox);
        SyncBudget(_settings.ApiProviders.LibreTranslate, LibreApiEnabled, LibreDailyLimitBox, LibreMonthlyLimitBox);
        _settings.ApiProviders.DeepLUseFreeEndpoint = DeepLFreeEndpointCheck.IsChecked == true;
        _settings.ApiProviders.LibreTranslateEndpoint = string.IsNullOrWhiteSpace(LibreEndpointBox.Text)
            ? "http://localhost:5000"
            : LibreEndpointBox.Text.Trim();
    }

    private static void SyncBudget(ProviderBudgetSettings target, CheckBox enabled, TextBox daily, TextBox monthly)
    {
        target.Enabled = enabled.IsChecked == true;
        if (int.TryParse(daily.Text, out var dailyValue)) target.DailyRequestLimit = Math.Max(0, dailyValue);
        if (int.TryParse(monthly.Text, out var monthlyValue)) target.MonthlyCharacterLimit = Math.Max(0, monthlyValue);
    }

    private void SaveSecretsFromUi()
    {
        _secrets = new ApiSecrets
        {
            PapagoClientId = PapagoClientIdBox.Password.Trim(),
            PapagoClientSecret = PapagoClientSecretBox.Password.Trim(),
            GoogleApiKey = GoogleApiKeyBox.Password.Trim(),
            DeepLApiKey = DeepLApiKeyBox.Password.Trim(),
            LibreTranslateApiKey = LibreApiKeyBox.Password.Trim()
        };
        SecretStore.Save(_secrets);
    }

    private bool StrategyUsesWeb() => !_settings.TranslationStrategy.Equals("ApiOnly", StringComparison.OrdinalIgnoreCase);

    private async Task EnsureWebViewReadyAsync(WebView2 view)
    {
        if (view.CoreWebView2 is not null) return;
        var previousMainTab = MainTabs.SelectedIndex;
        var previousSiteTab = TranslatorSiteTabs.SelectedIndex;
        try
        {
            MainTabs.SelectedIndex = 1;
            TranslatorSiteTabs.SelectedIndex = view == PapagoWebView ? 0 : view == GoogleWebView ? 1 : 2;
            UpdateLayout();
            await _webRuntime.InitializeAsync([view]);
            _webInitialized = PapagoWebView.CoreWebView2 is not null || GoogleWebView.CoreWebView2 is not null || DeepLWebView.CoreWebView2 is not null;
        }
        finally
        {
            TranslatorSiteTabs.SelectedIndex = previousSiteTab < 0 ? 0 : previousSiteTab;
            MainTabs.SelectedIndex = previousMainTab < 0 ? 0 : previousMainTab;
        }
    }

    private async Task WarmUpAfterStartAsync(ModelManager models)
    {
        try
        {
            await Task.Delay(700);
            var progress = new Progress<string>(message => Dispatcher.Invoke(() => AddLog("WARMUP  " + message)));
            await models.EnsureLanguagesAsync(BuildRequiredOcrLanguagesForSession(), progress);
            if (StrategyUsesWeb())
            {
                if (_settings.WebProviders.Papago) await Dispatcher.InvokeAsync(() => EnsureWebViewReadyAsync(PapagoWebView)).Task.Unwrap();
                if (_settings.WebProviders.Google) await Dispatcher.InvokeAsync(() => EnsureWebViewReadyAsync(GoogleWebView)).Task.Unwrap();
                if (_settings.WebProviders.DeepL) await Dispatcher.InvokeAsync(() => EnsureWebViewReadyAsync(DeepLWebView)).Task.Unwrap();
            }
            Dispatcher.Invoke(() => AddLog("WARMUP  백그라운드 준비 완료"));
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AddLog("WARMUP  " + ex.Message));
        }
    }

    private async Task InitializeWebTranslatorsAsync(bool navigateHome)
    {
        var previousMainTab = MainTabs.SelectedIndex;
        var previousSiteTab = TranslatorSiteTabs.SelectedIndex;
        try
        {
            MainTabs.SelectedIndex = 1;
            UpdateLayout();
            var views = new WebView2[] { PapagoWebView, GoogleWebView, DeepLWebView };
            for (var i = 0; i < views.Length; i++)
            {
                TranslatorSiteTabs.SelectedIndex = i;
                UpdateLayout();
                await _webRuntime.InitializeAsync([views[i]]);
            }
            _webInitialized = true;
            if (navigateHome)
            {
                PapagoWebView.Source = new Uri(WebTranslationScripts.PapagoHome);
                GoogleWebView.Source = new Uri(WebTranslationScripts.GoogleHome);
                DeepLWebView.Source = new Uri(WebTranslationScripts.DeepLHome);
            }
        }
        finally
        {
            TranslatorSiteTabs.SelectedIndex = previousSiteTab < 0 ? 0 : previousSiteTab;
            MainTabs.SelectedIndex = previousMainTab < 0 ? 0 : previousMainTab;
        }
    }

    private async void InitializeWebTranslatorsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "번역 웹 사이트를 준비하는 중...";
            await InitializeWebTranslatorsAsync(navigateHome: true);
            StatusText.Text = "번역 웹 사이트 준비 완료";
            AddLog("WEB     번역 사이트 새로고침 완료");
        }
        catch (Exception ex)
        {
            AddLog("WEB     " + ex.Message);
            MessageBox.Show(ex.Message, "WebView2 초기화 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PickWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_monitor is not null) return;
        StatusText.Text = "이 창을 숨깁니다. 번역할 Roblox 창을 한 번 클릭하세요...";
        Hide();
        await Task.Delay(350);
        var picker = new TargetWindowPicker();
        var hwnd = await picker.PickAsync(TimeSpan.FromSeconds(20));
        Show();
        Activate();

        if (hwnd == IntPtr.Zero)
        {
            StatusText.Text = "대상 창 선택이 취소되었거나 시간 초과되었습니다.";
            return;
        }

        _targetHwnd = hwnd;
        TargetWindowText.Text = $"대상 창: {NativeMethods.GetTitle(hwnd)} (0x{hwnd.ToInt64():X})";
        StatusText.Text = "대상 창이 선택되었습니다.";
        AddLog($"TARGET  {TargetWindowText.Text}");
    }

    private void EditRoiButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateTarget()) return;
        if (_monitor is not null)
        {
            MessageBox.Show("ROI를 수정하려면 먼저 번역을 중지하세요.");
            return;
        }

        Hide();
        var editor = new RoiEditorWindow(_targetHwnd, _settings.Rois);
        var result = editor.ShowDialog();
        Show();
        Activate();
        if (result == true)
        {
            _settings.Rois = editor.Result.Select(x => x.Clone()).ToList();
            RefreshRoiGrid();
            SaveAll();
            AddLog($"ROI     {_settings.Rois.Count}개 저장");
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateTarget()) return;
        if (_settings.Rois.Count == 0)
        {
            MessageBox.Show("ROI를 하나 이상 만들어 주세요.");
            return;
        }

        try
        {
            SyncUiToSettings();
            SaveSecretsFromUi();
            SaveAll();
            SetRunningUi(true);
            if (AdvancedPanel.Visibility == Visibility.Visible) CloseAdvancedSettings();

            // Keep Start responsive: WebView2 and OCR models are initialized lazily on first use.
            // Optional warm-up begins only after monitoring is already running.
            var providers = CreateTranslationProviders().Where(x => x.IsConfigured).ToArray();
            if (providers.Length == 0)
                throw new InvalidOperationException("활성화되고 설정이 완료된 번역 Provider가 없습니다. Web 또는 API/로컬 설정을 확인하세요.");

            var models = new ModelManager();
            _ocr = new TesseractOcrService(models, _settings.OcrMode);
            _translationCache = new TranslationCache(SettingsStore.AppDirectory, "translation-cache-hybrid-v9.json");
            var translator = new MultiTranslator(providers, _settings.PreferredProvider, _settings.TranslationStrategy, _settings.ProviderWindowMs, _translationCache);
            translator.Diagnostic += message => Dispatcher.Invoke(() => AddLog("TRANS   " + message));

            _overlay = new OverlayWindow(_targetHwnd, _settings, SaveAll);
            _overlay.Show();
            EnsureLiveWindow(showEvenWhenDisabled: false);

            _monitor = new MonitorEngine(_targetHwnd, _settings, new WindowCaptureService(_settings.CaptureMode), _ocr, translator);
            _monitor.Status += msg => Dispatcher.Invoke(() => { StatusText.Text = msg; AddLog("STATUS  " + msg); });
            _monitor.TranslationPending += pending => Dispatcher.Invoke(() => OnTranslationPending(pending));
            _monitor.TranslationCleared += roiId => Dispatcher.Invoke(() => OnTranslationCleared(roiId));
            _monitor.TranslationUpdated += update => Dispatcher.Invoke(() => OnTranslation(update));
            _monitor.Start();
            if (_settings.BackgroundWarmup) _ = WarmUpAfterStartAsync(models);
            StatusText.Text = "실시간 번역 실행 중";
            AddLog($"START   ROI 감시 + {_settings.TranslationStrategy} / {TranslationLanguages.DisplayName(_settings.SourceLanguage)} → {TranslationLanguages.DisplayName(_settings.TargetLanguage)} / OCR={_settings.OcrLanguages} / 혼합처리={(_settings.SmartMixedText ? "ON" : "OFF")} / Provider={string.Join(", ", providers.Select(x => x.Name))}");
        }
        catch (Exception ex)
        {
            AddLog("ERROR   " + ex.Message);
            MessageBox.Show(ex.Message, "시작 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            await StopInternalAsync();
        }
    }

    private string BuildRequiredOcrLanguagesForSession()
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultSource = TranslationLanguages.Normalize(_settings.SourceLanguage);
        var defaultOcr = _settings.OcrLanguageFollowsSource && defaultSource != "auto"
            ? TranslationLanguages.ToTesseract(defaultSource)
            : _settings.OcrLanguages;
        foreach (var lang in ModelManager.ParseLanguages(defaultOcr)) languages.Add(lang);
        foreach (var roi in _settings.Rois)
        {
            if (!string.IsNullOrWhiteSpace(roi.OcrLanguagesOverride))
            {
                foreach (var lang in ModelManager.ParseLanguages(roi.OcrLanguagesOverride!)) languages.Add(lang);
                continue;
            }
            if (_settings.OcrLanguageFollowsSource && !string.IsNullOrWhiteSpace(roi.SourceLanguageOverride))
            {
                var roiSource = TranslationLanguages.Normalize(roi.SourceLanguageOverride);
                if (roiSource != "auto") languages.Add(TranslationLanguages.ToTesseract(roiSource));
            }
        }

        // Web extraction has a rendered-screen OCR fallback. Download its target-language model
        // once at startup so a visible translation can still be read if the site's DOM changes.
        if (StrategyUsesWeb())
        {
            languages.Add(WebVisualOcrFallback.MapTesseractLanguage(_settings.TargetLanguage));
            foreach (var roi in _settings.Rois)
            {
                if (!string.IsNullOrWhiteSpace(roi.TargetLanguageOverride))
                    languages.Add(WebVisualOcrFallback.MapTesseractLanguage(roi.TargetLanguageOverride!));
            }
        }

        return string.Join('+', languages);
    }

    private IEnumerable<ITranslationProvider> CreateTranslationProviders()
    {
        foreach (var provider in CreateWebProviders()) yield return provider;
        foreach (var provider in CreateApiProviders()) yield return provider;
    }

    private IEnumerable<ITranslationProvider> CreateWebProviders()
    {
        if (_settings.WebProviders.Papago)
            yield return new WebViewTranslationProvider(
                "Papago Web", PapagoWebView, WebTranslationScripts.PapagoUrl,
                WebTranslationScripts.PapagoResult, true, _settings.WebTranslationTimeoutMs,
                _settings.AllowClipboardFallback, message => AddLog("WEBREAD " + message), () => EnsureWebViewReadyAsync(PapagoWebView));

        if (_settings.WebProviders.Google)
            yield return new WebViewTranslationProvider(
                "Google Web", GoogleWebView, WebTranslationScripts.GoogleUrl,
                WebTranslationScripts.GoogleResult, true, _settings.WebTranslationTimeoutMs,
                _settings.AllowClipboardFallback, message => AddLog("WEBREAD " + message), () => EnsureWebViewReadyAsync(GoogleWebView));

        if (_settings.WebProviders.DeepL)
            yield return new WebViewTranslationProvider(
                "DeepL Web", DeepLWebView, WebTranslationScripts.DeepLUrl,
                WebTranslationScripts.DeepLResult, true, _settings.WebTranslationTimeoutMs,
                _settings.AllowClipboardFallback, message => AddLog("WEBREAD " + message), () => EnsureWebViewReadyAsync(DeepLWebView));
    }

    private IEnumerable<ITranslationProvider> CreateApiProviders()
    {
        ITranslationProvider Wrap(ITranslationProvider provider, ProviderBudgetSettings budget) =>
            new BudgetedTranslationProvider(provider, budget, _apiUsage, message => AddLog("QUOTA   " + message));

        if (_settings.ApiProviders.Papago.Enabled)
            yield return Wrap(new PapagoApiTranslationProvider(_secrets.PapagoClientId, _secrets.PapagoClientSecret, _settings.ApiTimeoutMs), _settings.ApiProviders.Papago);
        if (_settings.ApiProviders.Google.Enabled)
            yield return Wrap(new GoogleApiTranslationProvider(_secrets.GoogleApiKey, _settings.ApiTimeoutMs), _settings.ApiProviders.Google);
        if (_settings.ApiProviders.DeepL.Enabled)
            yield return Wrap(new DeepLApiTranslationProvider(_secrets.DeepLApiKey, _settings.ApiProviders.DeepLUseFreeEndpoint, _settings.ApiTimeoutMs), _settings.ApiProviders.DeepL);
        if (_settings.ApiProviders.LibreTranslate.Enabled)
            yield return Wrap(new LibreTranslateProvider(_settings.ApiProviders.LibreTranslateEndpoint, _secrets.LibreTranslateApiKey, _settings.ApiTimeoutMs), _settings.ApiProviders.LibreTranslate);
    }

    private async void TestWebTranslatorsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SyncUiToSettings();
            if (!_webInitialized) await InitializeWebTranslatorsAsync(navigateHome: false);

            var providers = CreateWebProviders().ToArray();
            if (providers.Length == 0)
            {
                MessageBox.Show("Papago / Google / DeepL 중 하나 이상을 활성화하세요.");
                return;
            }

            StatusText.Text = "번역 웹 결과 읽기 테스트 중...";
            var target = _settings.TargetLanguage;
            var visualOcrLanguage = WebVisualOcrFallback.MapTesseractLanguage(target);
            await new ModelManager().EnsureLanguagesAsync(visualOcrLanguage);
            var testText = GetTranslationTestText(_settings.SourceLanguage);
            var lines = new List<string>();
            foreach (var provider in providers)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(_settings.WebTranslationTimeoutMs + 2500));
                    var sw = Stopwatch.StartNew();
                    var translated = await provider.TranslateAsync(testText, _settings.SourceLanguage, target, timeout.Token);
                    sw.Stop();
                    lines.Add($"{provider.Name}: {translated} ({sw.ElapsedMilliseconds}ms)");
                }
                catch (Exception ex)
                {
                    lines.Add($"{provider.Name}: 실패 - {ex.Message}");
                }
            }

            foreach (var line in lines) AddLog("WEBTEST " + line);
            StatusText.Text = "번역 웹 결과 읽기 테스트 완료";
            MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, lines),
                "번역 결과 읽기 테스트", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AddLog("WEBTEST 실패: " + ex.Message);
            MessageBox.Show(ex.Message, "번역 테스트 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string GetTranslationTestText(string sourceLanguage) => TranslationLanguages.Normalize(sourceLanguage) switch
    {
        "ko" => "안녕하세요. 이것은 RoiLingo 번역 테스트입니다.",
        "ja" => "こんにちは。これはRoiLingoの翻訳テストです。",
        "zh-CN" or "zh-TW" => "你好，这是 RoiLingo 翻译测试。",
        "es" => "Hola. Esta es una prueba de traducción de RoiLingo.",
        "fr" => "Bonjour. Ceci est un test de traduction RoiLingo.",
        "de" => "Hallo. Dies ist ein RoiLingo-Übersetzungstest.",
        "ru" => "Здравствуйте. Это тест перевода RoiLingo.",
        _ => "Hello. This is a RoiLingo translation test."
    };

    private void SaveApiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SyncUiToSettings();
            SaveSecretsFromUi();
            SaveAll();
            RefreshApiUsageText();
            StatusText.Text = "API/로컬 설정과 비밀키를 저장했습니다.";
            AddLog("SETTINGS API/로컬 설정 저장 완료 (비밀키 DPAPI 암호화)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "API 설정 저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TestApiProvidersButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SyncUiToSettings();
            SaveSecretsFromUi();
            SaveAll();
            var providers = CreateApiProviders().Where(x => x.IsConfigured).ToArray();
            if (providers.Length == 0)
            {
                MessageBox.Show("사용할 API/로컬 Provider를 활성화하고 필요한 키 또는 LibreTranslate Endpoint를 입력하세요.");
                return;
            }

            var testText = GetTranslationTestText(_settings.SourceLanguage);
            var lines = new List<string>();
            StatusText.Text = "API/로컬 연결 테스트 중...";
            foreach (var provider in providers)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(_settings.ApiTimeoutMs + 1500));
                    var sw = Stopwatch.StartNew();
                    var result = await provider.TranslateAsync(testText, _settings.SourceLanguage, _settings.TargetLanguage, timeout.Token);
                    sw.Stop();
                    lines.Add($"{provider.Name}: {result} ({sw.ElapsedMilliseconds}ms)");
                }
                catch (Exception ex)
                {
                    lines.Add($"{provider.Name}: 실패 - {ex.Message}");
                }
            }

            if (_settings.ApiProviders.DeepL.Enabled && !string.IsNullOrWhiteSpace(_secrets.DeepLApiKey))
            {
                try
                {
                    var deepl = new DeepLApiTranslationProvider(_secrets.DeepLApiKey, _settings.ApiProviders.DeepLUseFreeEndpoint, _settings.ApiTimeoutMs);
                    var usage = await deepl.GetUsageAsync(CancellationToken.None);
                    if (usage is not null) lines.Add($"DeepL 실제 API 사용량: {usage.Value.Used:N0} / {usage.Value.Limit:N0} chars");
                }
                catch (Exception ex)
                {
                    lines.Add("DeepL 실제 사용량 조회 실패: " + ex.Message);
                }
            }

            foreach (var line in lines) AddLog("APITEST " + line);
            RefreshApiUsageText();
            StatusText.Text = "API/로컬 연결 테스트 완료";
            MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, lines), "API/로컬 테스트", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AddLog("APITEST 실패: " + ex.Message);
            MessageBox.Show(ex.Message, "API/로컬 테스트 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ResetApiUsageButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("RoiLingo가 로컬에서 추적한 API 사용량 카운터를 초기화할까요? 실제 서비스의 청구/쿼터는 초기화되지 않습니다.",
                "사용량 초기화", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _apiUsage.ResetAsync();
        RefreshApiUsageText();
        AddLog("QUOTA   로컬 사용량 카운터 초기화");
    }

    private void RefreshApiUsageText()
    {
        var snapshot = _apiUsage.Snapshot();
        if (snapshot.Count == 0)
        {
            ApiUsageText.Text = "사용량: 아직 없음";
            return;
        }
        ApiUsageText.Text = "로컬 사용량: " + string.Join(" | ", snapshot.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value.DailyRequests:N0}회/{x.Value.MonthlyCharacters:N0}자"));
    }

    private void OcrLanguagePickerButton_Click(object sender, RoutedEventArgs e)
    {
        SyncUiToSettings();
        var picker = new OcrLanguagePickerWindow(_settings.OcrLanguages) { Owner = this };
        if (picker.ShowDialog() != true) return;

        _settings.OcrLanguages = picker.SelectedLanguages;
        _settings.OcrLanguageFollowsSource = false;
        OcrLanguageFollowsSourceCheck.IsChecked = false;
        OcrLanguagesBox.Text = _settings.OcrLanguages;
        RefreshOcrLanguageButton();
        SaveAll();
        StatusText.Text = $"OCR 언어: {FormatOcrLanguages(_settings.OcrLanguages)}";
    }

    private void RefreshOcrLanguageButton()
    {
        if (OcrLanguagePickerButton is null) return;
        OcrLanguagePickerButton.Content = "OCR: " + FormatOcrLanguages(_settings.OcrLanguages);
        OcrLanguagePickerButton.ToolTip = $"동시 OCR 언어: {_settings.OcrLanguages}. 클릭하여 여러 언어 선택";
    }

    private static string FormatOcrLanguages(string languages)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["eng"] = "영", ["kor"] = "한", ["jpn"] = "일", ["chi_sim"] = "중간", ["chi_tra"] = "중번",
            ["spa"] = "서", ["fra"] = "불", ["deu"] = "독", ["rus"] = "러", ["por"] = "포",
            ["ita"] = "이", ["vie"] = "베", ["tha"] = "태", ["ind"] = "인니", ["hin"] = "힌", ["ara"] = "아"
        };
        var values = ModelManager.ParseLanguages(string.IsNullOrWhiteSpace(languages) ? "eng+kor" : languages)
            .Select(x => map.TryGetValue(x, out var label) ? label : x)
            .ToArray();
        return values.Length <= 4 ? string.Join('+', values) : $"{string.Join("+", values.Take(3))}+{values.Length - 3}";
    }

    private void AdvancedSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdvancedPanel.Visibility == Visibility.Visible)
        {
            CloseAdvancedSettings();
            return;
        }

        AdvancedPanel.Visibility = Visibility.Visible;
        AdvancedSettingsButton.Content = "설정 닫기";
        MinWidth = AdvancedWindowMinWidth;
        MinHeight = AdvancedWindowMinHeight;

        var work = SystemParameters.WorkArea;
        Width = Math.Min(Math.Max(Width, 1280), Math.Max(AdvancedWindowMinWidth, work.Width - 40));
        Height = Math.Min(Math.Max(Height, 840), Math.Max(AdvancedWindowMinHeight, work.Height - 40));
        ClampWindowToWorkArea(work);
        MainTabs.Focus();
    }

    private void CloseAdvancedSettingsButton_Click(object sender, RoutedEventArgs e) => CloseAdvancedSettings();

    private void CloseAdvancedSettings()
    {
        try
        {
            SyncUiToSettings();
            SaveSecretsFromUi();
            SaveAll();
        }
        catch (Exception ex)
        {
            AddLog("SETTINGS 저장 실패: " + ex.Message);
        }

        AdvancedPanel.Visibility = Visibility.Collapsed;
        AdvancedSettingsButton.Content = "설정";
        MinWidth = 560;
        MinHeight = 135;
        Width = CompactWindowWidth;
        Height = CompactWindowHeight;
        ClampWindowToWorkArea(SystemParameters.WorkArea);
    }

    private void ClampWindowToWorkArea(Rect work)
    {
        if (Left < work.Left) Left = work.Left;
        if (Top < work.Top) Top = work.Top;
        if (Left + Width > work.Right) Left = Math.Max(work.Left, work.Right - Width);
        if (Top + Height > work.Bottom) Top = Math.Max(work.Top, work.Bottom - Height);
    }

    private void PreviewLiveWindowButton_Click(object sender, RoutedEventArgs e)
    {
        SyncUiToSettings();
        EnsureLiveWindow(showEvenWhenDisabled: true);
        _liveWindow?.Activate();
    }


    private void ToggleOverlayEditModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_overlay is null || !_overlay.IsLoaded)
        {
            MessageBox.Show("게임 오버레이 직접 편집은 실시간 번역 실행 중에 사용할 수 있습니다. 먼저 실시간 번역을 시작하세요.",
                "오버레이 직접 편집", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var enable = !_overlay.EditMode;
        _overlay.SetEditMode(enable);
        OverlayEditModeButton.Content = enable ? "게임 오버레이 편집 완료" : "게임 오버레이 직접 편집";
        QuickOverlayEditButton.Content = enable ? "오버레이 편집 완료" : "오버레이 편집";
        StatusText.Text = enable
            ? "오버레이 편집 모드: 번역 박스를 드래그해 이동하고, 우하단 핸들/휠로 크기·투명도를 조절하세요."
            : "오버레이 편집 완료. 다시 클릭 통과 모드로 전환했습니다.";
        AddLog(enable ? "OVERLAY 직접 편집 모드 ON" : "OVERLAY 직접 편집 모드 OFF");
    }
    private void OpenOverlaySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SyncUiToSettings();
        if (_settings.Rois.Count == 0)
        {
            MessageBox.Show("먼저 ROI를 하나 이상 만들어 주세요.", "게임 오버레이 설정",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_overlaySettingsWindow is { IsLoaded: true })
        {
            _overlaySettingsWindow.Activate();
            return;
        }

        _overlaySettingsWindow = new OverlaySettingsWindow(
            _settings,
            SaveAll,
            () => _overlay?.RefreshSettings());
        _overlaySettingsWindow.Owner = this;
        _overlaySettingsWindow.Closed += (_, _) => _overlaySettingsWindow = null;
        _overlaySettingsWindow.Show();
    }

    private async void ClearTranslationCacheButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_translationCache is not null)
                await _translationCache.ClearAsync();

            Directory.CreateDirectory(SettingsStore.AppDirectory);
            foreach (var path in Directory.EnumerateFiles(SettingsStore.AppDirectory, "translation-cache-*.json"))
            {
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            AddLog("CACHE   번역 캐시 초기화 완료");
            StatusText.Text = "번역 캐시를 비웠습니다. 다음 문장은 웹/API에서 다시 번역합니다.";
        }
        catch (Exception ex)
        {
            AddLog("CACHE   초기화 실패: " + ex.Message);
            MessageBox.Show(ex.Message, "번역 캐시 초기화 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EnsureLiveWindow(bool showEvenWhenDisabled)
    {
        if (!showEvenWhenDisabled && !_settings.ShowLiveWindow) return;
        if (_liveWindow is { IsLoaded: true })
        {
            if (!_liveWindow.IsVisible) _liveWindow.Show();
            return;
        }

        _liveWindow = new LiveTranslationWindow(_settings, SaveAll);
        _liveWindow.Closed += (_, _) => _liveWindow = null;
        _liveWindow.Show();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e) => await StopInternalAsync();

    private void OnTranslationPending(RoiTranslationPending pending)
    {
        // v2.1: a transient message may already have disappeared from the game while translation is
        // still running. Keep the previous completed overlay visible until the new result arrives
        // (or its hold timer expires), and persist the captured OCR text immediately in the runtime log.
        StatusText.Text = $"{pending.RoiName}: 문구 캡처 완료 · 번역 중...";
        AddLog($"CAPTURE {pending.RoiName,-12} OCR={pending.OcrConfidence:P0} at={pending.Timestamp:HH:mm:ss.fff} | {pending.SourceText}");
    }

    private void OnTranslationCleared(Guid roiId)
    {
        // Reserved for an explicit future clear action. Visual disappearance alone no longer clears
        // completed translations; OverlayWindow owns its configurable hold timer.
        var roi = _settings.Rois.FirstOrDefault(r => r.Id == roiId);
        if (roi is not null) AddLog($"CLEAR   {roi.Name,-12} 명시적 오버레이 제거");
        _overlay?.ClearTranslation(roiId);
    }

    private async void OnTranslation(RoiTranslationUpdate update)
    {
        _overlay?.UpdateTranslation(update);
        _liveWindow?.AddTranslation(update);
        var agree = update.AgreementScore.ToString("P0");
        var providers = string.Join(" | ", update.Results.Select(x => $"{x.Provider}={x.Text}"));
        AddLog($"{update.RoiName,-12} OCR={update.OcrConfidence:P0} {update.Provider} agree={agree} | {update.SourceText} => {update.TargetText}");
        if (update.Results.Count > 1) AddLog("CROSS   " + providers);
        StatusText.Text = $"{update.RoiName}: {update.TargetText}";

        var historyRecord = new TranslationHistoryRecord(
            Guid.NewGuid(), _sessionId, update.Timestamp, update.RoiId, update.RoiName,
            update.SourceText, update.TargetText, update.Provider, update.OcrConfidence,
            update.AgreementScore, update.Results);
        _history.Insert(0, historyRecord);
        TrimHistory();
        RefreshHistoryView();

        if (_settings.AutoSaveHistory)
        {
            try
            {
                await _historyStore.AppendTranslationAsync(historyRecord);
            }
            catch (Exception ex)
            {
                AddLog("HISTORY 저장 실패: " + ex.Message);
            }
        }

        var roi = _settings.Rois.FirstOrDefault(r => r.Id == update.RoiId);
        if (roi?.EventMode == true)
        {
            var now = DateTimeOffset.Now;
            if (!_lastEventSound.TryGetValue(update.RoiId, out var last) || now - last > TimeSpan.FromSeconds(5))
            {
                System.Media.SystemSounds.Exclamation.Play();
                _lastEventSound[update.RoiId] = now;
            }
        }
    }

    private async Task StopInternalAsync()
    {
        if (_monitor is not null)
        {
            await _monitor.StopAsync();
            await _monitor.DisposeAsync();
            _monitor = null;
        }
        if (_overlay is not null)
        {
            _overlay.Close();
            _overlay = null;
        }
        if (_liveWindow is not null)
        {
            _liveWindow.Close();
            _liveWindow = null;
        }
        _ocr?.Dispose();
        _ocr = null;
        SetRunningUi(false);
        OverlayEditModeButton.Content = "게임 오버레이 직접 편집";
        QuickOverlayEditButton.Content = "오버레이 편집";
        StatusText.Text = "중지됨";
        AddLog("STOP    실시간 감시 중지");
    }

    private void DeleteRoiButton_Click(object sender, RoutedEventArgs e)
    {
        if (RoiGrid.SelectedItem is RoiDefinition roi && _monitor is null)
        {
            _settings.Rois.Remove(roi);
            RefreshRoiGrid();
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SyncUiToSettings();
        SaveSecretsFromUi();
        SaveAll();
        StatusText.Text = "설정이 저장되었습니다.";
        AddLog("SETTINGS 저장 완료");
    }

    private void SaveAll() => SettingsStore.SaveSettings(_settings);

    private void RefreshRoiGrid()
    {
        RoiGrid.ItemsSource = null;
        RoiGrid.ItemsSource = _settings.Rois;
    }

    private bool ValidateTarget()
    {
        if (_targetHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_targetHwnd))
        {
            MessageBox.Show("먼저 '대상 창 선택'으로 Roblox 창을 선택하세요.");
            return false;
        }
        return true;
    }

    private void SetRunningUi(bool running)
    {
        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        QuickOverlayEditButton.IsEnabled = running;
        PickWindowButton.IsEnabled = !running;
        EditRoiButton.IsEnabled = !running;
        RoiGrid.IsReadOnly = running;
        SourceLanguageCombo.IsEnabled = !running;
        OcrLanguagePickerButton.IsEnabled = !running;
        TargetLanguageCombo.IsEnabled = !running;
        TranslationStrategyCombo.IsEnabled = !running;
    }

    private void AddLog(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        LogList.Items.Insert(0, line);
        while (LogList.Items.Count > 500) LogList.Items.RemoveAt(LogList.Items.Count - 1);
        _ = PersistRuntimeLogAsync(line);
    }

    private async Task PersistRuntimeLogAsync(string line)
    {
        try
        {
            await _historyStore.AppendRuntimeAsync(line);
        }
        catch (Exception ex) when (!_logPersistenceWarningShown)
        {
            _logPersistenceWarningShown = true;
            Dispatcher.Invoke(() => StatusText.Text = "실행 로그 파일 저장 실패: " + ex.Message);
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var records = await _historyStore.LoadRecentAsync(_settings.HistoryUiLimit);
            foreach (var record in records) _history.Add(record);
            RefreshHistoryView();
        }
        catch (Exception ex)
        {
            AddLog("HISTORY 불러오기 실패: " + ex.Message);
        }
    }

    private bool HistoryFilter(object item)
    {
        if (item is not TranslationHistoryRecord record) return false;
        var query = HistoryFilterBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query)) return true;
        return record.RoiName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || record.SourceText.Contains(query, StringComparison.OrdinalIgnoreCase)
            || record.TargetText.Contains(query, StringComparison.OrdinalIgnoreCase)
            || record.Provider.Contains(query, StringComparison.OrdinalIgnoreCase)
            || record.Results.Any(x => x.Provider.Contains(query, StringComparison.OrdinalIgnoreCase)
                                    || x.Text.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void HistoryFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshHistoryView();
    }

    private void RefreshHistoryView()
    {
        var view = CollectionViewSource.GetDefaultView(_history);
        view?.Refresh();
        var visible = view?.Cast<object>().Count() ?? _history.Count;
        HistoryCountText.Text = $"표시 {visible:N0} / 메모리 {_history.Count:N0}건";
    }

    private void TrimHistory()
    {
        var max = Math.Clamp(_settings.HistoryUiLimit, 100, 5000);
        while (_history.Count > max) _history.RemoveAt(_history.Count - 1);
    }

    private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ProviderResultList.Items.Clear();
        if (HistoryGrid.SelectedItem is not TranslationHistoryRecord record) return;
        ProviderResultList.Items.Add($"원문: {record.SourceText}");
        ProviderResultList.Items.Add($"선택: {record.Provider} -> {record.TargetText}");
        ProviderResultList.Items.Add($"OCR: {record.OcrConfidence:P0} / 교차 일치도: {record.AgreementScore:P0}");
        ProviderResultList.Items.Add("----------------------------------------");
        foreach (var result in record.Results)
            ProviderResultList.Items.Add($"{result.Provider,-12} ({result.Elapsed.TotalMilliseconds,5:0} ms)  {result.Text}");
    }

    private async void ExportHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await _historyStore.ExportCsvAsync(_history);
            AddLog("EXPORT  " + path);
            MessageBox.Show($"CSV로 저장했습니다.\n{path}", "내보내기 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "내보내기 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsStore.AppDirectory);
        Process.Start(new ProcessStartInfo { FileName = SettingsStore.AppDirectory, UseShellExecute = true });
    }

    private static void SelectComboByTag(ComboBox box, string value)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
            if (string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = item; return; }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private static void SelectComboByContent(ComboBox box, string value)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = item; return; }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        if (_monitor is not null)
        {
            e.Cancel = true;
            _closing = true;
            await StopInternalAsync();
            SyncUiToSettings();
            SaveSecretsFromUi();
            SaveAll();
            Close();
            return;
        }

        if (_liveWindow is not null)
        {
            _liveWindow.Close();
            _liveWindow = null;
        }
        if (_overlaySettingsWindow is not null)
        {
            _overlaySettingsWindow.Close();
            _overlaySettingsWindow = null;
        }
        SyncUiToSettings();
        SaveSecretsFromUi();
        SaveAll();
    }
}
