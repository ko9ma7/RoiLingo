using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Translation.Web;

/// <summary>
/// Dedicated off-screen WebView2 host used by the translation engine.
///
/// Important: all translator WebViews are kept materialized in the visual tree at the same time.
/// A TabControl only materializes the selected tab content reliably, which can leave the other
/// WebView2 controls without a native child HWND/controller and make engine startup depend on a
/// user opening/changing a settings tab.  A simple Grid avoids that dependency.
/// </summary>
public sealed class WebTranslatorHostWindow : Window
{
    private readonly TaskCompletionSource<bool> _loaded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WebView2 Papago { get; } = NewView();
    public WebView2 Google { get; } = NewView();
    public WebView2 DeepL { get; } = NewView();

    public WebTranslatorHostWindow()
    {
        Title = "RoiLingo Web Translation Engine";
        Width = 1200;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;

        // Keep a real rendered HWND/WebView2 surface while staying outside the user's desktop.
        // Each WebView has a non-zero layout slot, so all three controllers can be initialized
        // without the Advanced Settings window ever being opened.
        Left = SystemParameters.VirtualScreenLeft - Width - 2048;
        Top = SystemParameters.VirtualScreenTop - Height - 2048;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(Papago, 0);
        Grid.SetRow(Google, 1);
        Grid.SetRow(DeepL, 2);
        grid.Children.Add(Papago);
        grid.Children.Add(Google);
        grid.Children.Add(DeepL);
        Content = grid;

        Loaded += (_, _) =>
        {
            CaptureExclusion.Apply(this);
            _loaded.TrySetResult(true);
        };
    }

    private static WebView2 NewView() => new()
    {
        MinWidth = 320,
        MinHeight = 180,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };

    public async Task EnsureShownAsync()
    {
        if (!IsVisible) Show();
        if (!IsLoaded) await _loaded.Task;
        UpdateLayout();
    }
}
