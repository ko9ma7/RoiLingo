namespace RobloxLiveTranslator.Translation.Api;

public sealed class DeepLApiTranslationProvider : ApiProviderBase
{
    private readonly string _apiKey;
    private readonly bool _free;

    public DeepLApiTranslationProvider(string apiKey, bool useFreeEndpoint, int timeoutMs) : base(timeoutMs)
    {
        _apiKey = apiKey.Trim();
        _free = useFreeEndpoint;
    }

    public override string Name => "DeepL API";
    public override TranslationProviderKind Kind => TranslationProviderKind.Api;
    public override bool IsConfigured => _apiKey.Length > 0;

    private string BaseUrl => _free ? "https://api-free.deepl.com" : "https://api.deepl.com";

    protected override async Task<string> TranslateCoreAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v2/translate");
        request.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + _apiKey);
        var payload = new Dictionary<string, object?>
        {
            ["text"] = new[] { text },
            ["target_lang"] = TranslationLanguages.ToDeepLApi(targetLanguage)
        };
        var source = TranslationLanguages.Normalize(sourceLanguage);
        if (source != "auto") payload["source_lang"] = TranslationLanguages.ToDeepLApiSource(source);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, ct);
        var body = await EnsureSuccessAndReadAsync(response, ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("translations")[0].GetProperty("text").GetString() ?? string.Empty;
    }

    public async Task<(long Used, long Limit)?> GetUsageAsync(CancellationToken ct)
    {
        if (!IsConfigured) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/v2/usage");
        request.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + _apiKey);
        using var response = await Http.SendAsync(request, ct);
        var body = await EnsureSuccessAndReadAsync(response, ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("character_count", out var used) || !root.TryGetProperty("character_limit", out var limit)) return null;
        return (used.GetInt64(), limit.GetInt64());
    }
}
