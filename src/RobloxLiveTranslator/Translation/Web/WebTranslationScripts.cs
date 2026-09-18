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
    '.target_editarea textarea',
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
  const copyRe = /(복사|copy|클립보드|clipboard)/i;
  const visible = el => {
    if (!el || !el.getBoundingClientRect) return false;
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 2 && r.height > 2 &&
           r.bottom >= 0 && r.right >= 0 && r.top <= innerHeight && r.left <= innerWidth &&
           s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0;
  };

  const buttons = [...document.querySelectorAll('button,[role="button"]')].filter(visible);
  let best = null;
  let bestScore = -1;

  for (const btn of buttons) {
    const r = btn.getBoundingClientRect();
    const centerX = r.left + r.width / 2;
    const centerY = r.top + r.height / 2;
    const label = norm([
      btn.getAttribute('aria-label') || '',
      btn.getAttribute('title') || '',
      btn.getAttribute('data-tooltip') || '',
      btn.getAttribute('data-testid') || '',
      btn.innerText || ''
    ].join(' '));

    let score = -1;
    if (copyRe.test(label)) {
      score = 250;
      if (centerX > innerWidth * 0.50) score += 160;
      if (centerX > innerWidth * 0.70) score += 70;
      if (centerY > innerHeight * 0.35) score += 25;
    } else {
      // Some translator builds expose the copy icon without an accessible label.
      // In that case use the geometry of the target-side action row. The side navigation
      // is excluded by keeping the candidate left of ~94% of the viewport.
      const iconOnly = !!btn.querySelector('svg') && norm(btn.innerText || '') === '';
      const small = r.width >= 18 && r.width <= 72 && r.height >= 18 && r.height <= 72;
      const targetActionRow = centerX > innerWidth * 0.58 && centerX < innerWidth * 0.94 &&
                              centerY > innerHeight * 0.42 && centerY < innerHeight * 0.90;
      if (iconOnly && small && targetActionRow) {
        score = 40 + (centerX / Math.max(1, innerWidth)) * 100;
      }
    }

    if (score > bestScore) {
      bestScore = score;
      best = btn;
    }
  }

  if (!best || bestScore < 60) return false;
  best.click();
  return true;
})()
""";

    public static string BuildAnchoredTargetResultScript(string sourceText, string targetLanguage, string providerName)
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
    return r.width > 2 && r.height > 2 &&
           r.bottom >= 0 && r.right >= 0 && r.top <= innerHeight && r.left <= innerWidth &&
           s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0;
  };
  const read = el => norm((typeof el.value === 'string' && el.value) || el.innerText || el.textContent || '');
  const languageMenu = text => {
    const t = text.toLowerCase();
    const names = [
      '한국어','영어','일본어','중국어','스페인어','프랑스어','독일어','러시아어','포르투갈어',
      '이탈리아어','베트남어','태국어','인도네시아어','힌디어','아랍어',
      'english','korean','japanese','chinese','spanish','french','german','russian'
    ];
    let hits = 0;
    for (const n of names) if (t.includes(n.toLowerCase())) hits++;
    return hits >= 3;
  };
  const chrome = text => {
    const t = text.toLowerCase();
    if (languageMenu(t)) return true;
    return [
      '감지된 언어가 없습니다', '입력 언어를 확인해 주세요', '번역 방법',
      '텍스트 이미지 문서', '번역 설정', '번역기록', '플러스 소개'
    ].some(x => t.includes(x));
  };
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
      if (lang.startsWith('th')) return n >= 0x0E00 && n <= 0x0E7F;
      if (lang.startsWith('hi')) return n >= 0x0900 && n <= 0x097F;
      if (lang.startsWith('ar')) return (n >= 0x0600 && n <= 0x06FF) || (n >= 0x0750 && n <= 0x077F);
      return (n >= 65 && n <= 90) || (n >= 97 && n <= 122) || (n >= 0x00C0 && n <= 0x024F);
    };
    return letters.filter(match).length / letters.length;
  };

  const all = [...document.querySelectorAll('textarea,input,[contenteditable="true"],[role="textbox"],p,span,div')]
    .filter(visible)
    .map(el => {
      const text = read(el);
      const r = el.getBoundingClientRect();
      return {
        el, text, r,
        font: parseFloat(getComputedStyle(el).fontSize || '0') || 0,
        childTextCount: [...el.children].filter(c => norm(c.innerText || c.textContent || '').length > 1).length
      };
    })
    .filter(x => x.text.length >= 2 && x.text.length <= 1000);

  // Locate the source editor/text. Exact normalized matching is deliberately preferred over
  // guessing by CSS class because translator sites change generated class names frequently.
  const sourceCandidates = all
    .map(x => {
      const lower = x.text.toLowerCase();
      let score = 0;
      if (lower === sourceNorm) score = 1000;
      else if (sourceNorm.length >= 8 && (lower.includes(sourceNorm) || sourceNorm.includes(lower)))
        score = 700 - Math.abs(lower.length - sourceNorm.length);
      else return null;
      if (x.el.matches('textarea,input,[contenteditable="true"],[role="textbox"]')) score += 120;
      score += Math.min(40, x.font * 2);
      return { ...x, score };
    })
    .filter(Boolean)
    .sort((a,b) => b.score - a.score);

  const src = sourceCandidates.length ? sourceCandidates[0] : null;
  if (!src) return '';
  const srcCenterX = src.r.left + src.r.width / 2;
  const srcCenterY = src.r.top + src.r.height / 2;

  const candidates = [];
  for (const x of all) {
    const text = x.text;
    if (text.toLowerCase() === sourceNorm || chrome(text)) continue;
    if (x.childTextCount > 4 && !x.el.matches('textarea,input,[contenteditable="true"],[role="textbox"]')) continue;

    const centerX = x.r.left + x.r.width / 2;
    const centerY = x.r.top + x.r.height / 2;
    const onTargetSide = src ? centerX > srcCenterX + 40 : centerX > innerWidth * 0.50;
    if (!onTargetSide) continue;

    let score = targetRatio(text) * 190;
    if (text.length >= 4 && text.length <= 700) score += 25;
    score += Math.min(35, text.length / 5);
    score += Math.min(30, Math.max(0, x.font - 11) * 1.8);
    if (x.el.matches('textarea,input,[contenteditable="true"],[role="textbox"]')) score += 90;

    if (src) {
      const verticalDistance = Math.abs(centerY - srcCenterY);
      if (verticalDistance <= 45) score += 150;
      else if (verticalDistance <= 110) score += 100;
      else if (verticalDistance <= 220) score += 40;
      else score -= Math.min(120, verticalDistance / 3);

      const fontDiff = Math.abs(x.font - src.font);
      if (fontDiff <= 3) score += 25;
      if (x.r.top >= src.r.top - 80 && x.r.top <= src.r.bottom + 220) score += 55;
    } else {
      if (x.r.top > innerHeight * 0.18 && x.r.top < innerHeight * 0.80) score += 30;
    }

    candidates.push({ text, score });
  }

  candidates.sort((a,b) => b.score - a.score);
  return candidates.length && candidates[0].score >= 85 ? candidates[0].text : '';
})()
""";
    }

    public static string BuildTextNodeTargetResultScript(string sourceText, string targetLanguage, string providerName)
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
  const visibleRect = r => r && r.width > 1 && r.height > 1 && r.bottom >= 0 && r.right >= 0 && r.top <= innerHeight && r.left <= innerWidth;
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
      if (lang.startsWith('th')) return n >= 0x0E00 && n <= 0x0E7F;
      if (lang.startsWith('hi')) return n >= 0x0900 && n <= 0x097F;
      if (lang.startsWith('ar')) return (n >= 0x0600 && n <= 0x06FF) || (n >= 0x0750 && n <= 0x077F);
      if (lang.startsWith('el')) return n >= 0x0370 && n <= 0x03FF;
      return (n >= 65 && n <= 90) || (n >= 97 && n <= 122) || (n >= 0x00C0 && n <= 0x024F);
    };
    return letters.filter(match).length / letters.length;
  };
  const chrome = text => {
    const t = norm(text).toLowerCase();
    if (!t) return true;
    const exact = ['papago','papago+','로그인','번역기록','즐겨찾기','용어집','번역 설정','텍스트','이미지','문서','웹사이트','영어 감지','복사','copy','공유','share'];
    if (exact.includes(t)) return true;
    const names = ['한국어','영어','일본어','중국어','스페인어','프랑스어','독일어','러시아어','포르투갈어','이탈리아어','베트남어','태국어','인도네시아어','힌디어','아랍어','english','korean','japanese','chinese','spanish','french','german','russian'];
    let hits = 0;
    for (const n of names) if (t.includes(n)) hits++;
    if (hits >= 3) return true;
    return ['감지된 언어가 없습니다','입력 언어를 확인해 주세요','번역 방법','플러스 소개','언어 선택'].some(x => t.includes(x));
  };

  const items = [];
  const walker = document.createTreeWalker(document.body || document.documentElement, NodeFilter.SHOW_TEXT);
  let node;
  while ((node = walker.nextNode())) {
    const text = norm(node.nodeValue || '');
    if (text.length < 2 || text.length > 1400 || chrome(text)) continue;
    const parent = node.parentElement;
    if (!parent) continue;
    const style = getComputedStyle(parent);
    if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity || 1) <= 0) continue;
    const range = document.createRange();
    range.selectNodeContents(node);
    const r = range.getBoundingClientRect();
    if (!visibleRect(r)) continue;
    items.push({
      text,
      lower: text.toLowerCase(),
      left: r.left,
      top: r.top,
      width: r.width,
      height: r.height,
      cx: r.left + r.width / 2,
      cy: r.top + r.height / 2,
      font: parseFloat(style.fontSize || '0') || 0,
      weight: parseInt(style.fontWeight || '400', 10) || 400
    });
    if (items.length >= 1800) break;
  }

  // Text nodes are used instead of element.innerText so a large container that also contains
  // the language picker/navigation cannot win over the actual translated sentence.
  let src = null;
  let srcScore = -1;
  for (const x of items) {
    let score = -1;
    if (x.lower === sourceNorm) score = 1000;
    else if (sourceNorm.length >= 8 && (x.lower.includes(sourceNorm) || sourceNorm.includes(x.lower)))
      score = 700 - Math.abs(x.lower.length - sourceNorm.length);
    if (score < 0) continue;
    if (x.cx < innerWidth * 0.60) score += 120;
    score += Math.min(50, x.font * 2);
    if (score > srcScore) { src = x; srcScore = score; }
  }

  if (!src) return '';
  const srcX = src.cx;
  const srcY = src.cy;
  let best = null;
  let bestScore = -1e9;
  for (const x of items) {
    if (x.lower === sourceNorm) continue;
    if (sourceNorm.length >= 10 && (x.lower.includes(sourceNorm) || sourceNorm.includes(x.lower))) continue;
    if (x.cx <= Math.max(innerWidth * 0.48, srcX + 40)) continue;

    const ratio = targetRatio(x.text);
    const nonLatinTarget = /^(ko|ja|zh|ru|el)/i.test(target);
    if (nonLatinTarget && ratio < 0.10) continue;

    let score = ratio * 260;
    score += Math.min(55, x.text.length / 3.5);
    score += Math.min(35, Math.max(0, x.font - 11) * 2.1);
    if (x.weight >= 500) score += 12;
    if (x.text.length >= 8 && x.text.length <= 900) score += 35;

    const dy = Math.abs(x.cy - srcY);
    if (dy <= 50) score += 170;
    else if (dy <= 120) score += 115;
    else if (dy <= 240) score += 45;
    else score -= Math.min(130, dy / 2.5);

    // The translation editor normally occupies the central right pane; side navigation is farther right.
    if (x.cx > innerWidth * 0.52 && x.cx < innerWidth * 0.92) score += 55;
    if (x.top > innerHeight * 0.18 && x.top < innerHeight * 0.82) score += 25;

    if (score > bestScore) { best = x; bestScore = score; }
  }

  if (!best || bestScore < 105) return '';

  // If a translator splits one paragraph into several sibling text nodes, join only nearby
  // target-side nodes with a similar font. This preserves a full sentence without pulling in
  // the language picker or the lower action toolbar.
  const band = items
    .filter(x => x.cx > Math.max(innerWidth * 0.48, srcX + 40))
    .filter(x => Math.abs(x.cy - best.cy) <= Math.max(95, best.height * 3.5))
    .filter(x => Math.abs(x.font - best.font) <= 4)
    .filter(x => !chrome(x.text))
    .filter(x => !/^(ko|ja|zh|ru|el)/i.test(target) || targetRatio(x.text) >= 0.10)
    .sort((a,b) => Math.abs(a.top - b.top) < 4 ? a.left - b.left : a.top - b.top);

  const parts = [];
  for (const x of band) {
    if (!parts.includes(x.text)) parts.push(x.text);
  }
  const combined = norm(parts.join(' '));
  const maxCombined = Math.max(1200, sourceNorm.length * 5);
  if (combined.length > best.text.length && combined.length <= maxCombined && !chrome(combined))
    return combined;
  return best.text;
})()
""";
    }

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
      if (lang.startsWith('th')) return n >= 0x0E00 && n <= 0x0E7F;
      if (lang.startsWith('hi')) return n >= 0x0900 && n <= 0x097F;
      if (lang.startsWith('ar')) return (n >= 0x0600 && n <= 0x06FF) || (n >= 0x0750 && n <= 0x077F);
      return (n >= 65 && n <= 90) || (n >= 97 && n <= 122) || (n >= 0x00C0 && n <= 0x024F);
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

    public static string GoogleUrl(string text, string source, string target) =>
        $"https://translate.google.com/?sl={Uri.EscapeDataString(MapSource(source, TranslationLanguages.ToGoogle))}&tl={Uri.EscapeDataString(TranslationLanguages.ToGoogle(target))}&text={Uri.EscapeDataString(text)}&op=translate";

    public static string PapagoUrl(string text, string source, string target) =>
        $"https://papago.naver.com/?sk={Uri.EscapeDataString(MapSource(source, TranslationLanguages.ToPapago))}&tk={Uri.EscapeDataString(TranslationLanguages.ToPapago(target))}&st={Uri.EscapeDataString(text)}";

    public static string DeepLUrl(string text, string source, string target) =>
        $"https://www.deepl.com/translator#{Uri.EscapeDataString(MapSource(source, TranslationLanguages.ToDeepL))}/{Uri.EscapeDataString(TranslationLanguages.ToDeepL(target))}/{Uri.EscapeDataString(text)}";

    public static string GoogleHome => "https://translate.google.com/";
    public static string PapagoHome => "https://papago.naver.com/";
    public static string DeepLHome => "https://www.deepl.com/translator";

    private static string MapSource(string source, Func<string, string> mapper)
    {
        var normalized = TranslationLanguages.Normalize(source);
        return normalized == "auto" ? "auto" : mapper(normalized);
    }
}
