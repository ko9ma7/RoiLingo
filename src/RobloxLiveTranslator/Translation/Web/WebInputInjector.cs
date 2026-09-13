using Microsoft.Web.WebView2.Core;

namespace RobloxLiveTranslator.Translation.Web;

public static class WebInputInjector
{
    public static async Task<bool> EnsureSourceTextAsync(
        CoreWebView2 core,
        string providerName,
        string sourceText,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var present = await IsSourcePresentAsync(core, sourceText);
        if (present) return true;

        var script = BuildInputScript(providerName, sourceText);
        var result = await core.ExecuteScriptAsync(script);
        var injected = ParseBoolean(result);
        if (!injected) return false;

        await Task.Delay(180, cancellationToken);
        return await IsSourcePresentAsync(core, sourceText);
    }

    private static async Task<bool> IsSourcePresentAsync(CoreWebView2 core, string sourceText)
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
    return r.width > 4 && r.height > 4 && r.bottom > 0 && r.right > 0 && r.top < innerHeight && r.left < innerWidth && s.display !== 'none' && s.visibility !== 'hidden';
  };
  return [...document.querySelectorAll('textarea,input,[contenteditable="true"],[role="textbox"]')]
    .filter(visible)
    .some(el => {
      const t = read(el);
      return t === target || (target.length >= 10 && (t.includes(target) || target.includes(t)));
    });
})()
""";
        return ParseBoolean(await core.ExecuteScriptAsync(script));
    }

    private static string BuildInputScript(string providerName, string sourceText)
    {
        var sourceJson = JsonSerializer.Serialize(sourceText);
        var providerJson = JsonSerializer.Serialize(providerName);
        return $$"""
(() => {
  const text = {{sourceJson}};
  const provider = {{providerJson}};
  const visible = el => {
    if (!el || !el.getBoundingClientRect) return false;
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 40 && r.height > 24 && r.bottom > 0 && r.right > 0 && r.top < innerHeight && r.left < innerWidth && s.display !== 'none' && s.visibility !== 'hidden';
  };
  const candidates = [...document.querySelectorAll('textarea,[contenteditable="true"],[role="textbox"],input[type="text"],input:not([type])')]
    .filter(visible)
    .map(el => {
      const r = el.getBoundingClientRect();
      const ph = (el.getAttribute('placeholder') || el.getAttribute('aria-label') || '').toLowerCase();
      let score = 0;
      if (r.left < innerWidth * .56) score += 160;
      else score -= 120;
      if (r.width > innerWidth * .22) score += 70;
      if (r.height > 50) score += 40;
      if (/번역|translate|enter|입력|source|text/.test(ph)) score += 90;
      if (provider.toLowerCase().includes('papago') && r.top > innerHeight * .20 && r.top < innerHeight * .78) score += 35;
      if (provider.toLowerCase().includes('google') && r.top < innerHeight * .80) score += 25;
      if (provider.toLowerCase().includes('deepl') && r.top < innerHeight * .85) score += 25;
      return { el, score };
    })
    .sort((a,b) => b.score - a.score);

  const el = candidates[0]?.el;
  if (!el || candidates[0].score < 20) return false;

  el.focus();
  try {
    if (el instanceof HTMLTextAreaElement) {
      const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set;
      if (setter) setter.call(el, text); else el.value = text;
    } else if (el instanceof HTMLInputElement) {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
      if (setter) setter.call(el, text); else el.value = text;
    } else {
      el.textContent = text;
    }
  } catch {
    if ('value' in el) el.value = text; else el.textContent = text;
  }

  try { el.dispatchEvent(new InputEvent('input', { bubbles:true, inputType:'insertText', data:text })); }
  catch { el.dispatchEvent(new Event('input', { bubbles:true })); }
  el.dispatchEvent(new Event('change', { bubbles:true }));
  el.dispatchEvent(new KeyboardEvent('keyup', { bubbles:true, key:'Unidentified' }));
  return true;
})()
""";
    }

    private static bool ParseBoolean(string json) =>
        string.Equals(json?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}
