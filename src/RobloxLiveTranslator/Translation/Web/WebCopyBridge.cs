using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;

namespace RobloxLiveTranslator.Translation.Web;

/// <summary>
/// Captures text that a translator page tries to copy. This does not depend on the Windows
/// clipboard: the bridge hooks navigator.clipboard/document.execCommand inside the WebView2 page
/// and receives the exact text through WebMessageReceived. It is used only as a fallback.
/// </summary>
internal static class WebCopyBridge
{
    private static readonly ConditionalWeakTable<CoreWebView2, BridgeState> States = new();

    private const string BridgeScript = """
(() => {
  if (window.__roilingoCopyBridgeInstalled) return true;
  window.__roilingoCopyBridgeInstalled = true;

  const send = (text, kind) => {
    try {
      const value = String(text || '').replace(/\s+/g, ' ').trim();
      if (!value) return;
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ __roilingo: 'copy', kind: kind || 'unknown', text: value });
      }
    } catch (_) { }
  };

  // Most modern translator sites use navigator.clipboard.writeText.
  try {
    const clipboard = navigator.clipboard;
    const proto = clipboard && Object.getPrototypeOf(clipboard);
    if (proto && typeof proto.writeText === 'function' && !proto.__roilingoWriteTextWrapped) {
      const original = proto.writeText;
      Object.defineProperty(proto, '__roilingoWriteTextWrapped', { value: true, configurable: true });
      Object.defineProperty(proto, 'writeText', {
        configurable: true,
        writable: true,
        value: function(text) {
          send(text, 'navigator.clipboard.writeText');
          return original.call(this, text);
        }
      });
    }
  } catch (_) { }

  // Older sites may use document.execCommand('copy') after selecting a hidden textarea.
  try {
    if (typeof document.execCommand === 'function' && !document.__roilingoExecCopyWrapped) {
      const originalExec = document.execCommand.bind(document);
      Object.defineProperty(document, '__roilingoExecCopyWrapped', { value: true, configurable: true });
      document.execCommand = function(command, showUi, value) {
        if (String(command || '').toLowerCase() === 'copy') {
          try {
            const active = document.activeElement;
            let text = '';
            if (active && typeof active.value === 'string') {
              const start = Number.isInteger(active.selectionStart) ? active.selectionStart : 0;
              const end = Number.isInteger(active.selectionEnd) ? active.selectionEnd : active.value.length;
              text = active.value.slice(start, end) || active.value;
            }
            if (!text) text = (window.getSelection && window.getSelection().toString()) || '';
            send(text, 'document.execCommand');
          } catch (_) { }
        }
        return originalExec(command, showUi, value);
      };
    }
  } catch (_) { }

  document.addEventListener('copy', () => {
    try {
      const active = document.activeElement;
      let text = (window.getSelection && window.getSelection().toString()) || '';
      if (!text && active && typeof active.value === 'string') text = active.value;
      send(text, 'copy-event');
    } catch (_) { }
  }, true);

  return true;
})()
""";

    public static Task EnsureInstalledAsync(CoreWebView2 core) =>
        States.GetValue(core, _ => new BridgeState()).EnsureInstalledAsync(core);

    public static Task<string> CaptureFromTargetCopyButtonAsync(CoreWebView2 core, CancellationToken cancellationToken) =>
        States.GetValue(core, _ => new BridgeState()).CaptureAsync(core, cancellationToken);

    private sealed class BridgeState
    {
        private readonly SemaphoreSlim _installGate = new(1, 1);
        private readonly object _waiterGate = new();
        private bool _installed;
        private TaskCompletionSource<string>? _waiter;

        public async Task EnsureInstalledAsync(CoreWebView2 core)
        {
            if (_installed) return;
            await _installGate.WaitAsync();
            try
            {
                if (_installed) return;
                core.WebMessageReceived += OnWebMessageReceived;
                await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);
                try { await core.ExecuteScriptAsync(BridgeScript); }
                catch (InvalidOperationException) { /* navigation can race; next document gets the registered script */ }
                _installed = true;
            }
            finally
            {
                _installGate.Release();
            }
        }

        public async Task<string> CaptureAsync(CoreWebView2 core, CancellationToken cancellationToken)
        {
            await EnsureInstalledAsync(core);
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_waiterGate) _waiter = tcs;

            try
            {
                var json = await core.ExecuteScriptAsync(WebTranslationScripts.TargetCopyButtonClick);
                var clicked = false;
                try { clicked = JsonSerializer.Deserialize<bool>(json); }
                catch (JsonException) { }
                if (!clicked) return string.Empty;

                var timeout = Task.Delay(1400, cancellationToken);
                var completed = await Task.WhenAny(tcs.Task, timeout);
                if (completed == tcs.Task)
                    return await tcs.Task;

                cancellationToken.ThrowIfCancellationRequested();
                return string.Empty;
            }
            finally
            {
                lock (_waiterGate)
                {
                    if (ReferenceEquals(_waiter, tcs)) _waiter = null;
                }
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;
                if (!root.TryGetProperty("__roilingo", out var marker) || marker.GetString() != "copy") return;
                if (!root.TryGetProperty("text", out var textNode) || textNode.ValueKind != JsonValueKind.String) return;
                var text = textNode.GetString();
                if (string.IsNullOrWhiteSpace(text)) return;

                TaskCompletionSource<string>? waiter;
                lock (_waiterGate) waiter = _waiter;
                waiter?.TrySetResult(text.Trim());
            }
            catch (JsonException)
            {
                // Ignore messages from the page that are unrelated to RoiLingo.
            }
        }
    }
}
