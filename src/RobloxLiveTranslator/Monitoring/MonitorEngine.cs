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
    private readonly Dictionary<Guid, RoiState> _states = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private sealed class RoiState
    {
        public byte[]? Signature;
        public bool Dirty = true;
        public DateTimeOffset DirtySince = DateTimeOffset.MinValue;
        public DateTimeOffset LastOcrAt = DateTimeOffset.MinValue;
        public string LastText = "";
    }

    public event Action<RoiTranslationUpdate>? TranslationUpdated;
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
                    Status?.Invoke("대상 창을 캡처할 수 없습니다. 창이 최소화되었는지 확인하세요.");
                }
                else
                {
                    foreach (var roi in _settings.Rois.Where(r => r.Enabled).ToArray())
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

    private async Task ProcessRoiAsync(Bitmap frame, RoiDefinition roi, CancellationToken ct)
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

        state.Dirty = false;
        state.DirtySince = DateTimeOffset.MinValue;
        state.LastOcrAt = now;

        var languages = string.IsNullOrWhiteSpace(roi.OcrLanguagesOverride) ? _settings.OcrLanguages : roi.OcrLanguagesOverride!;
        var result = await _ocr.ReadAsync(crop, languages, ct);
        if (result.Confidence < 0.20f || !LooksLikeText(result.Text)) return;

        var text = TesseractOcrService.Normalize(result.Text);
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!string.IsNullOrEmpty(state.LastText) && TextSimilarity.Ratio(state.LastText, text) >= 0.94)
            return;

        state.LastText = text;
        var target = string.IsNullOrWhiteSpace(roi.TargetLanguageOverride) ? _settings.TargetLanguage : roi.TargetLanguageOverride!;
        var bundle = await _translator.TranslateAsync(text, target, ct);
        TranslationUpdated?.Invoke(new RoiTranslationUpdate(
            roi.Id, roi.Name, text, bundle.SelectedText, bundle.SelectedProvider,
            result.Confidence, bundle.AgreementScore, DateTimeOffset.Now, bundle.Results));
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
