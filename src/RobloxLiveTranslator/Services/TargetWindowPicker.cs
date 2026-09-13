using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Services;

public sealed class TargetWindowPicker
{
    public async Task<IntPtr> PickAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            var deadline = DateTime.UtcNow + timeout;
            var wasDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
                if (isDown && !wasDown)
                {
                    if (!NativeMethods.GetCursorPos(out var point)) return IntPtr.Zero;
                    var hwnd = NativeMethods.WindowFromPoint(point);
                    hwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
                    if (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd))
                        return hwnd;
                }

                wasDown = isDown;
                await Task.Delay(15, cancellationToken);
            }

            return IntPtr.Zero;
        }, cancellationToken);
    }
}
