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
        await WebCopyBridge.EnsureInstalledAsync(core);
        var clipboardBefore = _allowClipboardFallback ? WebResultExtractor.ReadClipboardText() : string.Empty;

        // Navigate with the site's deep link first. Some translator sites intermittently ignore
        // query-string source text after a SPA/UI update, so v1.6 verifies the source editor and
        // injects the OCR text through the real input element as a fallback. This restores the
        // visible "OCR text -> web translator" step even when the deep link stops populating.
        core.Navigate(_urlBuilder(text, targetLanguage));
        await Task.Delay(650, cancellationToken);
        var sourceReady = await WebInputInjector.EnsureSourceTextAsync(core, Name, text, cancellationToken);
        _diagnostic?.Invoke($"{Name}: 원문 전달 {(sourceReady ? "확인" : "재시도 예정")}");

        var started = Stopwatch.StartNew();
        var sourceReinjected = false;
        string? stableCandidate = null;
        string stableMethod = "none";
        var stableCount = 0;
        long lastAccessibilityProbeMs = -10000;
        var copyAttempted = false;
        var copyBridgeAttempts = 0;
        var visualOcrAttempted = false;

        while (started.ElapsedMilliseconds < _timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(240, cancellationToken);

            var elapsedMs = started.ElapsedMilliseconds;

            // A navigation may finish after our first injection attempt. Verify once more after the
            // page has settled instead of silently waiting on an empty source editor.
            if (!sourceReady && !sourceReinjected && elapsedMs >= 900)
            {
                sourceReinjected = true;
                sourceReady = await WebInputInjector.EnsureSourceTextAsync(core, Name, text, cancellationToken);
                _diagnostic?.Invoke($"{Name}: 원문 재전달 {(sourceReady ? "성공" : "실패")}");
            }

            var useAccessibility = elapsedMs >= 1700 && elapsedMs - lastAccessibilityProbeMs >= 1000;
            if (useAccessibility) lastAccessibilityProbeMs = elapsedMs;
            var extraction = await WebResultExtractor.TryExtractAsync(core, _resultScript, text, targetLanguage, Name, useAccessibility);
            var candidate = Normalize(extraction.Text);

            var lowConfidenceMethod = extraction.Method is "visible-dom-snapshot" or "dom-heuristic" or "body-text" or "accessibility-tree";

            // First try an in-page copy bridge. When Papago/Google/DeepL's Copy button calls
            // navigator.clipboard.writeText, WebView2 receives the exact translated string before
            // the OS clipboard permission/path can fail. This is substantially more robust than CSS selectors.
            var bridgeDue = copyBridgeAttempts == 0 ? elapsedMs >= 1250 : elapsedMs >= 3000;
            if ((string.IsNullOrWhiteSpace(candidate) || lowConfidenceMethod) &&
                copyBridgeAttempts < 2 && bridgeDue)
            {
                copyBridgeAttempts++;
                var bridgeText = Normalize(await WebCopyBridge.CaptureFromTargetCopyButtonAsync(core, cancellationToken));
                if (WebResultExtractor.IsTranslationCandidate(bridgeText, text, targetLanguage))
                {
                    candidate = bridgeText;
                    extraction = (bridgeText, "copy-bridge");
                }
            }

            // If the user can visibly see a translation but DOM/copy extraction still fails,
            // OCR the rendered WebView's target pane. This is slower, therefore only once/request.
            if (!visualOcrAttempted && elapsedMs >= 3200 &&
                (string.IsNullOrWhiteSpace(candidate) || lowConfidenceMethod))
            {
                visualOcrAttempted = true;
                var visual = Normalize(await WebVisualOcrFallback.TryReadAsync(
                    core, Name, text, targetLanguage, cancellationToken));
                if (WebResultExtractor.IsTranslationCandidate(visual, text, targetLanguage))
                {
                    candidate = visual;
                    extraction = (visual, "webview-visual-ocr");
                }
            }

            // Last clipboard fallback for sites that do not use navigator.clipboard.writeText.
            if (_allowClipboardFallback && elapsedMs >= 1800 && !copyAttempted &&
                (string.IsNullOrWhiteSpace(candidate) || lowConfidenceMethod))
            {
                copyAttempted = true;
                await ClipboardGate.WaitAsync(cancellationToken);
                try
                {
                    var copied = await WebResultExtractor.TryCopyButtonClipboardAsync(core, text, targetLanguage, clipboardBefore);
                    var copiedCandidate = Normalize(copied.Text);
                    if (!string.IsNullOrWhiteSpace(copiedCandidate))
                    {
                        candidate = copiedCandidate;
                        extraction = copied;
                    }
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

            var exactFallback = stableMethod is "copy-bridge" or "copy-button-clipboard" or "webview-visual-ocr";
            var highConfidence = stableMethod is "known-selector" or "text-node-geometry" or "layout-anchor" or
                                 "copy-bridge" or "copy-button-clipboard" or "webview-visual-ocr";
            var lowConfidenceSettled = stableCount >= 2 && started.ElapsedMilliseconds >= 1800 &&
                                       (!_allowClipboardFallback || copyAttempted);
            if (exactFallback || (stableCount >= 2 && highConfidence) || lowConfidenceSettled)
            {
                _lastResult = candidate;
                _diagnostic?.Invoke($"{Name}: 결과 읽기 성공 ({stableMethod}, {started.ElapsedMilliseconds}ms)");
                return candidate;
            }
        }

        throw new TimeoutException(
            $"{Name} 페이지에는 번역이 보이지만 {_timeoutMs / 1000.0:0.#}초 안에 결과를 읽지 못했습니다. " +
            "DOM/텍스트노드/접근성/복사브리지/클립보드/화면 OCR 폴백을 모두 시도했습니다. '번역 결과 읽기 테스트' 로그를 확인하세요.");
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
