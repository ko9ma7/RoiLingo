using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Web.WebView2.Core;
using RobloxLiveTranslator.Services;

namespace RobloxLiveTranslator.Translation.Web;

/// <summary>
/// Final fallback for translator sites whose DOM is intentionally/accidentally difficult to read.
/// If the user can visibly see a translation in the WebView, capture that rendered WebView and OCR
/// only the central/right target pane. It is slower than DOM extraction, so it runs at most once per request.
/// </summary>
internal static class WebVisualOcrFallback
{
    private static readonly Lazy<TesseractOcrService> Ocr = new(() =>
        new TesseractOcrService(new ModelManager(), "Fast"));

    public static string MapTesseractLanguage(string targetLanguage) => targetLanguage.ToLowerInvariant() switch
    {
        "ko" or "ko-kr" => "kor",
        "en" or "en-us" or "en-gb" => "eng",
        "ja" or "ja-jp" => "jpn",
        "zh" or "zh-cn" or "zh-hans" => "chi_sim",
        "zh-tw" or "zh-hant" => "chi_tra",
        "es" => "spa",
        "fr" => "fra",
        "de" => "deu",
        "ru" => "rus",
        "pt" or "pt-br" or "pt-pt" => "por",
        "it" => "ita",
        "vi" => "vie",
        "th" => "tha",
        "id" => "ind",
        "hi" => "hin",
        "ar" => "ara",
        _ => "eng"
    };

    public static async Task<string> TryReadAsync(
        CoreWebView2 core,
        string providerName,
        string sourceText,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new MemoryStream();
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            if (stream.Length < 1024) return string.Empty;
            stream.Position = 0;

            using var full = new Bitmap(stream);
            if (full.Width < 300 || full.Height < 200) return string.Empty;

            var cropRect = TargetPane(full.Width, full.Height, providerName);
            using var target = full.Clone(cropRect, PixelFormat.Format32bppArgb);
            var language = MapTesseractLanguage(targetLanguage);
            var result = await Ocr.Value.ReadAsync(target, language, cancellationToken);
            if (result.Confidence < 0.18f) return string.Empty;

            var candidate = CleanOcr(result.Text, targetLanguage);
            return WebResultExtractor.IsTranslationCandidate(candidate, sourceText, targetLanguage)
                ? candidate
                : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Visual OCR is only the last fallback. DOM/API translation must keep working if it fails.
            return string.Empty;
        }
    }

    private static Rectangle TargetPane(int width, int height, string providerName)
    {
        // Translator UIs are all split source-left / target-right. Keep the crop away from the
        // top language selectors and far-right navigation rail; the large result text remains.
        var x = (int)Math.Round(width * 0.49);
        var y = providerName.StartsWith("Papago", StringComparison.OrdinalIgnoreCase)
            ? (int)Math.Round(height * 0.35)
            : (int)Math.Round(height * 0.26);
        var right = (int)Math.Round(width * 0.93);
        var bottom = (int)Math.Round(height * 0.76);
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        right = Math.Clamp(right, x + 1, width);
        bottom = Math.Clamp(bottom, y + 1, height);
        return Rectangle.FromLTRB(x, y, right, bottom);
    }

    private static string CleanOcr(string? raw, string targetLanguage)
    {
        var text = TesseractOcrService.Normalize(raw);
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string[] uiTokens =
        [
            "높임말", "용어집", "즐겨찾기", "번역 설정", "번역기록", "플러스 소개", "로그인",
            "복사", "공유", "copy", "share", "Papago+", "Google Translate", "DeepL"
        ];
        foreach (var token in uiTokens)
            text = text.Replace(token, " ", StringComparison.OrdinalIgnoreCase);

        // A single language label at the beginning of the target pane is common and safe to trim.
        var leading = targetLanguage.ToLowerInvariant() switch
        {
            var x when x.StartsWith("ko") => new[] { "한국어" },
            var x when x.StartsWith("en") => new[] { "English", "영어" },
            var x when x.StartsWith("ja") => new[] { "日本語", "일본어" },
            var x when x.StartsWith("zh") => new[] { "中文", "중국어" },
            _ => Array.Empty<string>()
        };
        text = text.Trim();
        foreach (var label in leading)
        {
            if (text.StartsWith(label + " ", StringComparison.OrdinalIgnoreCase))
                text = text[(label.Length + 1)..].Trim();
        }

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
