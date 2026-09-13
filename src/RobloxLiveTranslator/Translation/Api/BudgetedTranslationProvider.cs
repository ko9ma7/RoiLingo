using RobloxLiveTranslator.Models;

namespace RobloxLiveTranslator.Translation.Api;

public sealed class BudgetedTranslationProvider : ITranslationProvider, IQuotaAwareTranslationProvider
{
    private readonly ITranslationProvider _inner;
    private readonly ProviderBudgetSettings _budget;
    private readonly ApiUsageStore _usage;
    private readonly Action<string>? _diagnostic;

    public BudgetedTranslationProvider(
        ITranslationProvider inner,
        ProviderBudgetSettings budget,
        ApiUsageStore usage,
        Action<string>? diagnostic = null)
    {
        _inner = inner;
        _budget = budget;
        _usage = usage;
        _diagnostic = diagnostic;
    }

    public string Name => _inner.Name;
    public TranslationProviderKind Kind => _inner.Kind;
    public bool IsConfigured => _budget.Enabled && _inner.IsConfigured;

    public bool CanSchedule(int sourceCharacters, out string reason)
    {
        if (!IsConfigured)
        {
            reason = "Provider가 비활성화되었거나 설정이 완료되지 않음";
            return false;
        }
        var check = _usage.CanUseSnapshot(Name, _budget.DailyRequestLimit, _budget.MonthlyCharacterLimit, sourceCharacters);
        reason = check.Reason;
        return check.Allowed;
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        var allowed = await _usage.CanUseAsync(Name, _budget.DailyRequestLimit, _budget.MonthlyCharacterLimit, text.Length, cancellationToken);
        if (!allowed.Allowed)
        {
            _diagnostic?.Invoke($"{Name}: 로컬 사용량 제한으로 건너뜀 - {allowed.Reason}");
            throw new InvalidOperationException(allowed.Reason);
        }

        var translated = await _inner.TranslateAsync(text, targetLanguage, cancellationToken);
        if (!string.IsNullOrWhiteSpace(translated))
            await _usage.RecordAsync(Name, text.Length, cancellationToken);
        return translated;
    }
}
