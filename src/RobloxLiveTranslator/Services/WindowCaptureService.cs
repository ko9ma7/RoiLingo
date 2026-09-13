using System.Drawing;
using System.Drawing.Imaging;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Services;

public sealed class WindowCaptureService
{
    private const uint PwRenderFullContent = 0x00000002;

    public Bitmap? CaptureClient(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || NativeMethods.IsIconic(hwnd)) return null;
        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect)) return null;
        if (!NativeMethods.TryGetClientScreenRect(hwnd, out var clientRect)) return null;
        if (windowRect.Width <= 0 || windowRect.Height <= 0 || clientRect.Width <= 0 || clientRect.Height <= 0) return null;

        using var whole = new Bitmap(windowRect.Width, windowRect.Height, PixelFormat.Format32bppArgb);
        var printed = false;
        using (var g = Graphics.FromImage(whole))
        {
            var hdc = g.GetHdc();
            try { printed = NativeMethods.PrintWindow(hwnd, hdc, PwRenderFullContent); }
            finally { g.ReleaseHdc(hdc); }
        }

        var offsetX = clientRect.Left - windowRect.Left;
        var offsetY = clientRect.Top - windowRect.Top;
        var crop = new Rectangle(offsetX, offsetY, clientRect.Width, clientRect.Height);

        if (printed && crop.X >= 0 && crop.Y >= 0 && crop.Right <= whole.Width && crop.Bottom <= whole.Height)
        {
            var result = whole.Clone(crop, PixelFormat.Format32bppArgb);
            if (!IsAlmostBlack(result)) return result;
            result.Dispose();
        }

        // Fallback for hardware-rendered windows when PrintWindow returns black.
        try
        {
            var screen = new Bitmap(clientRect.Width, clientRect.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(screen);
            g.CopyFromScreen(clientRect.Left, clientRect.Top, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);
            return screen;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAlmostBlack(Bitmap bitmap)
    {
        var total = 0;
        var dark = 0;
        var stepX = Math.Max(1, bitmap.Width / 20);
        var stepY = Math.Max(1, bitmap.Height / 20);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var c = bitmap.GetPixel(x, y);
                total++;
                if (c.R < 4 && c.G < 4 && c.B < 4) dark++;
            }
        }
        return total > 0 && dark / (double)total > 0.985;
    }
}
