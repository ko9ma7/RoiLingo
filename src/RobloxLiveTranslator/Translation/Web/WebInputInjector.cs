using Microsoft.Web.WebView2.Core;

namespace RobloxLiveTranslator.Translation.Web;

/// <summary>
/// Drives the real source editor in translator WebViews.  Modern translator pages are React/Vue
/// applications and may ignore a plain element.value assignment.  The injector therefore focuses
/// the real editor and uses the Chromium DevTools Input domain so the site receives browser-level
/// editing events, then verifies that the text is still present after the SPA has settled.
/// </summary>
public static class WebInputInjector
{
    public static async Task<bool> EnsureSourceTextAsync(
        CoreWebView2 core,
        string providerName,
        string sourceText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return false;

        // A translator SPA can hydrate/re-render shortly after navigation and wipe an early input.
        // Retry a few times and verify after each browser-level insertion.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsSourcePresentAsync(core, sourceText))
                return true;

            var focused = ParseBoolean(await core.ExecuteScriptAsync(BuildFocusSourceScript(providerName)));
            if (focused)
            {
                await ReplaceFocusedEditorTextAsync(core, sourceText, cancellationToken);
                await Task.Delay(attempt == 0 ? 180 : 260, cancellationToken);
                if (await IsSourcePresentAsync(core, sourceText))
                    return true;
            }

            // Fallback for pages where focusable editor discovery is temporarily incomplete.
            var direct = ParseBoolean(await core.ExecuteScriptAsync(BuildDirectInputScript(providerName, sourceText)));
            if (direct)
            {
                await Task.Delay(220, cancellationToken);
                if (await IsSourcePresentAsync(core, sourceText))
                    return true;
            }

            await Task.Delay(220 + attempt * 120, cancellationToken);
        }

