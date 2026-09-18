using System.Drawing;
using System.Drawing.Imaging;
using RobloxLiveTranslator.Models;
using RobloxLiveTranslator.Services;
using RobloxLiveTranslator.Translation;

namespace RobloxLiveTranslator.Monitoring;

public sealed class MonitorEngine : IAsyncDisposable
{
    private readonly IntPtr _hwnd;
    private readonly AppSettings _settings;
    private readonly WindowCaptureService _capture;
    private readonly TesseractOcrService _ocr;
    private readonly MultiTranslator _translator;
    private readonly Dictionary<Guid, RoiState> _states = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private sealed class RoiState
    {
        public readonly object Sync = new();
        public byte[]? Signature;
        public bool Dirty = true;
        public DateTimeOffset DirtySince = DateTimeOffset.MinValue;
        public DateTimeOffset LastOcrAt = DateTimeOffset.MinValue;
        public string LastText = "";
        public long Revision;
        public TranslationWorkItem? Pending;
        public CancellationTokenSource? TranslationCts;
        public Task? TranslationTask;
    }

    private sealed record TranslationWorkItem(
        long Revision, RoiDefinition Roi, string OriginalText, string TranslationInput, float Confidence, string SourceLanguage, string TargetLanguage);

    public event Action<RoiTranslationUpdate>? TranslationUpdated;
    public event Action<RoiTranslationPending>? TranslationPending;
    public event Action<Guid>? TranslationCleared;
    public event Action<string>? Status;

    public MonitorEngine(IntPtr hwnd, AppSettings settings, WindowCaptureService capture,
        TesseractOcrService ocr, MultiTranslator translator)
    {
        _hwnd = hwnd;
        _settings = settings;
        _capture = capture;
        _ocr = ocr;
        _translator = translator;
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null || _loop is null) return;

        _cts.Cancel();
        try { await _loop; } catch (OperationCanceledException) { }

        var pending = new List<Task>();
        foreach (var state in _states.Values)
        {
            CancellationTokenSource? cts;
            Task? task;
            lock (state.Sync)
            {
                cts = state.TranslationCts;
                task = state.TranslationTask;
            }
            cts?.Cancel();
            if (task is not null) pending.Add(task);
        }

        if (pending.Count > 0)
        {
            try { await Task.WhenAll(pending); }
            catch (OperationCanceledException) { }
            catch { /* individual translation errors are already reported through Status */ }
        }

