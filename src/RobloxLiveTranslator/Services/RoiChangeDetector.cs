using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RobloxLiveTranslator.Services;

public static class RoiChangeDetector
{
    private const int SignatureWidth = 48;
    private const int SignatureHeight = 24;

    public static byte[] ComputeSignature(Bitmap source)
    {
        using var small = Resize(source, SignatureWidth, SignatureHeight);
        var bytes = new byte[SignatureWidth * SignatureHeight];
        for (var y = 0; y < SignatureHeight; y++)
        for (var x = 0; x < SignatureWidth; x++)
        {
            var c = small.GetPixel(x, y);
            bytes[y * SignatureWidth + x] = Gray(c);
        }
        return bytes;
    }

    public static double Difference(byte[]? a, byte[] b)
    {
        if (a is null || a.Length != b.Length) return 1.0;
        long sum = 0;
        for (var i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
        return sum / (double)(a.Length * 255);
    }

    /// <summary>
    /// Cheap text-likelihood score used only to choose the best frame from a short visual burst.
    /// Text normally creates local contrast/edges, while an empty flat background scores lower.
    /// This is intentionally much cheaper than OCR.
    /// </summary>
    public static double TextLikelihood(Bitmap source)
    {
        const int width = 64;
        const int height = 32;
        using var small = Resize(source, width, height);
        var gray = new byte[width * height];
        double sum = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var value = Gray(small.GetPixel(x, y));
            gray[y * width + x] = value;
            sum += value;
        }

        var mean = sum / gray.Length;
        double variance = 0;
        double edges = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = y * width + x;
            var d = gray[index] - mean;
            variance += d * d;
            if (x > 0) edges += Math.Abs(gray[index] - gray[index - 1]);
            if (y > 0) edges += Math.Abs(gray[index] - gray[index - width]);
        }

        variance /= gray.Length * 255.0 * 255.0;
        edges /= Math.Max(1, ((width - 1) * height + (height - 1) * width) * 255.0);
        return variance * 0.45 + edges * 0.55;
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        var small = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(small);
        g.InterpolationMode = InterpolationMode.Low;
        g.DrawImage(source, 0, 0, width, height);
        return small;
    }

    private static byte Gray(Color c) => (byte)((c.R * 30 + c.G * 59 + c.B * 11) / 100);
}
