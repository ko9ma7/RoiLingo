using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Services;

/// <summary>
/// Small Win32 global-hotkey wrapper. A failed registration is non-fatal because
/// another application may already own the shortcut; toolbar actions still work.
/// </summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private readonly HashSet<int> _registered = [];

    public GlobalHotkeyManager(Window owner)
    {
        _hwnd = new WindowInteropHelper(owner).Handle;
        _source = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("WPF HWND source is not available.");
        _source.AddHook(WndProc);
    }

    public bool Register(int id, uint modifiers, uint virtualKey, Action callback)
    {
        _actions[id] = callback;
        if (!NativeMethods.RegisterHotKey(_hwnd, id, modifiers, virtualKey))
        {
            _actions.Remove(id);
            return false;
        }
        _registered.Add(id);
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_HOTKEY) return IntPtr.Zero;
        var id = wParam.ToInt32();
        if (_actions.TryGetValue(id, out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _registered)
            NativeMethods.UnregisterHotKey(_hwnd, id);
        _registered.Clear();
        _actions.Clear();
        _source.RemoveHook(WndProc);
    }
}