        foreach (var state in _states.Values)
        {
            lock (state.Sync)
            {
                state.TranslationCts?.Dispose();
                state.TranslationCts = null;
                state.TranslationTask = null;
            }
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.PollIntervalMs, 100, 2000));
        while (!ct.IsCancellationRequested)
        {
            var cycleStart = DateTime.UtcNow;
            try
            {
                using var frame = _capture.CaptureClient(_hwnd);
                if (frame is null)
                {
                    Status?.Invoke("대상 창 캡처 대기 중: 가려진/비활성 창은 백그라운드 캡처를 시도합니다. 최소화 또는 GPU 보호 창은 캡처가 제한될 수 있습니다.");
                }
                else
                {
                    // OCR is intentionally kept in the capture loop, but translation is NOT awaited here.
                    // Each ROI owns one latest-wins translation task, so a slow web translator cannot block
                    // capture/OCR for the remaining ROIs and stale text cannot build an unbounded queue.
                    foreach (var roi in _settings.Rois.Where(r => r.Enabled).OrderByDescending(r => r.EventMode).ToArray())
                    {
                        ct.ThrowIfCancellationRequested();
                        await ProcessRoiAsync(frame, roi, ct);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Status?.Invoke($"감시 오류: {ex.Message}");
            }

            var spent = DateTime.UtcNow - cycleStart;
            var delay = interval - spent;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
        }
    }

    private async Task ProcessRoiAsync(Bitmap frame, RoiDefinition roi, CancellationToken monitorToken)
    {
        using var crop = Crop(frame, roi);
        if (crop is null) return;

        if (!_states.TryGetValue(roi.Id, out var state))
            _states[roi.Id] = state = new RoiState();

        var signature = RoiChangeDetector.ComputeSignature(crop);
        var difference = RoiChangeDetector.Difference(state.Signature, signature);
        state.Signature = signature;

        var now = DateTimeOffset.Now;
        if (difference >= _settings.ChangeThreshold)
        {
            if (!state.Dirty) state.DirtySince = now;
            if (state.DirtySince == DateTimeOffset.MinValue) state.DirtySince = now;
            state.Dirty = true;
            return;
        }

        var forceDue = state.LastOcrAt == DateTimeOffset.MinValue ||
                       now - state.LastOcrAt >= TimeSpan.FromSeconds(Math.Clamp(_settings.ForceOcrSeconds, 2, 120));
        var settled = state.Dirty && now - state.DirtySince >= TimeSpan.FromMilliseconds(Math.Clamp(_settings.SettleMs, 50, 1000));
        if (!settled && !forceDue) return;

        var wasSettledVisualChange = settled;
        state.Dirty = false;
        state.DirtySince = DateTimeOffset.MinValue;
        state.LastOcrAt = now;

        var sourceLanguage = string.IsNullOrWhiteSpace(roi.SourceLanguageOverride)
            ? TranslationLanguages.Normalize(_settings.SourceLanguage)
            : TranslationLanguages.Normalize(roi.SourceLanguageOverride);
        var languages = !string.IsNullOrWhiteSpace(roi.OcrLanguagesOverride)
            ? roi.OcrLanguagesOverride!
            : _settings.OcrLanguageFollowsSource && sourceLanguage != "auto"
                ? TranslationLanguages.ToTesseract(sourceLanguage)
                : _settings.OcrLanguages;
        var result = await _ocr.ReadAsync(crop, languages, monitorToken, fastPath: roi.EventMode);

        // A stable visual change that no longer contains readable text means the previous
        // on-screen sentence disappeared. Clear it instead of leaving a stale translation forever.
        if (result.Confidence < 0.20f || !LooksLikeText(result.Text))
        {
            if (wasSettledVisualChange && HasLastText(state))
                ClearRoi(roi, state);
            return;
        }

        var text = TesseractOcrService.Normalize(result.Text);
        if (LooksLikeRoiLingoSelfCapture(text))
        {
            if (HasLastText(state)) ClearRoi(roi, state);
            Status?.Invoke($"{roi.Name}: RoiLingo 자체 화면이 캡처되어 OCR 결과를 버렸습니다. 오버레이/설정 창은 캡처 제외 처리되며, 필요하면 대상 창과 RoiLingo 창이 겹치지 않는지 확인하세요.");
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            if (wasSettledVisualChange && HasLastText(state))
                ClearRoi(roi, state);
            return;
        }

        lock (state.Sync)
        {
            if (!string.IsNullOrEmpty(state.LastText) && TextSimilarity.Ratio(state.LastText, text) >= 0.94)
                return;
        }

        var target = string.IsNullOrWhiteSpace(roi.TargetLanguageOverride)
            ? TranslationLanguages.Normalize(_settings.TargetLanguage, false)
            : TranslationLanguages.Normalize(roi.TargetLanguageOverride, false);

        var prepared = MixedLanguageTextProcessor.Prepare(text, target, _settings.SmartMixedText);
        if (prepared.SkipTranslation)
        {
            lock (state.Sync)
            {
                state.LastText = text;
                state.Revision++;
                state.Pending = null;
            }
            TranslationCleared?.Invoke(roi.Id);
            if (prepared.Reason == "already-target-language")
                Status?.Invoke($"{roi.Name}: 이미 {TranslationLanguages.DisplayName(target)} 문장이라 번역을 생략했습니다.");
            return;
        }

        if (prepared.MixedTargetAndForeign && !string.Equals(prepared.TranslationInput, text, StringComparison.Ordinal))
            Status?.Invoke($"{roi.Name}: 혼합 언어 감지 - 이미 {TranslationLanguages.DisplayName(target)}인 부분은 제외하고 나머지만 번역합니다.");

        StartLatestTranslation(roi, state, text, prepared.TranslationInput, result.Confidence, sourceLanguage, target, monitorToken);
    }

    private void StartLatestTranslation(
        RoiDefinition roi,
        RoiState state,
        string originalText,
        string translationInput,
        float confidence,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken monitorToken)
    {
        long revision;
        lock (state.Sync)
        {
            state.LastText = originalText;
            revision = ++state.Revision;
            state.Pending = new TranslationWorkItem(revision, roi, originalText, translationInput, confidence, sourceLanguage, targetLanguage);

            // Important: do not cancel a WebView translation merely because OCR produced a newer
            // sentence. Cancelling during navigation/input was the reason the translator page could
            // remain blank in v1.5. One worker per ROI finishes the current request, discards it if
            // stale, then processes only the newest pending sentence. Queue length is therefore <= 1.
            if (state.TranslationTask is null || state.TranslationTask.IsCompleted)
            {
                state.TranslationCts?.Dispose();
                state.TranslationCts = CancellationTokenSource.CreateLinkedTokenSource(monitorToken);
                var workerToken = state.TranslationCts.Token;
                state.TranslationTask = Task.Run(() => RunTranslationWorkerAsync(state, workerToken), workerToken);
            }
        }

        // Hide the previous sentence immediately; the pending indicator is not written into the
        // game overlay itself, so stale text disappears while the newest sentence is translated.
        TranslationPending?.Invoke(new RoiTranslationPending(
            roi.Id, roi.Name, originalText, confidence, DateTimeOffset.Now));
    }

    private async Task RunTranslationWorkerAsync(RoiState state, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TranslationWorkItem? work;
            lock (state.Sync)
            {
                work = state.Pending;
                state.Pending = null;
                if (work is null)
                {
                    state.TranslationTask = null;
                    return;
                }
            }

            try
            {
                var bundle = await _translator.TranslateAsync(work.TranslationInput, work.SourceLanguage, work.TargetLanguage, ct);
                ct.ThrowIfCancellationRequested();

                var failed = bundle.Results.Count == 0 ||
                             bundle.SelectedProvider.Equals("실패", StringComparison.OrdinalIgnoreCase) ||
                             bundle.SelectedProvider.Equals("없음", StringComparison.OrdinalIgnoreCase);

                var publish = false;
                lock (state.Sync)
                {
                    // A newer OCR sentence may have arrived while the web site was translating.
                    // Never show/history the old result, but also never interrupt the web input
                    // half-way through. The worker simply moves on to the newest pending item.
                    if (work.Revision == state.Revision &&
                        string.Equals(state.LastText, work.OriginalText, StringComparison.Ordinal))
                    {
                        if (failed)
                            state.LastText = string.Empty; // allow retry on a still-visible sentence
                        else
                            publish = true;
                    }
                }

                if (publish)
                {
                    TranslationUpdated?.Invoke(new RoiTranslationUpdate(
                        work.Roi.Id, work.Roi.Name, work.OriginalText, bundle.SelectedText, bundle.SelectedProvider,
                        work.Confidence, bundle.AgreementScore, DateTimeOffset.Now, bundle.Results));
                }
                else if (failed && work.Revision == state.Revision)
                {
                    Status?.Invoke($"{work.Roi.Name}: 번역 결과를 얻지 못했습니다. 같은 문장이 계속 보이면 자동으로 다시 시도합니다.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var current = false;
                lock (state.Sync)
                {
                    current = work.Revision == state.Revision;
                    if (current) state.LastText = string.Empty;
                }

                if (current)
                    Status?.Invoke($"{work.Roi.Name}: 번역 오류 - {ex.Message} (같은 문장이 계속 보이면 자동 재시도)");
            }
        }
    }

    private void ClearRoi(RoiDefinition roi, RoiState state)
    {
        CancellationTokenSource? previous;
        Task? previousTask;
        lock (state.Sync)
        {
            state.LastText = string.Empty;
            state.Revision++;
            state.Pending = null;
            previousTask = state.TranslationTask;
            previous = state.TranslationCts;
            state.TranslationCts = null;
            state.TranslationTask = null;
        }

        if (previous is not null)
        {
            previous.Cancel();
            if (previousTask is null) previous.Dispose();
            else _ = previousTask.ContinueWith(_ => previous.Dispose(), TaskScheduler.Default);
        }
        TranslationCleared?.Invoke(roi.Id);
    }

    private static bool HasLastText(RoiState state)
    {
        lock (state.Sync) return !string.IsNullOrWhiteSpace(state.LastText);
    }

    private static bool LooksLikeRoiLingoSelfCapture(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant();
        string[] markers =
        [
            "roilingo", "webread", "교차일치", "translation-cache", "sha-256", "readme", "실행 로그",
            "papago web", "google web", "deepl web", "ocr=", "ocr ", "번역 가져오는 중"
        ];
        return markers.Count(marker => lower.Contains(marker, StringComparison.OrdinalIgnoreCase)) >= 2;
    }

    private static bool LooksLikeText(string text)
    {
        if (text.Length < 2) return false;
        var meaningful = text.Count(c => char.IsLetterOrDigit(c));
        return meaningful >= 2 && meaningful / (double)Math.Max(1, text.Length) >= 0.20;
    }

    private static Bitmap? Crop(Bitmap source, RoiDefinition roi)
    {
        var x = (int)Math.Round(roi.X * source.Width);
        var y = (int)Math.Round(roi.Y * source.Height);
        var w = (int)Math.Round(roi.Width * source.Width);
        var h = (int)Math.Round(roi.Height * source.Height);
        x = Math.Clamp(x, 0, Math.Max(0, source.Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, source.Height - 1));
        w = Math.Clamp(w, 1, source.Width - x);
        h = Math.Clamp(h, 1, source.Height - y);
        if (w < 4 || h < 4) return null;
        return source.Clone(new Rectangle(x, y, w, h), PixelFormat.Format32bppArgb);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
