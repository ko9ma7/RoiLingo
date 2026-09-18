using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace RobloxLiveTranslator.Translation.Web;

internal static class WebResultExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<(string Text, string Method)> TryExtractAsync(
        CoreWebView2 core,
        string knownSelectorScript,
        string sourceText,
        string targetLanguage,
        string providerName,
        bool includeAccessibility)
    {
        // Only return evidence tied to the actual source/target editor. Earlier versions also
        // scanned the whole page body, which could mistake language menus and banners for output.
        var known = await ExecuteStringScriptAsync(core, knownSelectorScript);
        if (IsTranslationCandidate(known, sourceText, targetLanguage))
            return (Normalize(known), "known-selector");

        var textNodeScript = WebTranslationScripts.BuildTextNodeTargetResultScript(sourceText, targetLanguage, providerName);
        var textNode = await ExecuteStringScriptAsync(core, textNodeScript);
        if (IsTranslationCandidate(textNode, sourceText, targetLanguage))
            return (Normalize(textNode), "text-node-geometry");

        var anchoredScript = WebTranslationScripts.BuildAnchoredTargetResultScript(sourceText, targetLanguage, providerName);
        var anchored = await ExecuteStringScriptAsync(core, anchoredScript);
        if (IsTranslationCandidate(anchored, sourceText, targetLanguage))
            return (Normalize(anchored), "layout-anchor");

        if (includeAccessibility)
        {
            var ax = await TryAccessibilityTreeAsync(core, sourceText, targetLanguage);
            if (IsTranslationCandidate(ax, sourceText, targetLanguage))
                return (Normalize(ax), "accessibility-tree");
        }

        return (string.Empty, "none");
    }

    public static string ReadClipboardText()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static async Task<(string Text, string Method)> TryCopyButtonClipboardAsync(
        CoreWebView2 core,
        string sourceText,
        string targetLanguage,
        string clipboardBefore)
    {
        // A unique marker makes the probe reliable even when the user's clipboard already
        // contains the same translated sentence from an earlier request.
        var marker = $"RoiLingo:{Guid.NewGuid():N}";
        try
        {
            System.Windows.Clipboard.SetText(marker);
        }
        catch
        {
            // If clipboard ownership is temporarily unavailable, keep the previous behavior.
        }

        var clicked = await ExecuteBoolScriptAsync(core, WebTranslationScripts.TargetCopyButtonClick);
        if (!clicked)
        {
            TryRestoreClipboard(clipboardBefore, marker);
            return (string.Empty, "copy-button-not-found");
        }

        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(120);
            var current = ReadClipboardText();
            if (string.IsNullOrWhiteSpace(current) || string.Equals(current, marker, StringComparison.Ordinal))
                continue;

            var candidate = Normalize(current);
            TryRestoreClipboard(clipboardBefore, current);
            if (!IsTranslationCandidate(candidate, sourceText, targetLanguage))
                return (string.Empty, "copy-button-unusable");

            return (candidate, "copy-button-clipboard");
        }

        TryRestoreClipboard(clipboardBefore, ReadClipboardText());
        return (string.Empty, "copy-button-no-clipboard-change");
    }


    private static void TryRestoreClipboard(string previous, string current)
    {
        try
        {
            var now = ReadClipboardText();
            if (!string.Equals(now, current, StringComparison.Ordinal)) return;
            if (string.IsNullOrEmpty(previous)) System.Windows.Clipboard.Clear();
            else System.Windows.Clipboard.SetText(previous);
        }
        catch
        {
            // Clipboard ownership can change at any time; restoration is best-effort only.
        }
    }
    private static async Task<string> TryVisibleDomSnapshotAsync(CoreWebView2 core, string sourceText, string targetLanguage)
    {
        const string script = """
(() => {
  const norm = s => (s || '').replace(/\s+/g, ' ').trim();
  const visible = el => {
    if (!el || !el.getBoundingClientRect) return false;
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 2 && r.height > 2 && r.bottom >= 0 && r.right >= 0 &&
           r.top <= innerHeight && r.left <= innerWidth &&
           s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0;
  };
  const nodes = [];
  const selector = 'textarea,input,[contenteditable="true"],[role="textbox"],p,span,div';
  for (const el of document.querySelectorAll(selector)) {
    if (nodes.length >= 700 || !visible(el)) continue;
    const r = el.getBoundingClientRect();
    const text = norm((typeof el.value === 'string' && el.value) || el.innerText || el.textContent || '');
    if (text.length < 2 || text.length > 1200) continue;
    const meaningfulChildren = [...el.children].filter(c => norm(c.innerText || c.textContent || '').length > 1).length;
    if (!el.matches('textarea,input,[contenteditable="true"],[role="textbox"]') && meaningfulChildren > 3) continue;
    nodes.push({
      text,
      left: r.left,
      top: r.top,
      width: r.width,
      height: r.height,
      tag: (el.tagName || '').toLowerCase(),
      role: el.getAttribute('role') || '',
      label: el.getAttribute('aria-label') || el.getAttribute('title') || '',
      fontSize: parseFloat(getComputedStyle(el).fontSize || '0') || 0
    });
  }
  return nodes;
})()
""";

        try
        {
            var json = await core.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(json) || json == "null") return string.Empty;
            var nodes = JsonSerializer.Deserialize<List<DomCandidate>>(json, JsonOptions);
            if (nodes is null || nodes.Count == 0) return string.Empty;

            string best = string.Empty;
            double bestScore = double.MinValue;
            foreach (var node in nodes)
            {
                var text = Normalize(node.Text);
                if (!IsTranslationCandidate(text, sourceText, targetLanguage)) continue;
                var score = TargetScriptRatio(text, targetLanguage) * 180.0;
                score += Math.Min(40, text.Length / 4.0);
                if (node.Left + node.Width / 2 > 520) score += 18;
                if (node.Role.Contains("textbox", StringComparison.OrdinalIgnoreCase) || node.Tag is "textarea" or "input") score += 80;
                if (node.FontSize >= 16) score += Math.Min(28, (node.FontSize - 12) * 2);
                if (text.Length is >= 8 and <= 600) score += 25;
                if (TextSimilarity.Ratio(text, sourceText) > 0.70) score -= 180;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = text;
                }
            }
            return bestScore >= 55 ? best : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string ExtractFromBody(string body, string sourceText, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        var lines = body.Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(x => x.Length >= 2 && x.Length <= 800 && !LooksLikeTranslatorChromeText(x))
            .ToArray();

        string best = string.Empty;
        double bestScore = double.MinValue;
        for (var i = 0; i < lines.Length; i++)
        {
            for (var take = 1; take <= 3 && i + take <= lines.Length; take++)
            {
                var text = string.Join(' ', lines.Skip(i).Take(take));
                if (!IsTranslationCandidate(text, sourceText, targetLanguage)) continue;
                var ratio = TargetScriptRatio(text, targetLanguage);
                var score = ratio * 180 + Math.Min(45, text.Length / 4.0);
                if (TextSimilarity.Ratio(text, sourceText) > 0.70) score -= 180;
                if (text.Length is >= 10 and <= 600) score += 20;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = text;
                }
            }
        }
        return bestScore >= 65 ? best : string.Empty;
    }

    private static async Task<string> ExecuteStringScriptAsync(CoreWebView2 core, string script)
    {
        try
        {
            var result = await core.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(result) || result == "null" || result == "undefined") return string.Empty;
            return JsonSerializer.Deserialize<string>(result) ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static async Task<bool> ExecuteBoolScriptAsync(CoreWebView2 core, string script)
    {
        try
        {
            var result = await core.ExecuteScriptAsync(script);
            return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> TryAccessibilityTreeAsync(CoreWebView2 core, string sourceText, string targetLanguage)
    {
        try
        {
            var json = await core.CallDevToolsProtocolMethodAsync("Accessibility.getFullAXTree", "{}");
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
                return string.Empty;

            string best = string.Empty;
            double bestScore = double.MinValue;
            foreach (var node in nodes.EnumerateArray())
            {
                var role = ReadValue(node, "role");
                var value = ReadValue(node, "value");
                var name = ReadValue(node, "name");
                ScoreCandidate(value, role, sourceText, targetLanguage, ref best, ref bestScore);
                if (!string.Equals(name, value, StringComparison.Ordinal))
                    ScoreCandidate(name, role, sourceText, targetLanguage, ref best, ref bestScore);
            }
            return best;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadValue(JsonElement node, string property)
    {
        if (!node.TryGetProperty(property, out var wrapper) || wrapper.ValueKind != JsonValueKind.Object) return string.Empty;
        if (!wrapper.TryGetProperty("value", out var value)) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static void ScoreCandidate(string? raw, string? role, string sourceText, string targetLanguage, ref string best, ref double bestScore)
    {
        var text = Normalize(raw);
        if (!IsTranslationCandidate(text, sourceText, targetLanguage) || text.Length > 1200 || text.Length < 2) return;
        var score = TargetScriptRatio(text, targetLanguage) * 150.0 + Math.Min(35, text.Length / 4.0);
        role = (role ?? string.Empty).ToLowerInvariant();
        if (role.Contains("textbox") || role.Contains("text field")) score += 100;
        else if (role.Contains("statictext") || role.Contains("paragraph")) score += 35;
        else if (role.Contains("generic")) score += 10;
        if (text.Length is >= 8 and <= 500) score += 20;
        if (TextSimilarity.Ratio(text, sourceText) > 0.72) score -= 160;
        if (score > bestScore) { bestScore = score; best = text; }
    }

    internal static bool IsTranslationCandidate(string? value, string sourceText, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = Normalize(value);
        var source = Normalize(sourceText);
        if (normalized.Length < 2 || normalized.Length > 1800) return false;
        if (string.Equals(normalized, source, StringComparison.OrdinalIgnoreCase)) return false;
        if (TextSimilarity.Ratio(normalized, source) > 0.90) return false;
        if (LooksLikeTranslatorChromeText(normalized)) return false;

        // Require some evidence of the requested target script for every supported language.
        // This is the last guard against accepting translator chrome in the browser UI language.
        if (TargetScriptRatio(normalized, targetLanguage) < 0.12) return false;

        // A real translation is normally in the same order of magnitude as its source.
        // This rejects large navigation/menu dumps while preserving short UI/game strings.
        if (source.Length >= 12 && normalized.Length > Math.Max(900, source.Length * 6))
            return false;

        return true;
    }

    private static bool LooksLikeTranslatorChromeText(string text)
    {
        var compact = text.Trim().ToLowerInvariant();
        if (compact.Length == 0) return true;

        string[] exactLabels =
        [
            "papago", "papago+", "로그인", "번역기록", "즐겨찾기", "용어집", "번역 설정",
            "google 번역", "google translate", "deepl", "translator", "텍스트", "이미지", "문서", "웹사이트",
            "영어 감지", "언어 감지", "한국어", "영어", "일본어", "중국어", "중국어(간체)", "중국어(번체)",
            "스페인어", "프랑스어", "독일어", "러시아어", "포르투갈어", "이탈리아어", "베트남어",
            "태국어", "인도네시아어", "힌디어", "아랍어", "copy", "복사", "공유", "share", "번역 결과"
        ];
        if (compact.Length <= 100 && exactLabels.Any(label => compact.Equals(label, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Language pickers were the main false positive in v1.3.0. A result such as
        // "한국어 한국어 영어 일본어 중국어(간체) ..." is page chrome, not a translation.
        string[] languageNames =
        [
            "한국어", "영어", "일본어", "중국어", "중국어(간체)", "중국어(번체)", "스페인어",
            "프랑스어", "독일어", "러시아어", "포르투갈어", "이탈리아어", "베트남어",
            "태국어", "인도네시아어", "힌디어", "아랍어", "english", "korean",
            "japanese", "chinese", "spanish", "french", "german", "russian"
        ];
        var languageHits = languageNames.Count(name => compact.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (languageHits >= 3) return true;

        string[] chromePhrases =
        [
            "감지된 언어가 없습니다", "입력 언어를 확인해 주세요", "번역 방법", "텍스트 이미지 문서",
            "플러스 소개", "번역 설정", "번역기록", "언어 감지", "언어 선택",
            "웹에서 더 새로워진 파파고", "측면 패널", "저장된 번역", "번역 결과",
            "type the text to translate", "select target language", "select source language"
        ];
        if (chromePhrases.Any(p => compact.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static double TargetScriptRatio(string text, string targetLanguage)
    {
        var letters = 0;
        var target = 0;
        foreach (var c in text)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (MatchesTargetScript(c, targetLanguage)) target++;
        }
        return letters == 0 ? 0 : target / (double)letters;
    }

    private static bool MatchesTargetScript(char c, string language)
    {
        language = language.ToLowerInvariant();
        if (language.StartsWith("ko")) return c is >= '\uAC00' and <= '\uD7A3';
        if (language.StartsWith("ja")) return c is >= '\u3040' and <= '\u30FF' || c is >= '\u4E00' and <= '\u9FFF';
        if (language.StartsWith("zh")) return c is >= '\u3400' and <= '\u9FFF';
        if (language.StartsWith("ru")) return c is >= '\u0400' and <= '\u04FF';
        if (language.StartsWith("th")) return c is >= '\u0E00' and <= '\u0E7F';
        if (language.StartsWith("hi")) return c is >= '\u0900' and <= '\u097F';
        if (language.StartsWith("ar")) return c is >= '\u0600' and <= '\u06FF' || c is >= '\u0750' and <= '\u077F';
        return c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z' || c is >= '\u00C0' and <= '\u024F';
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private sealed class DomCandidate
    {
        public string Text { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Tag { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public double FontSize { get; set; }
    }
}
