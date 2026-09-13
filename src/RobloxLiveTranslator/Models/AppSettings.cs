namespace RobloxLiveTranslator.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; }
    public string OcrLanguages { get; set; } = "eng";
    public string OcrMode { get; set; } = "Balanced";
    public string TargetLanguage { get; set; } = "ko";
    public int PollIntervalMs { get; set; } = 220;
    public int SettleMs { get; set; } = 150;
    public int ForceOcrSeconds { get; set; } = 8;
    public double ChangeThreshold { get; set; } = 0.035;

    public string TranslationStrategy { get; set; } = "HybridBalanced";
    public string PreferredProvider { get; set; } = "Auto";
    public int ProviderWindowMs { get; set; } = 4500;
    public int WebTranslationTimeoutMs { get; set; } = 8000;
    public int ApiTimeoutMs { get; set; } = 7000;
    public bool AllowClipboardFallback { get; set; } = true;

    public bool AutoSaveHistory { get; set; } = true;
    public int HistoryUiLimit { get; set; } = 1000;
    public bool ShowLiveWindow { get; set; } = true;
    public double? LiveWindowLeft { get; set; }
    public double? LiveWindowTop { get; set; }
    public double LiveWindowWidth { get; set; } = 560;
    public double LiveWindowHeight { get; set; } = 360;
    public double LiveWindowFontSize { get; set; } = 18;
    public bool LiveWindowTopmost { get; set; } = true;
    public bool LiveWindowShowSource { get; set; } = true;
    public int LiveWindowMaxItems { get; set; } = 60;

    public List<RoiDefinition> Rois { get; set; } = [];
    public WebProviderFlags WebProviders { get; set; } = new();
    public ApiProviderSettings ApiProviders { get; set; } = new();
}

public sealed class WebProviderFlags
{
    public bool Papago { get; set; } = true;
    public bool Google { get; set; } = true;
    public bool DeepL { get; set; } = true;
}

public sealed class ApiProviderSettings
{
    public ProviderBudgetSettings Papago { get; set; } = new();
    public ProviderBudgetSettings Google { get; set; } = new();
    public ProviderBudgetSettings DeepL { get; set; } = new();
    public ProviderBudgetSettings LibreTranslate { get; set; } = new();
    public bool DeepLUseFreeEndpoint { get; set; } = true;
    public string LibreTranslateEndpoint { get; set; } = "http://localhost:5000";
}

public sealed class ProviderBudgetSettings
{
    public bool Enabled { get; set; }
    public int DailyRequestLimit { get; set; }
    public int MonthlyCharacterLimit { get; set; }
}
