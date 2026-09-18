using RobloxLiveTranslator.Models;

namespace RobloxLiveTranslator.Translation;

public sealed class MultiTranslator
{
    private readonly IReadOnlyList<ITranslationProvider> _providers;
    private readonly string _preferred;
    private readonly string _strategy;
    private readonly int _windowMs;
    private readonly TranslationCache _cache;
    private int _rotation;

    public MultiTranslator(
        IEnumerable<ITranslationProvider> providers,
        string preferred,
        string strategy,
        int windowMs,
        TranslationCache cache)
    {
        _providers = providers.Where(p => p.IsConfigured).ToArray();
        _preferred = string.IsNullOrWhiteSpace(preferred) ? "Auto" : preferred;
        _strategy = string.IsNullOrWhiteSpace(strategy) ? "WebOnly" : strategy;
        _windowMs = Math.Clamp(windowMs, 1200, 12000);
        _cache = cache;
    }

    public bool HasProvider => _providers.Count > 0;
    public event Action<string>? Diagnostic;

    public async Task<TranslationBundle> TranslateAsync(string source, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        sourceLanguage = TranslationLanguages.Normalize(sourceLanguage);
        targetLanguage = TranslationLanguages.Normalize(targetLanguage, false);
        if (sourceLanguage != "auto" && sourceLanguage.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase))
            return new TranslationBundle("원문", source, [new ProviderTranslation("원문", source, TimeSpan.Zero)], 1);

        var hasWeb = _providers.Any(p => p.Kind == TranslationProviderKind.Web);
        var hasNonWeb = _providers.Any(p => p.Kind != TranslationProviderKind.Web);
        var webCrossCheckMode = hasWeb &&
            (_strategy.Equals("WebOnly", StringComparison.OrdinalIgnoreCase) ||
             _strategy.Equals("MaximumCrossCheck", StringComparison.OrdinalIgnoreCase) ||
             !hasNonWeb);

        // When Web translators are the only translation path, do not let one old cached answer
        // short-circuit the whole WebView pipeline. The user explicitly enabled visible Web cross-checking,
        // so every enabled Web tab should receive the current OCR text. Cache remains a fast path for
        // API/local hybrid mode where avoiding paid requests is more important.
        if (!webCrossCheckMode && _cache.TryGet(source, sourceLanguage, targetLanguage, out var cachedProvider, out var cachedText))
            return new TranslationBundle(cachedProvider + " (cache)", cachedText,
                [new ProviderTranslation(cachedProvider + " (cache)", cachedText, TimeSpan.Zero)], 1);

        var scheduled = SelectProvidersForRequest(source.Length);
        if (scheduled.Count == 0)
            return new TranslationBundle("없음", "[활성화된 번역 제공자가 없습니다]", [], 0);

        Diagnostic?.Invoke("이번 번역: " + string.Join(", ", scheduled.Select(x => x.Name)));

