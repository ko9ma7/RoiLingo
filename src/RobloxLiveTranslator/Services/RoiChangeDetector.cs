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
        using var small = new Bitmap(SignatureWidth, SignatureHeight, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.Low;
            g.DrawImage(source, 0, 0, SignatureWidth, SignatureHeight);
        }

        var bytes = new byte[SignatureWidth * SignatureHeight];
        for (var y = 0; y < SignatureHeight; y++)
        for (var x = 0; x < SignatureWidth; x++)
        {
            var c = small.GetPixel(x, y);
            bytes[y * SignatureWidth + x] = (byte)((c.R * 30 + c.G * 59 + c.B * 11) / 100);
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
}
