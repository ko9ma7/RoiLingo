using System.Net;

namespace RobloxLiveTranslator.Translation.Api;

public sealed class GoogleApiTranslationProvider : ApiProviderBase
{
    private readonly string _apiKey;

    public GoogleApiTranslationProvider(string apiKey, int timeoutMs) : base(timeoutMs) => _apiKey = apiKey.Trim();

    public override string Name => "Google API";
    public override TranslationProviderKind Kind => TranslationProviderKind.Api;
    public override bool IsConfigured => _apiKey.Length > 0;

    protected override async Task<string> TranslateCoreAsync(string text, string targetLanguage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://translation.googleapis.com/language/translate/v2");
        request.Headers.TryAddWithoutValidation("X-goog-api-key", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            q = text,
            target = targetLanguage == "zh" ? "zh-CN" : targetLanguage,
            format = "text"
        }), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, ct);
        var body = await EnsureSuccessAndReadAsync(response, ct);
        using var doc = JsonDocument.Parse(body);
        var translated = doc.RootElement.GetProperty("data").GetProperty("translations")[0].GetProperty("translatedText").GetString() ?? string.Empty;
        return WebUtility.HtmlDecode(translated);
    }
}
