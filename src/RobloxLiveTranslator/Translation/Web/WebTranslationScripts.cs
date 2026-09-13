namespace RobloxLiveTranslator.Translation.Web;

public static class WebTranslationScripts
{
    public const string GoogleResult = """
(() => {
  const selectors = [
    'span[jsname="W297wb"]',
    'span[jsname="jqKxS"]',
    '[data-language-for-alternatives] span',
    'div[aria-live="polite"] span',
    '[data-testid="translator-target-input"]'
  ];
  for (const selector of selectors) {
    const values = [...document.querySelectorAll(selector)]
      .map(e => (e.value || e.innerText || e.textContent || '').trim())
      .filter(Boolean);
    if (values.length) return values.join(' ').trim();
  }
  return '';
})()
""";

    public const string PapagoResult = """
(() => {
  const selectors = [
    '#txtTarget',
    'textarea#txtTarget',
    '#targetEditArea > p',
    '#targetEditArea',
    '.target_editarea textarea',
    '.target_editarea',
    '[data-testid="target-textarea"]',
    '[data-testid*="target"] textarea',
    '[data-testid*="target"] [contenteditable="true"]'
  ];
  for (const selector of selectors) {
    const el = document.querySelector(selector);
    if (!el) continue;
    const value = (el.value || el.innerText || el.textContent || '').trim();
    if (value) return value;
  }
  return '';
})()
""";

    public const string DeepLResult = """
(() => {
  const selectors = [
    '#target-dummydiv',
    'd-textarea[data-testid="translator-target-input"] p',
    'd-textarea.lmt__target_textarea p',
    'textarea[dl-test="translator-target-input"]',
    '[data-testid="translator-target-input"]',
    'd-textarea[name="target"]',
    '[aria-labelledby*="translation-target"] [contenteditable="true"]'
  ];
  for (const selector of selectors) {
    const el = document.querySelector(selector);
    if (!el) continue;
    const value = (el.value || el.innerText || el.textContent || '').trim();
    if (value) return value;
  }
  return '';
})()
""";


