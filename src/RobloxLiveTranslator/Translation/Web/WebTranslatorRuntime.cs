using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RobloxLiveTranslator.Services;

namespace RobloxLiveTranslator.Translation.Web;

public sealed class WebTranslatorRuntime
{
    private CoreWebView2Environment? _environment;

    public async Task InitializeAsync(IEnumerable<WebView2> webViews)
    {
        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            throw new InvalidOperationException(
                "Microsoft Edge WebView2 Runtime이 설치되어 있지 않습니다. 관리자 권한이 아닌 일반 터미널에서 'winget install --id Microsoft.EdgeWebView2Runtime -e'를 실행한 뒤 다시 시작하세요.", ex);
        }

        Directory.CreateDirectory(SettingsStore.WebViewDataDirectory);
        _environment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: SettingsStore.WebViewDataDirectory);
        foreach (var webView in webViews)
        {
            await webView.EnsureCoreWebView2Async(_environment);
            if (webView.CoreWebView2 is null) continue;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        }
    }
}
