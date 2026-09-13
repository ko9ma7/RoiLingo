namespace RobloxLiveTranslator.Translation.Api;

public sealed class LibreTranslateProvider : ApiProviderBase
{
    private readonly string _endpoint;
    private readonly string _apiKey;

    public LibreTranslateProvider(string endpoint, string apiKey, int timeoutMs) : base(timeoutMs)
    {
        _endpoint = (endpoint ?? string.Empty).Trim().TrimEnd('/');
        _apiKey = (apiKey ?? string.Empty).Trim();
    }

    public override string Name => "LibreTranslate";
    public override TranslationProviderKind Kind => IsLocalEndpoint(_endpoint) ? TranslationProviderKind.Local : TranslationProviderKind.Api;
    public override bool IsConfigured => Uri.TryCreate(_endpoint, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    protected override async Task<string> TranslateCoreAsync(string text, string targetLanguage, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["q"] = text,
            ["source"] = "auto",
            ["target"] = targetLanguage == "zh-CN" ? "zh" : targetLanguage,
            ["format"] = "text"
        };
        if (_apiKey.Length > 0) payload["api_key"] = _apiKey;
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint + "/translate")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        using var response = await Http.SendAsync(request, ct);
        var body = await EnsureSuccessAndReadAsync(response, ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("translatedText").GetString() ?? string.Empty;
    }

    private static bool IsLocalEndpoint(string endpoint) =>
        endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Contains("::1", StringComparison.OrdinalIgnoreCase);
}
