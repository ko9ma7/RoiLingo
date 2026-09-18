using System.Drawing;
using System.Drawing.Imaging;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Services;

public static class ScreenCaptureService
{
    public static string LastMethod { get; private set; } = "none";

    public static Bitmap? CaptureVirtualScreen(IntPtr preferredTarget = default)
    {
        LastMethod = "none";

        var x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0)
        {
            LastMethod = "invalid-virtual-screen";
            return null;
        }

        // CaptureBlt includes layered/transparent surfaces that a plain GDI copy can omit.
        var bitmap = TryCopyVirtualScreen(x, y, width, height, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
        if (bitmap is not null)
        {
            LastMethod = "screen-copy-captureblt";
            return bitmap;
        }

        // Some display drivers reject CaptureBlt, so retain a plain GDI fallback.
        bitmap = TryCopyVirtualScreen(x, y, width, height, CopyPixelOperation.SourceCopy);
        if (bitmap is not null)
        {
            LastMethod = "screen-copy";
            return bitmap;
        }

        // If the virtual desktop is black/empty, preserve the target client surface and
        // place it back into virtual-screen coordinates so ROI selection still works.
        if (preferredTarget != IntPtr.Zero &&
            NativeMethods.IsWindow(preferredTarget) &&
            NativeMethods.TryGetClientScreenRect(preferredTarget, out var clientRect) &&
            clientRect.Width > 0 && clientRect.Height > 0)
        {
            var targetService = new WindowCaptureService("Auto");
            var targetCapture = targetService.CaptureClient(preferredTarget);
            if (targetCapture is not null)
            {
                using (targetCapture)
                {
                    var composite = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    using var graphics = Graphics.FromImage(composite);
                    graphics.Clear(Color.Black);

                    var destinationX = clientRect.Left - x;
                    var destinationY = clientRect.Top - y;
                    graphics.DrawImageUnscaled(targetCapture, destinationX, destinationY);

                    LastMethod = $"target-{targetService.LastMethod}-composite";
                    return composite;
                }
            }
        }

        LastMethod = "failed";
        return null;
    }

    private static Bitmap? TryCopyVirtualScreen(int x, int y, int width, int height, CopyPixelOperation operation)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(x, y, 0, 0, bitmap.Size, operation);

            if (IsVisuallyEmpty(bitmap))
            {
                bitmap.Dispose();
                return null;
            }

            return bitmap;
        }
        catch
        {
            bitmap?.Dispose();
            return null;
        }
    }

    private static bool IsVisuallyEmpty(Bitmap bitmap)
    {
        var sampleWidth = Math.Min(bitmap.Width, 96);
        var sampleHeight = Math.Min(bitmap.Height, 96);
        if (sampleWidth <= 0 || sampleHeight <= 0)
        {
            return true;
        }

        using var sample = new Bitmap(sampleWidth, sampleHeight, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(sample))
        {
            graphics.DrawImage(bitmap, new Rectangle(0, 0, sampleWidth, sampleHeight));
        }

        var dark = 0;
        var light = 0;
        var total = sampleWidth * sampleHeight;
        for (var py = 0; py < sampleHeight; py++)
        {
            for (var px = 0; px < sampleWidth; px++)
            {
                var color = sample.GetPixel(px, py);
                var brightness = (color.R + color.G + color.B) / 3;
                if (brightness <= 4)
                {
                    dark++;
                }
                else if (brightness >= 251)
                {
                    light++;
                }
            }
        }

        return dark / (double)total >= 0.985 || light / (double)total >= 0.985;
    }
}
