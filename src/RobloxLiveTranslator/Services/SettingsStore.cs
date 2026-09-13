using RobloxLiveTranslator.Models;

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
        settings.SchemaVersion = 4;
        Directory.CreateDirectory(AppDirectory);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, SettingsPath, true);
    }

    private static AppSettings Migrate(AppSettings settings)
    {
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

        settings.LiveWindowWidth = Math.Clamp(settings.LiveWindowWidth, 360, 2400);
        settings.LiveWindowHeight = Math.Clamp(settings.LiveWindowHeight, 220, 1600);
        settings.LiveWindowFontSize = Math.Clamp(settings.LiveWindowFontSize, 12, 32);
        settings.LiveWindowMaxItems = Math.Clamp(settings.LiveWindowMaxItems, 10, 200);
        settings.ApiTimeoutMs = Math.Clamp(settings.ApiTimeoutMs, 2000, 30000);
        settings.SchemaVersion = 4;
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

    private static AppSettings CreateDefault() => new() { SchemaVersion = 4 };
}
