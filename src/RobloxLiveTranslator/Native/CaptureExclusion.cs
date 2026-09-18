using System.Windows;
using System.Windows.Interop;

namespace RobloxLiveTranslator.Native;

internal static class CaptureExclusion
{
    /// <summary>
    /// Prevent RoiLingo's own top-level windows from being included when WindowCaptureService
    /// falls back to CopyFromScreen. Without this, the overlay can OCR itself and create a feedback loop.
    /// This is best-effort because older Windows builds may not support WDA_EXCLUDEFROMCAPTURE.
    /// </summary>
    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (!NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE))
                NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_MONITOR);
        }
        catch
        {
            // Capture exclusion is a quality safeguard, not a reason to crash the app.
        }
    }
}
