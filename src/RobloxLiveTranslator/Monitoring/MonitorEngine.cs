using System.Drawing;
using System.Drawing.Imaging;
using RobloxLiveTranslator.Models;
using RobloxLiveTranslator.Services;
using RobloxLiveTranslator.Translation;

namespace RobloxLiveTranslator.Monitoring;

public sealed class MonitorEngine : IAsyncDisposable
{
    private const int NormalQueueLimit = 4;
    private const int EventQueueLimit = 12;

    private readonly IntPtr _hwnd;
    private readonly AppSettings _settings;
    private readonly WindowCaptureService _capture;
    private readonly IOcrService _ocr;
    private readonly MultiTranslator _translator;
    private readonly Dictionary<Guid, RoiState> _states = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _lastCaptureMethod = string.Empty;
    private DateTimeOffset _lastCaptureFailureNotice = DateTimeOffset.MinValue;

    private sealed class RoiState
    {
        public readonly object Sync = new();
        public byte[]? Signature;
        public bool Dirty = true;
        public DateTimeOffset DirtySince = DateTimeOffset.MinValue;
        public DateTimeOffset LastOcrAt = DateTimeOffset.MinValue;
        public string LastText = "";
        public DateTimeOffset LastAcceptedAt = DateTimeOffset.MinValue;

        // A transient message can disappear before the ROI becomes visually stable. Keep the
        // most text-like bitmap seen during the visual burst and OCR that snapshot later.
        public Bitmap? CandidateSnapshot;
        public double CandidateScore = double.MinValue;
        public DateTimeOffset CandidateCapturedAt = DateTimeOffset.MinValue;

        // Translation is a small bounded FIFO, not an unbounded queue and not latest-wins.
        // This preserves short event/admin messages while preventing a busy ROI from growing forever.
        public Queue<TranslationWorkItem> TranslationQueue { get; } = new();
        public string InFlightText = "";
        public CancellationTokenSource? TranslationCts;
        public Task? TranslationTask;
    }

    private sealed record TranslationWorkItem(
        RoiDefinition Roi,
        string OriginalText,
        string TranslationInput,
        float Confidence,
        string SourceLanguage,
        string TargetLanguage,
        DateTimeOffset CapturedAt);

    public event Action<RoiTranslationUpdate>? TranslationUpdated;
    public event Action<RoiTranslationPending>? TranslationPending;
    public event Action<Guid>? TranslationCleared;
    public event Action<string>? Status;

