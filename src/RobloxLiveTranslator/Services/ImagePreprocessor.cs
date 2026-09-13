using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RobloxLiveTranslator.Services;

public static class ImagePreprocessor
{
    public static Bitmap PrepareForOcr(Bitmap source)
    {
        var scale = source.Height < 90 ? 3 : source.Height < 160 ? 2 : 1;
        var output = new Bitmap(Math.Max(1, source.Width * scale), Math.Max(1, source.Height * scale), PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(output);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var matrix = new ColorMatrix(new[]
        {
            new[] { .30f, .30f, .30f, 0f, 0f },
            new[] { .59f, .59f, .59f, 0f, 0f },
            new[] { .11f, .11f, .11f, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f }
        });
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(matrix);
        g.DrawImage(source, new Rectangle(0, 0, output.Width, output.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attrs);
        return output;
    }

    public static byte[] ToPngBytes(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
