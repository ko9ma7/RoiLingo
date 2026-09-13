namespace RobloxLiveTranslator.Models;

public sealed record ProviderTranslation(string Provider, string Text, TimeSpan Elapsed);

public sealed record TranslationBundle(
    string SelectedProvider,
    string SelectedText,
    IReadOnlyList<ProviderTranslation> Results,
    double AgreementScore);

public sealed record RoiTranslationUpdate(
    Guid RoiId,
    string RoiName,
    string SourceText,
    string TargetText,
    string Provider,
    float OcrConfidence,
    double AgreementScore,
    DateTimeOffset Timestamp,
    IReadOnlyList<ProviderTranslation> Results);

public sealed record TranslationHistoryRecord(
    Guid Id,
    string SessionId,
    DateTimeOffset Timestamp,
    Guid RoiId,
    string RoiName,
    string SourceText,
    string TargetText,
    string Provider,
    float OcrConfidence,
    double AgreementScore,
    IReadOnlyList<ProviderTranslation> Results);
