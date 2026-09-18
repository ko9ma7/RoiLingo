using System.Drawing;
using System.Drawing.Imaging;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Services;

/// <summary>
/// Captures the selected HWND client area.
///
/// Auto mode prefers a direct screen copy while the target is foreground because that is the most
/// reliable path for GPU/game windows.  When the target is covered/inactive, it falls back to
/// PrintWindow so the capture is position-independent.  BackgroundOnly never reads from the screen.
///
/// PrintWindow can return a successful but visually empty/stale bitmap for accelerated windows, so
/// returned frames are screened for near-uniform content before they are accepted.
/// </summary>
public sealed class WindowCaptureService
{
    private const uint PwClientOnly = 0x00000001;
    private const uint PwRenderFullContent = 0x00000002;
    private readonly string _mode;

    public string LastMethod { get; private set; } = "none";

    public WindowCaptureService(string mode = "Auto")
    {
        _mode = string.IsNullOrWhiteSpace(mode) ? "Auto" : mode;
    }

    public Bitmap? CaptureClient(IntPtr hwnd)
    {
        LastMethod = "none";
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return null;
        if (!NativeMethods.GetClientRect(hwnd, out var client) || client.Width <= 0 || client.Height <= 0) return null;

        var foreground = IsTargetForeground(hwnd);
        var minimized = NativeMethods.IsIconic(hwnd);
        var backgroundOnly = _mode.Equals("BackgroundOnly", StringComparison.OrdinalIgnoreCase);
        var backgroundFirst = _mode.Equals("BackgroundFirst", StringComparison.OrdinalIgnoreCase);

        // GPU/game windows are most accurately captured from the desktop while they are visible.
        // This path also avoids accepting a white/stale PrintWindow frame that technically succeeded.
        if (!backgroundOnly && foreground && !minimized && !backgroundFirst)
        {
            var screen = TryScreenClient(hwnd);
            if (screen is not null)
            {
                LastMethod = "screen-foreground";
                return screen;
            }
        }

        // Position-independent background capture for ordinary Win32/Chromium/etc. windows.
        var direct = TryPrintClient(hwnd, client.Width, client.Height, PwClientOnly | PwRenderFullContent)
                     ?? TryPrintClient(hwnd, client.Width, client.Height, PwClientOnly);
        if (direct is not null)
        {
            LastMethod = minimized ? "PrintWindow-client(minimized)" : "PrintWindow-client";
            return direct;
        }

        var full = TryPrintWholeWindow(hwnd);
        if (full is not null)
        {
            LastMethod = "PrintWindow-full";
            return full;
        }

        if (backgroundOnly || minimized || !foreground) return null;

        // Final fallback for foreground windows, including BackgroundFirst mode.
        var fallback = TryScreenClient(hwnd);
        if (fallback is not null)
        {
            LastMethod = "screen-foreground-fallback";
            return fallback;
        }

        return null;
    }

    private static Bitmap? TryScreenClient(IntPtr hwnd)
    {
        if (!NativeMethods.TryGetClientScreenRect(hwnd, out var clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
            return null;

        try
        {
            var screen = TryCopyScreen(clientRect, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
            if (screen is not null) return screen;
            return TryCopyScreen(clientRect, CopyPixelOperation.SourceCopy);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryCopyScreen(NativeMethods.RECT clientRect, CopyPixelOperation operation)
    {
        Bitmap? screen = null;
        try
        {
            screen = new Bitmap(clientRect.Width, clientRect.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(screen);
            graphics.CopyFromScreen(clientRect.Left, clientRect.Top, 0, 0, screen.Size, operation);
            return IsVisuallyEmpty(screen) ? DisposeAndNull(screen) : screen;
        }
        catch
        {
            screen?.Dispose();
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

            if (printed && !IsVisuallyEmpty(bitmap)) return bitmap;
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
            if (!printed || IsVisuallyEmpty(whole)) return null;

            if (!NativeMethods.TryGetClientScreenRect(hwnd, out var clientScreen)) return null;
            var offsetX = clientScreen.Left - windowRect.Left;
            var offsetY = clientScreen.Top - windowRect.Top;
            var crop = new Rectangle(offsetX, offsetY, client.Width, client.Height);
            if (crop.X < 0 || crop.Y < 0 || crop.Right > whole.Width || crop.Bottom > whole.Height) return null;
            var result = whole.Clone(crop, PixelFormat.Format32bppArgb);
            if (IsVisuallyEmpty(result))
            {
                result.Dispose();
                return null;
            }
            return result;
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

    /// <summary>
    /// Rejects common PrintWindow false-success frames: almost-black, almost-white, or extremely
    /// uniform images.  A real game/UI frame normally has enough range/variance to pass this test.
    /// </summary>
    private static bool IsVisuallyEmpty(Bitmap bitmap)
    {
        var count = 0;
        var dark = 0;
        var light = 0;
        double sum = 0;
        double sumSq = 0;
        var min = 255;
        var max = 0;
        var stepX = Math.Max(1, bitmap.Width / 32);
        var stepY = Math.Max(1, bitmap.Height / 24);

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var c = bitmap.GetPixel(x, y);
                var gray = (c.R * 30 + c.G * 59 + c.B * 11) / 100;
                count++;
                if (gray < 5) dark++;
                if (gray > 250) light++;
                sum += gray;
                sumSq += gray * gray;
                min = Math.Min(min, gray);
                max = Math.Max(max, gray);
            }
        }

        if (count == 0) return true;
        if (dark / (double)count > 0.985 || light / (double)count > 0.985) return true;

        var mean = sum / count;
        var variance = Math.Max(0, sumSq / count - mean * mean);
        var range = max - min;
        return range < 6 && variance < 3.0;
    }

    private static Bitmap? DisposeAndNull(Bitmap bitmap)
    {
        bitmap.Dispose();
        return null;
    }
}
