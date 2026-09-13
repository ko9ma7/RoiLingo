namespace RobloxLiveTranslator.Translation.Api;

public sealed class PapagoApiTranslationProvider : ApiProviderBase
{
    private readonly string _clientId;
    private readonly string _clientSecret;

    public PapagoApiTranslationProvider(string clientId, string clientSecret, int timeoutMs) : base(timeoutMs)
    {
        _clientId = clientId.Trim();
        _clientSecret = clientSecret.Trim();
    }

    public override string Name => "Papago API";
    public override TranslationProviderKind Kind => TranslationProviderKind.Api;
    public override bool IsConfigured => _clientId.Length > 0 && _clientSecret.Length > 0;

    protected override async Task<string> TranslateCoreAsync(string text, string targetLanguage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://papago.apigw.ntruss.com/nmt/v1/translation");
        request.Headers.TryAddWithoutValidation("X-NCP-APIGW-API-KEY-ID", _clientId);
        request.Headers.TryAddWithoutValidation("X-NCP-APIGW-API-KEY", _clientSecret);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["source"] = "auto",
            ["target"] = MapTarget(targetLanguage),
            ["text"] = text
        });
        using var response = await Http.SendAsync(request, ct);
        var body = await EnsureSuccessAndReadAsync(response, ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("message").GetProperty("result").GetProperty("translatedText").GetString() ?? string.Empty;
    }

    private static string MapTarget(string value) => value switch
    {
        "zh" => "zh-CN",
        _ => value
    };
}
