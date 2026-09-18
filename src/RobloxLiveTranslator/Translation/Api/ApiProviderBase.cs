namespace RobloxLiveTranslator.Translation.Api;

public abstract class ApiProviderBase : ITranslationProvider
{
    protected static readonly HttpClient Http = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private readonly int _timeoutMs;

    protected ApiProviderBase(int timeoutMs) => _timeoutMs = Math.Clamp(timeoutMs, 2000, 30000);

    public abstract string Name { get; }
    public abstract TranslationProviderKind Kind { get; }
    public abstract bool IsConfigured { get; }

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_timeoutMs);
        return await TranslateCoreAsync(text, sourceLanguage, targetLanguage, linked.Token);
    }

    protected abstract Task<string> TranslateCoreAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct);

    protected static async Task<string> EnsureSuccessAndReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var concise = body.Length > 500 ? body[..500] : body;
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {concise}");
        }
        return body;
    }
}
