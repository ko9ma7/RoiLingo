using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace RobloxLiveTranslator.Translation.Web;

/// <summary>
/// WebView2 translator used by the no-API-key mode.
/// Each request navigates to an explicit source/target/text deep-link first.  This is intentionally
/// more conservative than mutating a long-lived SPA editor because the target language must never
/// leak from a previous request. DOM injection is retained only as a recovery path.
/// </summary>
public sealed class WebViewTranslationProvider : ITranslationProvider
{
    private static readonly SemaphoreSlim ClipboardGate = new(1, 1);
    private readonly WebView2 _webView;
    private readonly Func<string, string, string, string> _urlBuilder;
    private readonly string _resultScript;
    private readonly int _timeoutMs;
    private readonly bool _allowClipboardFallback;
    private readonly Action<string>? _diagnostic;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<Task>? _ensureReady;

    public string Name { get; }
    public TranslationProviderKind Kind => TranslationProviderKind.Web;
    public bool IsConfigured { get; }

    public WebViewTranslationProvider(
        string name,
        WebView2 webView,
        Func<string, string, string, string> urlBuilder,
        string resultScript,
        bool enabled,
        int timeoutMs,
        bool allowClipboardFallback,
        Action<string>? diagnostic = null,
        Func<Task>? ensureReady = null)
    {
        Name = name;
        _webView = webView;
        _urlBuilder = urlBuilder;
        _resultScript = resultScript;
        IsConfigured = enabled;
        _timeoutMs = Math.Clamp(timeoutMs, 4000, 25000);
        _allowClipboardFallback = allowClipboardFallback;
        _diagnostic = diagnostic;
        _ensureReady = ensureReady;
    }

    public async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException($"{Name} is disabled.");
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (text.Length > 1800) text = text[..1800];

