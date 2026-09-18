using RobloxLiveTranslator.Models;
using RobloxLiveTranslator.Translation;

namespace RobloxLiveTranslator.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // Keep the 1.0.x data directory so existing ROI/settings are preserved after upgrading to RoiLingo 1.3.
    public static string AppDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RobloxLiveTranslator");

    public static string WebViewDataDirectory => Path.Combine(AppDirectory, "webview2");

    private static string SettingsPath => Path.Combine(AppDirectory, "settings.json");

    public static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return CreateDefault();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? CreateDefault();
            return Migrate(settings);
        }
        catch (JsonException)
        {
            BackupBrokenFile(SettingsPath);
            return CreateDefault();
        }
        catch (IOException)
        {
            return CreateDefault();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        settings.SchemaVersion = 13;
        Directory.CreateDirectory(AppDirectory);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, SettingsPath, true);
    }

    private static AppSettings Migrate(AppSettings settings)
    {
        settings.CaptureVisibilityPolicy = settings.CaptureVisibilityPolicy switch
        {
            "Exclude" => "Exclude",
            "MonitorOnly" => "MonitorOnly",
            _ => "Allow"
        };
        settings.PreferredProvider = string.IsNullOrWhiteSpace(settings.PreferredProvider) ? "Auto" : settings.PreferredProvider;
        if (settings.SchemaVersion < 2)
        {
            settings.WebProviders ??= new WebProviderFlags();
            settings.WebProviders.Papago = true;
            settings.WebProviders.Google = true;
            settings.WebProviders.DeepL = true;
            settings.ProviderWindowMs = Math.Max(settings.ProviderWindowMs, 3200);
            settings.WebTranslationTimeoutMs = Math.Max(settings.WebTranslationTimeoutMs, 6000);

            if (!settings.PreferredProvider.EndsWith(" Web", StringComparison.OrdinalIgnoreCase))
                settings.PreferredProvider = settings.PreferredProvider switch
                {
                    "Google" => "Google Web",
                    "DeepL" => "DeepL Web",
                    _ => "Papago Web"
                };
        }

        settings.SourceLanguage = string.IsNullOrWhiteSpace(settings.SourceLanguage) ? "en" : TranslationLanguages.Normalize(settings.SourceLanguage);
        settings.TargetLanguage = string.IsNullOrWhiteSpace(settings.TargetLanguage) ? "ko" : TranslationLanguages.Normalize(settings.TargetLanguage, false);
        settings.OcrLanguages = string.IsNullOrWhiteSpace(settings.OcrLanguages) ? "eng+kor" : settings.OcrLanguages.Trim();
        settings.TranslationStrategy = string.IsNullOrWhiteSpace(settings.TranslationStrategy) ? "Fastest" : settings.TranslationStrategy;
        settings.Rois ??= [];

        settings.WebProviders ??= new WebProviderFlags();
        settings.ApiProviders ??= new ApiProviderSettings();
        settings.ApiProviders.Papago ??= new ProviderBudgetSettings();
        settings.ApiProviders.Google ??= new ProviderBudgetSettings();
        settings.ApiProviders.DeepL ??= new ProviderBudgetSettings();
        settings.ApiProviders.LibreTranslate ??= new ProviderBudgetSettings();

        if (settings.SchemaVersion < 4)
        {
            settings.TranslationStrategy = "HybridBalanced";
            settings.PreferredProvider = "Auto";
            settings.WebTranslationTimeoutMs = Math.Max(settings.WebTranslationTimeoutMs, 8000);
        }

        if (settings.SchemaVersion < 6)
        {
            // v1.7 adopts a compact startup shell. The optional history-style live window
            // remains available from the main toolbar, but no longer opens automatically.
            settings.ShowLiveWindow = false;
        }

        if (settings.SchemaVersion < 7)
        {
            // v1.8 separates OCR/source language from target language. Existing projects were
            // overwhelmingly English OCR, so preserve behavior by defaulting the source to English.
            settings.SourceLanguage = string.IsNullOrWhiteSpace(settings.SourceLanguage) ? "en" : settings.SourceLanguage;
            settings.OcrLanguageFollowsSource = true;
            // Previous heuristic web extraction cached translator UI text. A clean Web-only strategy
            // is the safest default for users without API credentials; configured API users can switch
            // to HybridBalanced from the compact mode selector.
            var hasEnabledApiOrLocal = settings.ApiProviders.Papago.Enabled ||
                                       settings.ApiProviders.Google.Enabled ||
                                       settings.ApiProviders.DeepL.Enabled ||
                                       settings.ApiProviders.LibreTranslate.Enabled;
            if (string.IsNullOrWhiteSpace(settings.TranslationStrategy) ||
                (!hasEnabledApiOrLocal && settings.TranslationStrategy.Equals("HybridBalanced", StringComparison.OrdinalIgnoreCase)))
                settings.TranslationStrategy = "WebOnly";
        }


        if (settings.SchemaVersion < 8)
        {
            // v1.9: automatic source detection is paired with a real multi-language OCR set.
            // Existing English->Korean installs are migrated to eng+kor so bilingual screens do not
            // turn Hangul into Latin garbage before translation. Users can change the set at any time.
            if (settings.TargetLanguage.Equals("ko", StringComparison.OrdinalIgnoreCase) &&
                (settings.SourceLanguage.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                 settings.SourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase)))
            {
                settings.SourceLanguage = "auto";
                settings.OcrLanguageFollowsSource = false;
                if (string.IsNullOrWhiteSpace(settings.OcrLanguages) ||
                    settings.OcrLanguages.Equals("eng", StringComparison.OrdinalIgnoreCase))
                    settings.OcrLanguages = "eng+kor";
            }
            settings.SmartMixedText = true;
        }


        if (settings.SchemaVersion < 9)
        {
            // v2.0: background-first capture, faster startup defaults, independent overlay height, and UI locale.
            settings.UiLanguage = string.IsNullOrWhiteSpace(settings.UiLanguage) ? "ko-KR" : settings.UiLanguage;
            settings.CaptureMode = string.IsNullOrWhiteSpace(settings.CaptureMode) ? "Auto" : settings.CaptureMode;
            settings.BackgroundWarmup = true;
            if (settings.PollIntervalMs >= 200) settings.PollIntervalMs = 150;
            if (settings.SettleMs >= 150) settings.SettleMs = 100;
            if (settings.ProviderWindowMs > 3000) settings.ProviderWindowMs = 2500;
        }


        if (settings.SchemaVersion < 10)
        {
            // v2.1: transient messages are retained long enough to finish OCR/translation, and
            // overlays use independent absolute width/height once the user resizes them directly.
            foreach (var roi in settings.Rois ?? [])
            {
                if (roi.OverlayHoldSeconds <= 0) roi.OverlayHoldSeconds = 20;
                roi.OverlayWidth = Math.Max(0, roi.OverlayWidth);
                roi.OverlayHeight = Math.Max(0, roi.OverlayHeight);
            }
        }

        if (settings.SchemaVersion < 11)
        {
            // v2.2.1: foreground GPU/game windows must prefer the real screen image; PrintWindow
            // can report success while returning a blank/stale surface.  Auto still uses PrintWindow
            // when the target is covered/inactive.
            if (string.IsNullOrWhiteSpace(settings.CaptureMode) ||
                settings.CaptureMode.Equals("BackgroundFirst", StringComparison.OrdinalIgnoreCase))
                settings.CaptureMode = "Auto";
        }

        if (settings.SchemaVersion < 13)
        {
            // v2.2.2: the old defaults performed three simultaneous WebView translations and
            // two OCR passes. Migrate only the recognizable untouched legacy combination so
            // deliberate user tuning is preserved.
            var legacyDefaults = (settings.SourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                                  settings.SourceLanguage.Equals("en", StringComparison.OrdinalIgnoreCase)) &&
                                 settings.OcrLanguages.Equals("eng+kor", StringComparison.OrdinalIgnoreCase) &&
                                 !settings.OcrLanguageFollowsSource &&
                                 settings.OcrMode.Equals("Balanced", StringComparison.OrdinalIgnoreCase) &&
                                 settings.TranslationStrategy.Equals("WebOnly", StringComparison.OrdinalIgnoreCase) &&
                                 settings.PollIntervalMs == 150 &&
                                 settings.SettleMs == 100 &&
                                 settings.ProviderWindowMs == 2500;
            if (legacyDefaults)
            {
                settings.SourceLanguage = "en";
                settings.OcrMode = "Fast";
                settings.TranslationStrategy = "Fastest";
                settings.ProviderWindowMs = 1600;
            }
        }

        settings.LiveWindowWidth = Math.Clamp(settings.LiveWindowWidth, 360, 2400);
        settings.LiveWindowHeight = Math.Clamp(settings.LiveWindowHeight, 220, 1600);
        settings.LiveWindowFontSize = Math.Clamp(settings.LiveWindowFontSize, 12, 32);
        settings.LiveWindowMaxItems = Math.Clamp(settings.LiveWindowMaxItems, 10, 200);
        settings.ApiTimeoutMs = Math.Clamp(settings.ApiTimeoutMs, 2000, 30000);

        foreach (var roi in settings.Rois ?? [])
        {
            roi.OverlayWidthScale = roi.OverlayWidthScale <= 0 ? 1.0 : Math.Clamp(roi.OverlayWidthScale, 0.25, 5.0);
            roi.OverlayHeightScale = roi.OverlayHeightScale <= 0 ? 1.0 : Math.Clamp(roi.OverlayHeightScale, 0.35, 5.0);
            roi.OverlayWidth = Math.Clamp(roi.OverlayWidth, 0, 2400);
            roi.OverlayHeight = Math.Clamp(roi.OverlayHeight, 0, 1600);
            roi.OverlayHoldSeconds = Math.Clamp(roi.OverlayHoldSeconds, 0, 300);
            roi.OverlayOpacity = roi.OverlayOpacity <= 0 ? 0.82 : Math.Clamp(roi.OverlayOpacity, 0.10, 1.0);
            roi.OverlayFontSize = roi.OverlayFontSize <= 0 ? 17 : Math.Clamp(roi.OverlayFontSize, 10, 42);
            roi.OverlayOffsetX = Math.Clamp(roi.OverlayOffsetX, -1000, 1000);
            roi.OverlayOffsetY = Math.Clamp(roi.OverlayOffsetY, -800, 800);
        }

        settings.SchemaVersion = 13;
        return settings;
    }

    private static void BackupBrokenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Copy(path, path + $".broken-{DateTimeOffset.Now:yyyyMMddHHmmss}", true);
        }
        catch (IOException)
        {
            // Optional backup failure is non-fatal; the caller already falls back to defaults.
        }
    }

    private static AppSettings CreateDefault() => new()
    {
        SchemaVersion = 13,
        CaptureVisibilityPolicy = "Allow",
        TranslationStrategy = "Fastest",
        SourceLanguage = "en",
        OcrLanguages = "eng+kor",
        OcrLanguageFollowsSource = false,
        OcrMode = "Fast",
        ProviderWindowMs = 1600,
        SmartMixedText = true
    };
}
