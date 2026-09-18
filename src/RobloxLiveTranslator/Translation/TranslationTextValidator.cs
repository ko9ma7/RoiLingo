namespace RobloxLiveTranslator.Translation;

public static class TranslationTextValidator
{
    private static readonly string[] LanguageNames =
    [
        "한국어", "영어", "일본어", "중국어", "중국어(간체)", "중국어(번체)", "스페인어",
        "프랑스어", "독일어", "러시아어", "포르투갈어", "이탈리아어", "베트남어", "태국어",
        "인도네시아어", "힌디어", "아랍어", "english", "korean", "japanese", "chinese",
        "spanish", "french", "german", "russian", "portuguese", "italian", "vietnamese", "thai"
    ];

    private static readonly string[] ChromePhrases =
    [
        "감지된 언어가 없습니다", "입력 언어를 확인해 주세요", "번역 방법", "텍스트 이미지 문서",
        "플러스 소개", "번역 설정", "번역기록", "언어 감지", "언어 선택", "번역 결과",
        "웹에서 더 새로워진 파파고", "측면 패널", "저장된 번역",
        "[활성화된 번역 제공자에서 결과를 얻지 못했습니다]", "[활성화된 번역 제공자가 없습니다]"
    ];

    public static bool IsUsable(string sourceText, string? translatedText, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(translatedText)) return false;
        var text = Normalize(translatedText);
        if (text.Length < 1 || text.Length > 4000) return false;

        var lower = text.ToLowerInvariant();
        if (ChromePhrases.Any(p => lower.Contains(p.ToLowerInvariant(), StringComparison.Ordinal)))
            return false;

        var languageHits = LanguageNames.Count(name => lower.Contains(name.ToLowerInvariant(), StringComparison.Ordinal));
        if (languageHits >= 3) return false;
        if (text.Length <= 80 && LanguageNames.Any(name => lower.Equals(name.ToLowerInvariant(), StringComparison.Ordinal)))
            return false;

        string[] exactUi =
        [
            "papago", "papago+", "로그인", "번역기록", "즐겨찾기", "용어집", "번역 설정",
            "google 번역", "google translate", "deepl", "translator", "텍스트", "이미지", "문서", "웹사이트",
            "copy", "복사", "share", "공유", "번역 결과"
        ];
        if (text.Length <= 120 && exactUi.Any(x => lower.Equals(x, StringComparison.OrdinalIgnoreCase)))
            return false;

        var normalizedTarget = TranslationLanguages.Normalize(targetLanguage, false);
        var letters = text.Count(char.IsLetter);
        if (letters > 0)
        {
            var targetChars = text.Count(c => MatchesTargetScript(c, normalizedTarget));
            // Script validation is intentionally permissive enough for names/acronyms, but it prevents
            // a Korean UI label from being accepted as an English result (and vice versa).
            if (targetChars / (double)letters < 0.12) return false;
        }

        // For longer strings, an answer almost identical to the source is usually an unfinished web translation.
        if (sourceText.Length >= 12 && TextSimilarity.Ratio(Normalize(sourceText), text) > 0.96)
            return false;

        return true;
    }

    public static bool IsUsable(string sourceText, string? translatedText) =>
        IsUsable(sourceText, translatedText, "en");

    private static bool MatchesTargetScript(char c, string language) => language switch
    {
        "ko" => c is >= '\uAC00' and <= '\uD7A3',
        "ja" => c is >= '\u3040' and <= '\u30FF' || c is >= '\u4E00' and <= '\u9FFF',
        "zh-CN" or "zh-TW" => c is >= '\u3400' and <= '\u9FFF',
        "ru" => c is >= '\u0400' and <= '\u04FF',
        "th" => c is >= '\u0E00' and <= '\u0E7F',
        "hi" => c is >= '\u0900' and <= '\u097F',
        "ar" => c is >= '\u0600' and <= '\u06FF' || c is >= '\u0750' and <= '\u077F',
        "en" or "es" or "fr" or "de" or "pt" or "it" or "vi" or "id" =>
            c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z' || c is >= '\u00C0' and <= '\u024F',
        _ => char.IsLetter(c)
    };

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
