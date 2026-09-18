using System.Drawing;
using RobloxLiveTranslator.Models;

namespace RobloxLiveTranslator.Services;

/// <summary>
/// OCR backend boundary. Tesseract is the built-in implementation; additional local backends
/// (for example PaddleOCR or language-specialized recognizers) can be added without changing
/// monitoring, quick-capture, translation, history, or overlay code.
/// </summary>
public interface IOcrService : IDisposable
{
    Task<OcrResult> ReadAsync(
        Bitmap roi,
        string languages,
        CancellationToken cancellationToken = default,
        bool fastPath = false);
}