    public const string TargetCopyButtonClick = """
(() => {
  const norm = s => (s || '').replace(/\s+/g, ' ').trim();
  const re = /(복사|copy|클립보드|clipboard)/i;
  const visible = el => {
    if (!el || !el.getBoundingClientRect) return false;
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 2 && r.height > 2 && s.display !== 'none' && s.visibility !== 'hidden';
  };
  const buttons = [...document.querySelectorAll('button,[role="button"]')].filter(visible);
  let best = null;
  let score = -1;
  for (const btn of buttons) {
    const r = btn.getBoundingClientRect();
    const label = norm(`${btn.getAttribute('aria-label') || ''} ${btn.getAttribute('title') || ''} ${btn.innerText || ''}`);
    if (!re.test(label)) continue;
    let s = 0;
    if (r.left + r.width / 2 > innerWidth * 0.5) s += 100;
    if (/복사|copy/i.test(label)) s += 50;
    s += Math.min(20, r.left / Math.max(1, innerWidth) * 20);
    if (s > score) { score = s; best = btn; }
  }
  if (!best) return false;
  best.click();
  return true;
})()
""";
    public static string BuildHeuristicResultScript(string sourceText, string targetLanguage, string providerName)
    {
        var sourceJson = JsonSerializer.Serialize(sourceText);
        var targetJson = JsonSerializer.Serialize(targetLanguage);
        var providerJson = JsonSerializer.Serialize(providerName);
        return $$"""
(() => {
  const source = {{sourceJson}};
  const target = {{targetJson}};
  const provider = {{providerJson}};
  const norm = s => (s || '').replace(/\s+/g, ' ').trim();
  const sourceNorm = norm(source).toLowerCase();
  const visible = el => {
    if (!el || !el.getBoundingClientRect) return false;
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 2 && r.height > 2 && s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0;
  };
  const read = el => norm((typeof el.value === 'string' && el.value) || el.innerText || el.textContent || '');
  const targetRatio = text => {
    const letters = [...text].filter(ch => /\p{L}/u.test(ch));
    if (!letters.length) return 0;
    const lang = target.toLowerCase();
    const match = ch => {
      const n = ch.codePointAt(0);
      if (lang.startsWith('ko')) return n >= 0xAC00 && n <= 0xD7A3;
      if (lang.startsWith('ja')) return (n >= 0x3040 && n <= 0x30FF) || (n >= 0x4E00 && n <= 0x9FFF);
      if (lang.startsWith('zh')) return n >= 0x3400 && n <= 0x9FFF;
      if (lang.startsWith('ru')) return n >= 0x0400 && n <= 0x04FF;
      return (n >= 65 && n <= 90) || (n >= 97 && n <= 122);
    };
    return letters.filter(match).length / letters.length;
  };
  const uiOnly = t => /^(papago\+?|로그인|번역기록|즐겨찾기|용어집|번역 설정|google translate|google 번역|deepl|translator|텍스트|이미지|문서|웹사이트|영어 감지|한국어|영어|일본어|중국어|복사|copy)$/i.test(t);
  const candidates = [];
  const add = (el, bonus, reason) => {
    if (!visible(el)) return;
    const text = read(el);
    if (text.length < 2 || text.length > 900 || uiOnly(text)) return;
    const lower = text.toLowerCase();
    if (lower === sourceNorm) return;
    const r = el.getBoundingClientRect();
    const style = getComputedStyle(el);
    let score = bonus + targetRatio(text) * 160;
    if (r.left + r.width / 2 > innerWidth * 0.48) score += 34;
    const font = parseFloat(style.fontSize || '0');
    score += Math.min(30, Math.max(0, font - 11) * 1.7);
    score += Math.min(28, text.length / 5);
    if (el.matches('textarea,input,[contenteditable="true"],[role="textbox"]')) score += 85;
    if (text.length >= 8 && text.length <= 500) score += 18;
    if (sourceNorm && (lower.includes(sourceNorm) || sourceNorm.includes(lower))) score -= 130;
    candidates.push({ text, score, reason });
  };

  document.querySelectorAll('textarea,input,[contenteditable="true"],[role="textbox"]').forEach(el => add(el, 75, 'editor'));

  // Modern translator UIs often render the result as ordinary React/Vue text nodes.
  // Inspect leaf-ish text containers rather than relying on CSS class names that change frequently.
  document.querySelectorAll('p,span,div').forEach(el => {
    const childTextCount = [...el.children].filter(c => norm(c.innerText || c.textContent || '').length > 1).length;
    if (childTextCount <= 3) add(el, childTextCount === 0 ? 28 : 8, 'text');
  });

  // Use the site's Copy button as a stable semantic anchor without touching the Windows clipboard.
  // We inspect nearby text because synthetic clipboard clicks are often blocked and would overwrite user clipboard data.
  const copyWords = /(복사|copy|클립보드|clipboard)/i;
  document.querySelectorAll('button,[role="button"],[aria-label],[title]').forEach(btn => {
    const label = norm(`${btn.getAttribute('aria-label') || ''} ${btn.getAttribute('title') || ''} ${btn.innerText || ''}`);
    if (!copyWords.test(label) || !visible(btn)) return;
    const br = btn.getBoundingClientRect();
    if (br.left + br.width / 2 < innerWidth * 0.42) return;
    let node = btn.parentElement;
    for (let depth = 0; node && depth < 6; depth++, node = node.parentElement) {
      node.querySelectorAll('textarea,[contenteditable="true"],[role="textbox"],p,span,div').forEach(el => {
        if (el === btn || el.contains(btn)) return;
        add(el, 105 - depth * 12, 'copy-anchor');
      });
    }
  });

  candidates.sort((a, b) => b.score - a.score);
  return candidates.length ? candidates[0].text : '';
})()
""";
    }

    public static string GoogleUrl(string text, string target) =>
        $"https://translate.google.com/?sl=auto&tl={Uri.EscapeDataString(MapGoogle(target))}&text={Uri.EscapeDataString(text)}&op=translate";

    public static string PapagoUrl(string text, string target) =>
        $"https://papago.naver.com/?sk=auto&tk={Uri.EscapeDataString(MapPapago(target))}&st={Uri.EscapeDataString(text)}";

    public static string DeepLUrl(string text, string target) =>
        $"https://www.deepl.com/translator#auto/{Uri.EscapeDataString(MapDeepL(target))}/{Uri.EscapeDataString(text)}";

    public static string GoogleHome => "https://translate.google.com/";
    public static string PapagoHome => "https://papago.naver.com/";
    public static string DeepLHome => "https://www.deepl.com/translator";

    private static string MapGoogle(string target) => target switch
    {
        "zh" => "zh-CN",
        _ => target
    };

    private static string MapPapago(string target) => target switch
    {
        "zh" => "zh-CN",
        _ => target
    };

    private static string MapDeepL(string target) => target switch
    {
        "zh-CN" or "zh" => "zh",
        _ => target.ToLowerInvariant()
    };
}
