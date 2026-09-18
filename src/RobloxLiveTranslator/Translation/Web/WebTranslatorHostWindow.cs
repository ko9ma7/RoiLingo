using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Translation.Web;

/// <summary>
/// Dedicated, off-screen WebView2 host used by the translation engine.
///
/// The visible WebView2 controls inside MainWindow live under the collapsible advanced-settings UI.
/// WPF/Win32 hosted controls can depend on their visual host being materialized, so the translation
/// engine must not depend on the user opening that panel. This window is shown modelessly outside the
/// virtual desktop and stays alive for the application lifetime. It is not shown in the taskbar and
/// never takes focus.
/// </summary>
public sealed class WebTranslatorHostWindow : Window
{
    private readonly TaskCompletionSource<bool> _loaded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WebView2 Papago { get; } = new();
    public WebView2 Google { get; } = new();
    public WebView2 DeepL { get; } = new();

    public WebTranslatorHostWindow()
    {
        Title = "RoiLingo Web Translation Engine";
        Width = 1280;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;

        // Keep a normal rendered HWND/WebView2 surface, but place it safely outside the virtual desktop.
        Left = SystemParameters.VirtualScreenLeft - Width - 2048;
        Top = SystemParameters.VirtualScreenTop - Height - 2048;

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Papago", Content = Papago });
        tabs.Items.Add(new TabItem { Header = "Google", Content = Google });
        tabs.Items.Add(new TabItem { Header = "DeepL", Content = DeepL });
        Content = tabs;

        Loaded += (_, _) =>
        {
            CaptureExclusion.Apply(this);
            _loaded.TrySetResult(true);
        };
    }

    public async Task EnsureShownAsync()
    {
        if (!IsVisible) Show();
        if (!IsLoaded) await _loaded.Task;
    }
}
