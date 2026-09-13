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
        var known = await ExecuteStringScriptAsync(core, knownSelectorScript);
        if (IsUsable(known, sourceText)) return (Normalize(known), "known-selector");

        var snapshot = await TryVisibleDomSnapshotAsync(core, sourceText, targetLanguage);
        if (IsUsable(snapshot, sourceText)) return (Normalize(snapshot), "visible-dom-snapshot");

        var heuristicScript = WebTranslationScripts.BuildHeuristicResultScript(sourceText, targetLanguage, providerName);
        var heuristic = await ExecuteStringScriptAsync(core, heuristicScript);
        if (IsUsable(heuristic, sourceText)) return (Normalize(heuristic), "dom-heuristic");

        var body = await ExecuteStringScriptAsync(core, "(() => document.body ? (document.body.innerText || '') : '')()");
        var bodyCandidate = ExtractFromBody(body, sourceText, targetLanguage);
        if (IsUsable(bodyCandidate, sourceText)) return (Normalize(bodyCandidate), "body-text");

        if (includeAccessibility)
        {
            var ax = await TryAccessibilityTreeAsync(core, sourceText, targetLanguage);
            if (IsUsable(ax, sourceText)) return (Normalize(ax), "accessibility-tree");
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
        var clicked = await ExecuteBoolScriptAsync(core, WebTranslationScripts.TargetCopyButtonClick);
        if (!clicked) return (string.Empty, "copy-button-not-found");

        await Task.Delay(180);
        var current = ReadClipboardText();
        if (string.IsNullOrWhiteSpace(current) || string.Equals(Normalize(current), Normalize(clipboardBefore), StringComparison.Ordinal))
            return (string.Empty, "copy-button-no-clipboard-change");
        var candidate = Normalize(current);
        if (!IsUsable(candidate, sourceText) || TargetScriptRatio(candidate, targetLanguage) < 0.20)
            return (string.Empty, "copy-button-unusable");

        // The copy-button fallback is an internal extraction technique; avoid permanently
        // replacing whatever the user had in the clipboard before the translation.
        TryRestoreClipboard(clipboardBefore, current);
        return (candidate, "copy-button-clipboard");
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
                if (!IsUsable(text, sourceText) || LooksLikeUiLabel(text)) continue;
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
            .Where(x => x.Length >= 2 && x.Length <= 800 && !LooksLikeUiLabel(x))
            .ToArray();

        string best = string.Empty;
        double bestScore = double.MinValue;
        for (var i = 0; i < lines.Length; i++)
        {
            for (var take = 1; take <= 3 && i + take <= lines.Length; take++)
            {
                var text = string.Join(' ', lines.Skip(i).Take(take));
                if (!IsUsable(text, sourceText)) continue;
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
        if (!IsUsable(text, sourceText) || text.Length > 1200 || text.Length < 2 || LooksLikeUiLabel(text)) return;
        var score = TargetScriptRatio(text, targetLanguage) * 150.0 + Math.Min(35, text.Length / 4.0);
        role = (role ?? string.Empty).ToLowerInvariant();
        if (role.Contains("textbox") || role.Contains("text field")) score += 100;
        else if (role.Contains("statictext") || role.Contains("paragraph")) score += 35;
        else if (role.Contains("generic")) score += 10;
        if (text.Length is >= 8 and <= 500) score += 20;
        if (TextSimilarity.Ratio(text, sourceText) > 0.72) score -= 160;
        if (score > bestScore) { bestScore = score; best = text; }
    }

    private static bool IsUsable(string? value, string sourceText)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = Normalize(value);
        var source = Normalize(sourceText);
        if (normalized.Length < 2) return false;
        return !string.Equals(normalized, source, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeUiLabel(string text)
    {
        var compact = text.Trim().ToLowerInvariant();
        if (compact.Length > 80) return false;
        string[] labels =
        [
            "papago", "papago+", "로그인", "번역기록", "즐겨찾기", "용어집", "번역 설정",
            "google 번역", "google translate", "deepl", "translator", "텍스트", "이미지", "문서", "웹사이트",
            "영어 감지", "한국어", "영어", "일본어", "중국어", "copy", "복사", "공유", "share"
        ];
        return labels.Any(label => compact.Equals(label, StringComparison.OrdinalIgnoreCase));
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
        if (language.StartsWith("el")) return c is >= '\u0370' and <= '\u03FF';
        return c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z';
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