        var hardDeadlineMs = Math.Max(12000, _windowMs + 5000);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromMilliseconds(hardDeadlineMs + 500));
        var tasks = scheduled.Select(p => TranslateSafeAsync(p, source, sourceLanguage, targetLanguage, linked.Token)).ToArray();
        var timer = Stopwatch.StartNew();
        long? crossCheckReadyAtMs = null;

        List<ProviderTranslation> completed;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completed = Completed(tasks);
            var preferredReady = IsAutoPreferred()
                ? completed.Count > 0
                : completed.Any(x => x.Provider.Equals(_preferred, StringComparison.OrdinalIgnoreCase));
            var enoughForCrossCheck = completed.Count >= Math.Min(2, scheduled.Count);
            var allFinished = tasks.All(t => t.IsCompleted);

            if (preferredReady && enoughForCrossCheck && crossCheckReadyAtMs is null)
                crossCheckReadyAtMs = timer.ElapsedMilliseconds;

            var graceExpired = crossCheckReadyAtMs is not null
                && timer.ElapsedMilliseconds - crossCheckReadyAtMs.Value >= 750;
            var normalWindowExpiredWithResult = completed.Count > 0 && timer.ElapsedMilliseconds >= _windowMs;
            if (allFinished || graceExpired || normalWindowExpiredWithResult || timer.ElapsedMilliseconds >= hardDeadlineMs)
                break;

            var pending = tasks.Where(t => !t.IsCompleted).Cast<Task>().ToArray();
            if (pending.Length == 0) break;
            await Task.WhenAny(pending.Append(Task.Delay(150, cancellationToken)));
        }

        completed = Completed(tasks);
        if (completed.Count == 0 && tasks.Any(t => !t.IsCompleted))
        {
            var remaining = Math.Max(0, hardDeadlineMs - (int)timer.ElapsedMilliseconds);
            if (remaining > 0)
                await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(remaining, cancellationToken));
            completed = Completed(tasks);
        }

        linked.Cancel();
        if (completed.Count == 0)
            return new TranslationBundle("실패", "[활성화된 번역 제공자에서 결과를 얻지 못했습니다]", [], 0);

        var selected = SelectPreferred(completed, targetLanguage);
        var agreement = Agreement(completed);
        await _cache.PutAsync(source, sourceLanguage, targetLanguage, selected.Provider, selected.Text);
        return new TranslationBundle(selected.Provider, selected.Text, completed, agreement);
    }

    /// <summary>
    /// Low-latency path for one-shot captures. All eligible providers are started, but the first
    /// validated result wins and the rest are cancelled. Fixed ROI monitoring still uses TranslateAsync
    /// so its normal cross-check behaviour is preserved.
    /// </summary>
    public async Task<TranslationBundle> TranslateFirstSuccessAsync(
        string source,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        sourceLanguage = TranslationLanguages.Normalize(sourceLanguage);
        targetLanguage = TranslationLanguages.Normalize(targetLanguage, false);
        if (sourceLanguage != "auto" && sourceLanguage.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase))
            return new TranslationBundle("원문", source, [new ProviderTranslation("원문", source, TimeSpan.Zero)], 1);

        if (_cache.TryGet(source, sourceLanguage, targetLanguage, out var cachedProvider, out var cachedText))
            return new TranslationBundle(cachedProvider + " (cache)", cachedText,
                [new ProviderTranslation(cachedProvider + " (cache)", cachedText, TimeSpan.Zero)], 1);

        var scheduled = SelectProvidersForRequest(source.Length);
        if (scheduled.Count == 0)
            return new TranslationBundle("없음", "[활성화된 번역 제공자가 없습니다]", [], 0);

        Diagnostic?.Invoke("빠른 번역(첫 성공): " + string.Join(", ", scheduled.Select(x => x.Name)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(7000, _windowMs + 3500)));
        var pending = scheduled
            .Select(p => TranslateSafeAsync(p, source, sourceLanguage, targetLanguage, linked.Token))
            .ToList();

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);
            var result = await finished;
            if (result is null) continue;

            linked.Cancel();
            await _cache.PutAsync(source, sourceLanguage, targetLanguage, result.Provider, result.Text);
            return new TranslationBundle(result.Provider, result.Text, [result], 1);
        }

        return new TranslationBundle("실패", "[활성화된 번역 제공자에서 결과를 얻지 못했습니다]", [], 0);
    }

    private IReadOnlyList<ITranslationProvider> SelectProvidersForRequest(int sourceCharacters)
    {
        var all = _providers.Where(p => p.IsConfigured).Where(p =>
        {
            if (p is not IQuotaAwareTranslationProvider quotaAware) return true;
            var allowed = quotaAware.CanSchedule(sourceCharacters, out var reason);
            if (!allowed) Diagnostic?.Invoke($"{p.Name}: 사용량 예산 때문에 이번 요청에서 제외 - {reason}");
            return allowed;
        }).ToArray();
        if (all.Length == 0) return [];

        if (_strategy.Equals("MaximumCrossCheck", StringComparison.OrdinalIgnoreCase))
            return all;
        if (_strategy.Equals("WebOnly", StringComparison.OrdinalIgnoreCase))
            return all.Where(p => p.Kind == TranslationProviderKind.Web).ToArray();
        if (_strategy.Equals("ApiOnly", StringComparison.OrdinalIgnoreCase))
            return PickRotating(all.Where(p => p.Kind != TranslationProviderKind.Web).ToArray(), 2);

        // HybridBalanced: if API/local providers exist, use one API/local + one web provider to
        // preserve credits.  If there is no API/local provider at all, all enabled Web providers
        // are scheduled together.  That matches RoiLingo's original Web cross-check use case and
        // also makes every enabled translator tab visibly receive the OCR sentence.
        var api = all.Where(p => p.Kind != TranslationProviderKind.Web).ToArray();
        var web = all.Where(p => p.Kind == TranslationProviderKind.Web).ToArray();
        if (api.Length == 0 && web.Length > 0)
            return web;

        var result = new List<ITranslationProvider>(2);

        var preferred = all.FirstOrDefault(p => !IsAutoPreferred() && p.Name.Equals(_preferred, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null) result.Add(preferred);

        if (api.Length > 0 && result.All(x => x.Kind == TranslationProviderKind.Web))
            result.Add(PickOne(api));
        if (web.Length > 0 && result.All(x => x.Kind != TranslationProviderKind.Web))
            result.Add(PickOne(web));

        if (result.Count == 0)
        {
            if (api.Length > 0) result.Add(PickOne(api));
            if (web.Length > 0) result.Add(PickOne(web));
        }
        else if (result.Count == 1)
        {
            var pool = result[0].Kind == TranslationProviderKind.Web ? api : web;
            if (pool.Length > 0) result.Add(PickOne(pool));
        }

        return result.DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
    }

    private IReadOnlyList<ITranslationProvider> PickRotating(IReadOnlyList<ITranslationProvider> providers, int count)
    {
        if (providers.Count <= count) return providers.ToArray();
        var start = Math.Abs(Interlocked.Increment(ref _rotation)) % providers.Count;
        return Enumerable.Range(0, count).Select(i => providers[(start + i) % providers.Count]).ToArray();
    }

    private ITranslationProvider PickOne(IReadOnlyList<ITranslationProvider> providers)
    {
        if (providers.Count == 1) return providers[0];
        var index = Math.Abs(Interlocked.Increment(ref _rotation)) % providers.Count;
        return providers[index];
    }

    private bool IsAutoPreferred() => _preferred.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                                      _preferred.StartsWith("자동", StringComparison.OrdinalIgnoreCase);

    private static List<ProviderTranslation> Completed(IEnumerable<Task<ProviderTranslation?>> tasks) =>
        tasks.Where(t => t.IsCompletedSuccessfully)
            .Select(t => t.Result)
            .Where(x => x is not null)
            .Cast<ProviderTranslation>()
            .ToList();

    private async Task<ProviderTranslation?> TranslateSafeAsync(ITranslationProvider provider, string source, string sourceLanguage, string target, CancellationToken ct)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var text = await provider.TranslateAsync(source, sourceLanguage, target, ct);
            sw.Stop();
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            if (!TranslationTextValidator.IsUsable(source, text, target))
            {
                Diagnostic?.Invoke($"{provider.Name}: 번역 결과가 페이지 UI/실패 문구로 판단되어 폐기됨");
                return null;
            }
            return new ProviderTranslation(provider.Name, text, sw.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Diagnostic?.Invoke($"{provider.Name}: {ex.Message}");
            return null;
        }
    }

    private ProviderTranslation SelectPreferred(IReadOnlyList<ProviderTranslation> results, string target)
    {
        if (!IsAutoPreferred())
        {
            var exact = results.FirstOrDefault(x => x.Provider.Equals(_preferred, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }

        var order = target.Equals("ko", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Papago API", "Papago Web", "Google API", "Google Web", "DeepL API", "DeepL Web", "LibreTranslate" }
            : new[] { "DeepL API", "Google API", "LibreTranslate", "DeepL Web", "Google Web", "Papago API", "Papago Web" };
        foreach (var name in order)
        {
            var item = results.FirstOrDefault(x => x.Provider.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (item is not null) return item;
        }
        return results.OrderBy(x => x.Elapsed).First();
    }

    private static double Agreement(IReadOnlyList<ProviderTranslation> results)
    {
        if (results.Count < 2) return 1;
        var scores = new List<double>();
        for (var i = 0; i < results.Count; i++)
        for (var j = i + 1; j < results.Count; j++)
            scores.Add(TextSimilarity.Ratio(results[i].Text, results[j].Text));
        return scores.Count == 0 ? 1 : scores.Average();
    }
}
