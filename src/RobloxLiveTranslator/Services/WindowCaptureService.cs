using System.Drawing;
using System.Drawing.Imaging;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Services;

/// <summary>
/// Captures the selected HWND without depending on its screen position.
/// BackgroundFirst uses PrintWindow against the client DC first, so a non-minimized
/// window can normally be captured even when another window is covering it.
/// Screen capture is only used as a last resort while the target is actually foreground;
/// this prevents OCR from accidentally reading whatever happens to cover the target.
/// </summary>
public sealed class WindowCaptureService
{
    private const uint PwClientOnly = 0x00000001;
    private const uint PwRenderFullContent = 0x00000002;
    private readonly string _mode;

    public string LastMethod { get; private set; } = "none";

    public WindowCaptureService(string mode = "BackgroundFirst")
    {
        _mode = string.IsNullOrWhiteSpace(mode) ? "BackgroundFirst" : mode;
    }

    public Bitmap? CaptureClient(IntPtr hwnd)
    {
        LastMethod = "none";
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return null;
        if (!NativeMethods.GetClientRect(hwnd, out var client) || client.Width <= 0 || client.Height <= 0) return null;

        // 1) Position-independent client capture. This is the preferred path for covered/inactive windows.
        var direct = TryPrintClient(hwnd, client.Width, client.Height, PwClientOnly | PwRenderFullContent)
                     ?? TryPrintClient(hwnd, client.Width, client.Height, PwClientOnly);
        if (direct is not null)
        {
            LastMethod = NativeMethods.IsIconic(hwnd) ? "PrintWindow-client(minimized)" : "PrintWindow-client";
            return direct;
        }

        // 2) Some programs ignore PW_CLIENTONLY+PW_RENDERFULLCONTENT but render the full window.
        var full = TryPrintWholeWindow(hwnd);
        if (full is not null)
        {
            LastMethod = "PrintWindow-full";
            return full;
        }

        if (_mode.Equals("BackgroundOnly", StringComparison.OrdinalIgnoreCase)) return null;

        // 3) Screen fallback is safe only when the target itself is foreground. Otherwise it would OCR
        // the covering app instead of the selected app, which is worse than returning no frame.
        if (!IsTargetForeground(hwnd) || NativeMethods.IsIconic(hwnd)) return null;
        if (!NativeMethods.TryGetClientScreenRect(hwnd, out var clientRect)) return null;

        try
        {
            var screen = new Bitmap(clientRect.Width, clientRect.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(screen);
            g.CopyFromScreen(clientRect.Left, clientRect.Top, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);
            LastMethod = "screen-foreground-fallback";
            return screen;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryPrintClient(IntPtr hwnd, int width, int height, uint flags)
    {
        try
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var printed = false;
            using (var g = Graphics.FromImage(bitmap))
            {
                var hdc = g.GetHdc();
                try { printed = NativeMethods.PrintWindow(hwnd, hdc, flags); }
                finally { g.ReleaseHdc(hdc); }
            }

            if (printed && !IsAlmostBlack(bitmap)) return bitmap;
            bitmap.Dispose();
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryPrintWholeWindow(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect) ||
            !NativeMethods.GetClientRect(hwnd, out var client) ||
            windowRect.Width <= 0 || windowRect.Height <= 0 || client.Width <= 0 || client.Height <= 0)
            return null;

        try
        {
            using var whole = new Bitmap(windowRect.Width, windowRect.Height, PixelFormat.Format32bppArgb);
            var printed = false;
            using (var g = Graphics.FromImage(whole))
            {
                var hdc = g.GetHdc();
                try { printed = NativeMethods.PrintWindow(hwnd, hdc, PwRenderFullContent); }
                finally { g.ReleaseHdc(hdc); }
            }
            if (!printed || IsAlmostBlack(whole)) return null;

            // Use the current client origin only to crop the already-rendered off-screen image.
            if (!NativeMethods.TryGetClientScreenRect(hwnd, out var clientScreen)) return null;
            var offsetX = clientScreen.Left - windowRect.Left;
            var offsetY = clientScreen.Top - windowRect.Top;
            var crop = new Rectangle(offsetX, offsetY, client.Width, client.Height);
            if (crop.X < 0 || crop.Y < 0 || crop.Right > whole.Width || crop.Bottom > whole.Height) return null;
            return whole.Clone(crop, PixelFormat.Format32bppArgb);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTargetForeground(IntPtr hwnd)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        var targetRoot = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        var foregroundRoot = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        return targetRoot == foregroundRoot;
    }

    private static bool IsAlmostBlack(Bitmap bitmap)
    {
        var total = 0;
        var dark = 0;
        var stepX = Math.Max(1, bitmap.Width / 24);
        var stepY = Math.Max(1, bitmap.Height / 24);
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