        sourceLanguage = TranslationLanguages.Normalize(sourceLanguage);
        targetLanguage = TranslationLanguages.Normalize(targetLanguage, false);
        if (sourceLanguage != "auto" && sourceLanguage.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase))
            return text;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_webView.Dispatcher.CheckAccess())
                return await TranslateOnUiThreadAsync(text, sourceLanguage, targetLanguage, cancellationToken);

            var op = _webView.Dispatcher.InvokeAsync(
                () => TranslateOnUiThreadAsync(text, sourceLanguage, targetLanguage, cancellationToken));
            return await op.Task.Unwrap();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> TranslateOnUiThreadAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (_webView.CoreWebView2 is null)
        {
            if (_ensureReady is not null) await _ensureReady();
            else await _webView.EnsureCoreWebView2Async();
        }
        var core = _webView.CoreWebView2 ?? throw new InvalidOperationException($"{Name} WebView2 초기화에 실패했습니다.");
        await WebCopyBridge.EnsureInstalledAsync(core);

        // The target language is encoded on EVERY request. This prevents a manually changed translator
        // tab (for example Chinese) from contaminating a Korean translation request.
        var requestUrl = _urlBuilder(text, sourceLanguage, targetLanguage);
        await NavigateAsync(core, requestUrl, cancellationToken);

        var sourceReady = await WaitForSourceAsync(core, text, cancellationToken);
        if (!sourceReady)
        {
            sourceReady = await WebInputInjector.EnsureSourceTextAsync(core, Name, text, cancellationToken);
            if (sourceReady)
                _diagnostic?.Invoke($"{Name}: 딥링크 입력 확인 실패 → 실제 입력칸 직접 입력으로 복구");
        }
        else
        {
            _diagnostic?.Invoke($"{Name}: 원문 전달 확인 / 목표 {TranslationLanguages.DisplayName(targetLanguage)}");
        }

        if (!sourceReady)
            throw new InvalidOperationException($"{Name}: 원문 입력을 확인하지 못했습니다. 사이트 구조가 변경되었을 수 있습니다.");

        var clipboardBefore = _allowClipboardFallback ? WebResultExtractor.ReadClipboardText() : string.Empty;
        var started = Stopwatch.StartNew();
        string stableText = string.Empty;
        string stableMethod = "none";
        var stableCount = 0;
        var copyBridgeTried = false;
        var clipboardTried = false;
        var visualTried = false;

        while (started.ElapsedMilliseconds < _timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(240, cancellationToken);
            var elapsed = started.ElapsedMilliseconds;

            // Do not extract anything unless the source sentence is still present. This single guard
            // eliminates most false positives from language menus, banners and browser chrome.
            if (!await WebInputInjector.IsSourcePresentAsync(core, text))
            {
                if (elapsed < 3500)
                {
                    var recovered = await WebInputInjector.EnsureSourceTextAsync(core, Name, text, cancellationToken);
                    if (recovered) _diagnostic?.Invoke($"{Name}: SPA 갱신 후 원문 재입력 성공");
                }
                continue;
            }

            var extraction = await WebResultExtractor.TryExtractAsync(
                core, _resultScript, text, targetLanguage, Name, includeAccessibility: false);
            var candidate = Normalize(extraction.Text);

            if (string.IsNullOrWhiteSpace(candidate) && !copyBridgeTried && elapsed >= 1400)
            {
                copyBridgeTried = true;
                var bridge = Normalize(await WebCopyBridge.CaptureFromTargetCopyButtonAsync(core, cancellationToken));
                if (WebResultExtractor.IsTranslationCandidate(bridge, text, targetLanguage))
                {
                    candidate = bridge;
                    extraction = (bridge, "copy-bridge");
                }
            }

            if (string.IsNullOrWhiteSpace(candidate) && _allowClipboardFallback && !clipboardTried && elapsed >= 2100)
            {
                clipboardTried = true;
                await ClipboardGate.WaitAsync(cancellationToken);
                try
                {
                    var copied = await WebResultExtractor.TryCopyButtonClipboardAsync(
                        core, text, targetLanguage, clipboardBefore);
                    var copiedText = Normalize(copied.Text);
                    if (WebResultExtractor.IsTranslationCandidate(copiedText, text, targetLanguage))
                    {
                        candidate = copiedText;
                        extraction = copied;
                    }
                }
                finally
                {
                    ClipboardGate.Release();
                }
            }

            if (string.IsNullOrWhiteSpace(candidate) && !visualTried && elapsed >= 3800)
            {
                visualTried = true;
                var visual = Normalize(await WebVisualOcrFallback.TryReadAsync(
                    core, Name, text, targetLanguage, cancellationToken));
                if (WebResultExtractor.IsTranslationCandidate(visual, text, targetLanguage))
                {
                    candidate = visual;
                    extraction = (visual, "webview-visual-ocr");
                }
            }

            if (!WebResultExtractor.IsTranslationCandidate(candidate, text, targetLanguage))
                continue;

            if (Equivalent(candidate, stableText)) stableCount++;
            else
            {
                stableText = candidate;
                stableMethod = extraction.Method;
                stableCount = 1;
            }

            var direct = stableMethod is "known-selector" or "copy-bridge" or "copy-button-clipboard";
            if (direct || stableCount >= 2)
            {
                _diagnostic?.Invoke($"{Name}: 결과 읽기 성공 ({stableMethod}, {elapsed}ms) → {TranslationLanguages.DisplayName(targetLanguage)}");
                return candidate;
            }
        }

        throw new TimeoutException(
            $"{Name}: 원문은 입력했지만 {_timeoutMs / 1000.0:0.#}초 안에 {TranslationLanguages.DisplayName(targetLanguage)} 번역 결과를 확인하지 못했습니다.");
    }

    private static async Task<bool> WaitForSourceAsync(
        CoreWebView2 core,
        string text,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < 14; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await WebInputInjector.IsSourcePresentAsync(core, text)) return true;
            await Task.Delay(i < 5 ? 180 : 260, cancellationToken);
        }
        return false;
    }

    private static async Task NavigateAsync(CoreWebView2 core, string url, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, CoreWebView2NavigationCompletedEventArgs e) => completion.TrySetResult(e.IsSuccess);
        core.NavigationCompleted += Handler;
        try
        {
            core.Navigate(url);
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            var finished = await Task.WhenAny(completion.Task, Task.Delay(6500, cancellationToken));
            if (finished == completion.Task) _ = await completion.Task;
            await WaitForDomReadyAsync(core, cancellationToken);
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }

    private static async Task WaitForDomReadyAsync(CoreWebView2 core, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 20; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var state = await core.ExecuteScriptAsync("document.readyState");
                if (state.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
                    state.Contains("interactive", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(300, cancellationToken);
                    return;
                }
            }
            catch (InvalidOperationException) { }
            await Task.Delay(160, cancellationToken);
        }
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