    public MonitorEngine(IntPtr hwnd, AppSettings settings, WindowCaptureService capture,
        IOcrService ocr, MultiTranslator translator)
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
                state.TranslationQueue.Clear();
                state.CandidateSnapshot?.Dispose();
                state.CandidateSnapshot = null;
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
        var interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.PollIntervalMs, 80, 2000));
        while (!ct.IsCancellationRequested)
        {
            var cycleStart = DateTime.UtcNow;
            try
            {
                using var frame = _capture.CaptureClient(_hwnd);
                if (frame is null)
                {
                    var now = DateTimeOffset.Now;
                    if (now - _lastCaptureFailureNotice >= TimeSpan.FromSeconds(5))
                    {
                        _lastCaptureFailureNotice = now;
                        Status?.Invoke("대상 창 프레임을 얻지 못했습니다. 전면 게임이면 캡처 방식을 '자동'으로 사용하세요. 비활성 GPU 게임은 Windows가 백그라운드 프레임을 제공하지 않을 수 있습니다.");
                    }
                }
                else
                {
                    if (!string.Equals(_lastCaptureMethod, _capture.LastMethod, StringComparison.Ordinal))
                    {
                        _lastCaptureMethod = _capture.LastMethod;
                        Status?.Invoke($"캡처 정상: {_capture.LastMethod} / {frame.Width}x{frame.Height}");
                    }

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
        var changed = difference >= _settings.ChangeThreshold;
        if (changed)
        {
            if (!state.Dirty || state.DirtySince == DateTimeOffset.MinValue)
            {
                state.DirtySince = now;
                ResetCandidate(state);
            }

            state.Dirty = true;
            KeepBestSnapshot(state, crop, now);
        }

        var forceDue = state.LastOcrAt == DateTimeOffset.MinValue ||
                       now - state.LastOcrAt >= TimeSpan.FromSeconds(Math.Clamp(_settings.ForceOcrSeconds, 2, 120));
        var settled = state.Dirty && !changed &&
                      now - state.DirtySince >= TimeSpan.FromMilliseconds(Math.Clamp(_settings.SettleMs, 40, 1000));

        // Do not wait forever for a fast animated message to become stable. OCR the best frame
        // captured in this visual burst after a short bounded window even if it already disappeared.
        var burstLimitMs = roi.EventMode
            ? Math.Clamp(_settings.SettleMs + 80, 100, 260)
            : Math.Clamp(_settings.SettleMs * 2 + 80, 160, 420);
        var burstDue = state.Dirty && now - state.DirtySince >= TimeSpan.FromMilliseconds(burstLimitMs);

        if (!settled && !burstDue && !forceDue) return;

        var fromVisualBurst = state.Dirty;
        var capturedAt = state.CandidateCapturedAt == DateTimeOffset.MinValue ? now : state.CandidateCapturedAt;
        Bitmap ocrFrame;
        if (fromVisualBurst && state.CandidateSnapshot is not null)
        {
            ocrFrame = state.CandidateSnapshot;
            state.CandidateSnapshot = null;
        }
        else
        {
            ocrFrame = (Bitmap)crop.Clone();
        }

        state.CandidateScore = double.MinValue;
        state.CandidateCapturedAt = DateTimeOffset.MinValue;
        state.Dirty = false;
        state.DirtySince = DateTimeOffset.MinValue;
        state.LastOcrAt = now;

        try
        {
            var sourceLanguage = string.IsNullOrWhiteSpace(roi.SourceLanguageOverride)
                ? TranslationLanguages.Normalize(_settings.SourceLanguage)
                : TranslationLanguages.Normalize(roi.SourceLanguageOverride);
            var languages = !string.IsNullOrWhiteSpace(roi.OcrLanguagesOverride)
                ? roi.OcrLanguagesOverride!
                : _settings.OcrLanguageFollowsSource && sourceLanguage != "auto"
                    ? TranslationLanguages.ToTesseract(sourceLanguage)
                    : _settings.OcrLanguages;

            var result = await _ocr.ReadAsync(ocrFrame, languages, monitorToken, fastPath: roi.EventMode);

            // Empty OCR after a disappearance must NOT clear/cancel a previously captured sentence.
            // A short event may already be translating from the retained snapshot.
            if (result.Confidence < 0.18f || !LooksLikeText(result.Text)) return;

            var text = TesseractOcrService.Normalize(result.Text);
            if (LooksLikeRoiLingoSelfCapture(text))
            {
                Status?.Invoke($"{roi.Name}: RoiLingo 자체 화면이 캡처되어 OCR 결과를 버렸습니다.");
                return;
            }
            if (string.IsNullOrWhiteSpace(text)) return;

            lock (state.Sync)
            {
                if (!string.IsNullOrEmpty(state.LastText) && TextSimilarity.Ratio(state.LastText, text) >= 0.94)
                {
                    // A static sentence is skipped on periodic force-OCR. If the ROI really changed and
                    // the same message reappeared later, allow it after a short duplicate-suppression gap.
                    if (!fromVisualBurst || now - state.LastAcceptedAt < TimeSpan.FromSeconds(1.2))
                        return;
                }
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
                    state.LastAcceptedAt = now;
                }
                if (prepared.Reason == "already-target-language")
                    Status?.Invoke($"{roi.Name}: 이미 {TranslationLanguages.DisplayName(target)} 문장이라 번역을 생략했습니다.");
                return;
            }

            if (prepared.MixedTargetAndForeign && !string.Equals(prepared.TranslationInput, text, StringComparison.Ordinal))
                Status?.Invoke($"{roi.Name}: 혼합 언어 감지 - 이미 {TranslationLanguages.DisplayName(target)}인 부분은 제외하고 나머지만 번역합니다.");

            EnqueueTranslation(roi, state, text, prepared.TranslationInput, result.Confidence,
                sourceLanguage, target, capturedAt, monitorToken);
        }
        finally
        {
            ocrFrame.Dispose();
        }
    }

    private void EnqueueTranslation(
        RoiDefinition roi,
        RoiState state,
        string originalText,
        string translationInput,
        float confidence,
        string sourceLanguage,
        string targetLanguage,
        DateTimeOffset capturedAt,
        CancellationToken monitorToken)
    {
        bool startWorker = false;
        bool dropped = false;
        lock (state.Sync)
        {
            state.LastText = originalText;
            state.LastAcceptedAt = DateTimeOffset.Now;

            var duplicateInFlight = !string.IsNullOrWhiteSpace(state.InFlightText) &&
                                    TextSimilarity.Ratio(state.InFlightText, originalText) >= 0.96;
            var duplicateQueued = state.TranslationQueue.Any(x => TextSimilarity.Ratio(x.OriginalText, originalText) >= 0.96);
            if (duplicateInFlight || duplicateQueued) return;

            var limit = roi.EventMode ? EventQueueLimit : NormalQueueLimit;
            if (!roi.EventMode && state.TranslationQueue.Count > 0)
            {
                // For a normal chat/UI ROI, an old sentence is less useful than the current one.
                // Keep the in-flight request, but replace queued stale work with the newest OCR.
                state.TranslationQueue.Clear();
            }
            while (state.TranslationQueue.Count >= limit)
            {
                state.TranslationQueue.Dequeue();
                dropped = true;
            }

            state.TranslationQueue.Enqueue(new TranslationWorkItem(
                roi, originalText, translationInput, confidence, sourceLanguage, targetLanguage, capturedAt));

            if (state.TranslationTask is null || state.TranslationTask.IsCompleted)
            {
                state.TranslationCts?.Dispose();
                state.TranslationCts = CancellationTokenSource.CreateLinkedTokenSource(monitorToken);
                startWorker = true;
            }
        }

        TranslationPending?.Invoke(new RoiTranslationPending(
            roi.Id, roi.Name, originalText, confidence, capturedAt));

        if (dropped)
            Status?.Invoke($"{roi.Name}: 순간 문구가 매우 빠르게 들어와 오래된 대기 항목 1개를 정리했습니다. 최신 { (roi.EventMode ? EventQueueLimit : NormalQueueLimit) }개는 유지합니다.");

        if (startWorker)
        {
            lock (state.Sync)
            {
                if (state.TranslationTask is null || state.TranslationTask.IsCompleted)
                {
                    var token = state.TranslationCts!.Token;
                    state.TranslationTask = Task.Run(() => RunTranslationWorkerAsync(state, token), token);
                }
            }
        }
    }

    private async Task RunTranslationWorkerAsync(RoiState state, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TranslationWorkItem? work;
            lock (state.Sync)
            {
                if (state.TranslationQueue.Count == 0)
                {
                    state.InFlightText = string.Empty;
                    state.TranslationTask = null;
                    return;
                }

                work = state.TranslationQueue.Dequeue();
                state.InFlightText = work.OriginalText;
            }

            try
            {
                var bundle = await _translator.TranslateAsync(work.TranslationInput, work.SourceLanguage, work.TargetLanguage, ct);
                ct.ThrowIfCancellationRequested();

                var failed = bundle.Results.Count == 0 ||
                             bundle.SelectedProvider.Equals("실패", StringComparison.OrdinalIgnoreCase) ||
                             bundle.SelectedProvider.Equals("없음", StringComparison.OrdinalIgnoreCase);

                if (!failed)
                {
                    // Publish every captured unique message. The bitmap may have disappeared long ago;
                    // CapturedAt preserves the moment it was actually seen for history/event logs.
                    TranslationUpdated?.Invoke(new RoiTranslationUpdate(
                        work.Roi.Id, work.Roi.Name, work.OriginalText, bundle.SelectedText, bundle.SelectedProvider,
                        work.Confidence, bundle.AgreementScore, work.CapturedAt, bundle.Results));
                }
                else
                {
                    Status?.Invoke($"{work.Roi.Name}: 캡처한 문구의 번역 결과를 얻지 못했습니다. OCR 원문은 실행 로그에 남아 있습니다.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Status?.Invoke($"{work.Roi.Name}: 캡처한 문구 번역 오류 - {ex.Message}. OCR 원문은 실행 로그에 남아 있습니다.");
            }
            finally
            {
                lock (state.Sync)
                {
                    if (work is not null && string.Equals(state.InFlightText, work.OriginalText, StringComparison.Ordinal))
                        state.InFlightText = string.Empty;
                }
            }
        }
    }

    private static void KeepBestSnapshot(RoiState state, Bitmap crop, DateTimeOffset capturedAt)
    {
        var score = RoiChangeDetector.TextLikelihood(crop);
        if (state.CandidateSnapshot is not null && score <= state.CandidateScore * 1.05)
            return;

        state.CandidateSnapshot?.Dispose();
        state.CandidateSnapshot = (Bitmap)crop.Clone();
        state.CandidateScore = score;
        state.CandidateCapturedAt = capturedAt;
    }

    private static void ResetCandidate(RoiState state)
    {
        state.CandidateSnapshot?.Dispose();
        state.CandidateSnapshot = null;
        state.CandidateScore = double.MinValue;
        state.CandidateCapturedAt = DateTimeOffset.MinValue;
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