        return false;
    }

    public static Task<bool> IsSourcePresentAsync(CoreWebView2 core, string sourceText)
    {
        var sourceJson = JsonSerializer.Serialize(sourceText);
        var script = $$"""
(() => {
  const target = {{sourceJson}}.replace(/\s+/g, ' ').trim().toLowerCase();
  if (!target) return false;
  const read = el => ((typeof el.value === 'string' && el.value) || el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
  const visible = el => {
    if (!el || !el.getBoundingClientRect) return false;
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 4 && r.height > 4 && r.bottom > 0 && r.right > 0 && r.top < innerHeight && r.left < innerWidth &&
           s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0;
  };
  const roots = [document];
  const seen = new Set();
  const editors = [];
  while (roots.length) {
    const root = roots.pop();
    if (!root || seen.has(root)) continue;
    seen.add(root);
    try {
      for (const el of root.querySelectorAll('textarea,input,[contenteditable="true"],[role="textbox"]')) {
        editors.push(el);
        if (el.shadowRoot) roots.push(el.shadowRoot);
      }
      for (const el of root.querySelectorAll('*')) if (el.shadowRoot) roots.push(el.shadowRoot);
      for (const frame of root.querySelectorAll('iframe')) {
        try { if (frame.contentDocument) roots.push(frame.contentDocument); } catch (_) { }
      }
    } catch (_) { }
  }
  return editors.filter(visible).some(el => {
    const t = read(el);
    return t === target || (target.length >= 10 && (t.includes(target) || target.includes(t)));
  });
})()
""";
        return ExecuteBoolAsync(core, script);
    }

    private static async Task ReplaceFocusedEditorTextAsync(
        CoreWebView2 core,
        string sourceText,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Ctrl+A / Backspace gives React/contenteditable controls a real editing sequence.
        await DevToolsKeyAsync(core, "keyDown", "Control", "ControlLeft", 17, 2);
        await DevToolsKeyAsync(core, "keyDown", "a", "KeyA", 65, 2);
        await DevToolsKeyAsync(core, "keyUp", "a", "KeyA", 65, 2);
        await DevToolsKeyAsync(core, "keyUp", "Control", "ControlLeft", 17, 0);
        await DevToolsKeyAsync(core, "keyDown", "Backspace", "Backspace", 8, 0);
        await DevToolsKeyAsync(core, "keyUp", "Backspace", "Backspace", 8, 0);

        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(new { text = sourceText });
        await core.CallDevToolsProtocolMethodAsync("Input.insertText", json);

        // Some applications listen to change/keyup in addition to the browser input event.
        await core.ExecuteScriptAsync("""
(() => {
  const el = document.activeElement;
  if (!el) return false;
  try { el.dispatchEvent(new Event('change', { bubbles: true })); } catch (_) { }
  try { el.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true, key: 'Unidentified' })); } catch (_) { }
  return true;
})()
""");
    }

    private static Task DevToolsKeyAsync(
        CoreWebView2 core,
        string type,
        string key,
        string code,
        int virtualKey,
        int modifiers)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type,
            key,
            code,
            windowsVirtualKeyCode = virtualKey,
            nativeVirtualKeyCode = virtualKey,
            modifiers
        });
        return core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", payload);
    }

    private static string BuildFocusSourceScript(string providerName)
    {
        var providerJson = JsonSerializer.Serialize(providerName);
        return $$"""
(() => {
  const provider = {{providerJson}}.toLowerCase();
  const visible = el => {
    if (!el || !el.getBoundingClientRect) return false;
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 60 && r.height > 28 && r.bottom > 0 && r.right > 0 && r.top < innerHeight && r.left < innerWidth &&
           s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0;
  };
  const readHint = el => [el.getAttribute('placeholder'), el.getAttribute('aria-label'), el.getAttribute('data-testid'), el.id, el.className]
    .filter(Boolean).join(' ').toLowerCase();

  const roots = [document];
  const seen = new Set();
  const editors = [];
  while (roots.length) {
    const root = roots.pop();
    if (!root || seen.has(root)) continue;
    seen.add(root);
    try {
      for (const el of root.querySelectorAll('textarea,[contenteditable="true"],[role="textbox"],input[type="text"],input:not([type])')) {
        editors.push(el);
        if (el.shadowRoot) roots.push(el.shadowRoot);
      }
      for (const el of root.querySelectorAll('*')) if (el.shadowRoot) roots.push(el.shadowRoot);
      for (const frame of root.querySelectorAll('iframe')) {
        try { if (frame.contentDocument) roots.push(frame.contentDocument); } catch (_) { }
      }
    } catch (_) { }
  }

  const ranked = editors.filter(visible).map(el => {
    const r = el.getBoundingClientRect();
    const hint = readHint(el);
    let score = 0;
    if (r.left < innerWidth * 0.56) score += 180; else score -= 160;
    if (r.width > innerWidth * 0.24) score += 90;
    if (r.height > 55) score += 55;
    if (/번역할|입력|원본|source|translate|enter|text/.test(hint)) score += 120;
    if (/target|결과|translation result/.test(hint)) score -= 180;

    if (provider.includes('papago')) {
      if (/txtsource|sourceedit|source.*textarea/.test(hint)) score += 300;
      if (/txttarget|targetedit/.test(hint)) score -= 350;
      if (r.top > innerHeight * .24 && r.top < innerHeight * .82) score += 45;
    } else if (provider.includes('google')) {
      if (/source text|원본 텍스트/.test(hint)) score += 260;
      if (/translator-source/.test(hint)) score += 220;
    } else if (provider.includes('deepl')) {
      if (/translator-source-input|source/.test(hint)) score += 260;
      if (/translator-target-input|target/.test(hint)) score -= 260;
    }
    return { el, score };
  }).sort((a,b) => b.score - a.score);

  const best = ranked[0];
  if (!best || best.score < 20) return false;
  try {
    best.el.setAttribute('data-roilingo-source', '1');
    best.el.focus({ preventScroll: true });
    if (typeof best.el.select === 'function') best.el.select();
    return document.activeElement === best.el || best.el.matches(':focus');
  } catch (_) {
    try { best.el.focus(); return true; } catch (_) { return false; }
  }
})()
""";
    }

    private static string BuildDirectInputScript(string providerName, string sourceText)
    {
        var sourceJson = JsonSerializer.Serialize(sourceText);
        var providerJson = JsonSerializer.Serialize(providerName);
        return $$"""
(() => {
  const text = {{sourceJson}};
  const provider = {{providerJson}}.toLowerCase();
  const el = document.querySelector('[data-roilingo-source="1"]') || document.activeElement;
  if (!el || el === document.body) return false;
  try {
    el.focus();
    if (el instanceof HTMLTextAreaElement) {
      const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set;
      if (setter) setter.call(el, text); else el.value = text;
    } else if (el instanceof HTMLInputElement) {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
      if (setter) setter.call(el, text); else el.value = text;
    } else {
      el.textContent = '';
      try { document.execCommand('insertText', false, text); }
      catch (_) { el.textContent = text; }
    }
    try { el.dispatchEvent(new InputEvent('input', { bubbles:true, inputType:'insertText', data:text })); }
    catch (_) { el.dispatchEvent(new Event('input', { bubbles:true })); }
    el.dispatchEvent(new Event('change', { bubbles:true }));
    return true;
  } catch (_) { return false; }
})()
""";
    }

    private static async Task<bool> ExecuteBoolAsync(CoreWebView2 core, string script) =>
        ParseBoolean(await core.ExecuteScriptAsync(script));

    private static bool ParseBoolean(string? json) =>
        string.Equals(json?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}
