using System.Collections.Concurrent;
using System.Drawing;
using System.Text.RegularExpressions;
using RobloxLiveTranslator.Models;
using Tesseract;

namespace RobloxLiveTranslator.Services;

public sealed class TesseractOcrService : IDisposable
{
    private readonly ModelManager _models;
    private readonly string _mode;
    private readonly ConcurrentDictionary<string, Lazy<TesseractEngine>> _engines = new(StringComparer.OrdinalIgnoreCase);

    public TesseractOcrService(ModelManager models, string mode = "Balanced")
    {
        _models = models;
        _mode = string.IsNullOrWhiteSpace(mode) ? "Balanced" : mode;
    }

    public async Task<OcrResult> ReadAsync(Bitmap roi, string languages, CancellationToken cancellationToken = default)
    {
        await _models.EnsureLanguagesAsync(languages, cancellationToken: cancellationToken);
        using var prepared = ImagePreprocessor.PrepareForOcr(roi);
        var bytes = ImagePreprocessor.ToPngBytes(prepared);

        return await Task.Run(() =>
        {
            var engine = _engines.GetOrAdd(languages, key => new Lazy<TesseractEngine>(() =>
            {
                var e = new TesseractEngine(_models.TessDataDirectory, key, EngineMode.LstmOnly);
                e.SetVariable("preserve_interword_spaces", "1");
                return e;
            })).Value;

            lock (engine)
            {
                using var pix = Pix.LoadFromMemory(bytes);
                var modes = _mode.Equals("Fast", StringComparison.OrdinalIgnoreCase)
                    ? new[] { PageSegMode.SparseText }
                    : _mode.Equals("Accurate", StringComparison.OrdinalIgnoreCase)
                        ? new[] { PageSegMode.SparseText, PageSegMode.Auto, PageSegMode.SingleBlock }
                        : new[] { PageSegMode.SparseText, PageSegMode.Auto };

                OcrResult best = new(string.Empty, 0);
                double bestScore = double.MinValue;
                foreach (var pageMode in modes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var page = engine.Process(pix, pageMode);
                    var text = Normalize(page.GetText());
                    var confidence = page.GetMeanConfidence();
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var meaningful = text.Count(char.IsLetterOrDigit);
                    var score = confidence * 100.0 + Math.Min(25, meaningful / 6.0);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new OcrResult(text, confidence);
                    }
                }
                return best;
            }
        }, cancellationToken);
    }

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var value = text.Replace('\r', ' ').Replace('\n', ' ');
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return value;
    }

    public void Dispose()
    {
        foreach (var lazy in _engines.Values)
            if (lazy.IsValueCreated) lazy.Value.Dispose();
        _engines.Clear();
    }
}
