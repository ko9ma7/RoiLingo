using System.Windows;
using System.Windows.Interop;

namespace RobloxLiveTranslator.Native;

internal static class CaptureExclusion
{
    private static string _policy = "Allow";

    public static void Configure(string? policy)
    {
        _policy = policy switch
        {
            "Exclude" => "Exclude",
            "MonitorOnly" => "MonitorOnly",
            _ => "Allow"
        };
    }

    /// <summary>
    /// Applies the user-selected display affinity to RoiLingo's top-level windows.
    /// Allow is the default so Remote Desktop, screen sharing, and screenshots can display the app.
    /// Exclude/MonitorOnly are opt-in privacy modes for users who do not want the app captured.
    /// </summary>
    public static void Apply(Window window, string? policy = null)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var effectivePolicy = policy ?? _policy;
            var affinity = effectivePolicy switch
            {
                "Exclude" => NativeMethods.WDA_EXCLUDEFROMCAPTURE,
                "MonitorOnly" => NativeMethods.WDA_MONITOR,
                _ => NativeMethods.WDA_NONE
            };
            NativeMethods.SetWindowDisplayAffinity(hwnd, affinity);
        }
        catch
        {
            // Capture exclusion is a quality safeguard, not a reason to crash the app.
        }
    }
}
