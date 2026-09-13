using Microsoft.Web.WebView2.Wpf;

namespace RobloxLiveTranslator.Translation.Web;

public sealed class WebViewTranslationProvider : ITranslationProvider
{
    private static readonly SemaphoreSlim ClipboardGate = new(1, 1);
    private readonly WebView2 _webView;
    private readonly Func<string, string, string> _urlBuilder;
    private readonly string _resultScript;
    private readonly int _timeoutMs;
    private readonly bool _allowClipboardFallback;
    private readonly Action<string>? _diagnostic;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _lastResult = string.Empty;

    public string Name { get; }
    public TranslationProviderKind Kind => TranslationProviderKind.Web;
    public bool IsConfigured { get; }

    public WebViewTranslationProvider(
        string name,
        WebView2 webView,
        Func<string, string, string> urlBuilder,
        string resultScript,
        bool enabled,
        int timeoutMs,
        bool allowClipboardFallback,
        Action<string>? diagnostic = null)
    {
        Name = name;
        _webView = webView;
        _urlBuilder = urlBuilder;
        _resultScript = resultScript;
        IsConfigured = enabled;
        _timeoutMs = Math.Clamp(timeoutMs, 3000, 25000);
        _allowClipboardFallback = allowClipboardFallback;
        _diagnostic = diagnostic;
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException($"{Name} is disabled.");
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (text.Length > 1800) text = text[..1800];

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_webView.Dispatcher.CheckAccess())
                return await TranslateOnUiThreadAsync(text, targetLanguage, cancellationToken);

            var op = _webView.Dispatcher.InvokeAsync(() => TranslateOnUiThreadAsync(text, targetLanguage, cancellationToken));
            return await op.Task.Unwrap();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> TranslateOnUiThreadAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (_webView.CoreWebView2 is null)
            await _webView.EnsureCoreWebView2Async();
        var core = _webView.CoreWebView2 ?? throw new InvalidOperationException($"{Name} WebView2 초기화에 실패했습니다.");
        var clipboardBefore = _allowClipboardFallback ? WebResultExtractor.ReadClipboardText() : string.Empty;
        core.Navigate(_urlBuilder(text, targetLanguage));

        var started = Stopwatch.StartNew();
        string? stableCandidate = null;
        string stableMethod = "none";
        var stableCount = 0;
        long lastAccessibilityProbeMs = -10000;
        var copyAttempted = false;

        while (started.ElapsedMilliseconds < _timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(240, cancellationToken);

            var elapsedMs = started.ElapsedMilliseconds;
            var useAccessibility = elapsedMs >= 1700 && elapsedMs - lastAccessibilityProbeMs >= 1000;
            if (useAccessibility) lastAccessibilityProbeMs = elapsedMs;
            var extraction = await WebResultExtractor.TryExtractAsync(core, _resultScript, text, targetLanguage, Name, useAccessibility);
            var candidate = Normalize(extraction.Text);

            if (string.IsNullOrWhiteSpace(candidate) && _allowClipboardFallback && elapsedMs >= 2800 && !copyAttempted)
            {
                copyAttempted = true;
                await ClipboardGate.WaitAsync(cancellationToken);
                try
                {
                    var copied = await WebResultExtractor.TryCopyButtonClipboardAsync(core, text, targetLanguage, clipboardBefore);
                    candidate = Normalize(copied.Text);
                    if (!string.IsNullOrWhiteSpace(candidate)) extraction = copied;
                }
                finally
                {
                    ClipboardGate.Release();
                }
            }

            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (Equivalent(candidate, text) && started.ElapsedMilliseconds < 2200) continue;
            if (started.ElapsedMilliseconds < 1400 && Equivalent(candidate, _lastResult)) continue;

            if (Equivalent(candidate, stableCandidate)) stableCount++;
            else
            {
                stableCandidate = candidate;
                stableMethod = extraction.Method;
                stableCount = 1;
            }

            if (stableCount >= 2 || stableMethod == "copy-button-clipboard")
            {
                _lastResult = candidate;
                _diagnostic?.Invoke($"{Name}: 결과 읽기 성공 ({stableMethod}, {started.ElapsedMilliseconds}ms)");
                return candidate;
            }
        }

        throw new TimeoutException(
            $"{Name} 페이지에는 번역이 보이지만 {_timeoutMs / 1000.0:0.#}초 안에 결과를 읽지 못했습니다. " +
            "DOM/접근성/본문/복사버튼 폴백을 모두 시도했습니다. '번역 결과 읽기 테스트' 로그를 확인하세요.");
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static bool Equivalent(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
}
