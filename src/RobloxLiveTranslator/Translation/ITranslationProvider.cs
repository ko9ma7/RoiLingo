namespace RobloxLiveTranslator.Translation;

public enum TranslationProviderKind
{
    Web,
    Api,
    Local
}

public interface ITranslationProvider
{
    string Name { get; }
    TranslationProviderKind Kind { get; }
    bool IsConfigured { get; }
    Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken);
}

public interface IQuotaAwareTranslationProvider
{
    bool CanSchedule(int sourceCharacters, out string reason);
}
