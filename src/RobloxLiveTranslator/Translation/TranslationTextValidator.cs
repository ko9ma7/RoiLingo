namespace RobloxLiveTranslator.Translation;

public static class TranslationTextValidator
{
    private static readonly string[] LanguageNames =
    [
        "한국어", "영어", "일본어", "중국어", "중국어(간체)", "중국어(번체)", "스페인어",
        "프랑스어", "독일어", "러시아어", "포르투갈어", "이탈리아어", "베트남어", "태국어",
        "인도네시아어", "힌디어", "아랍어", "english", "korean", "japanese", "chinese",
        "spanish", "french", "german", "russian", "portuguese", "italian"
    ];

    private static readonly string[] ChromePhrases =
    [
        "감지된 언어가 없습니다", "입력 언어를 확인해 주세요", "번역 방법", "텍스트 이미지 문서",
        "플러스 소개", "번역 설정", "번역기록", "언어 감지", "언어 선택",
        "[활성화된 번역 제공자에서 결과를 얻지 못했습니다]", "[활성화된 번역 제공자가 없습니다]"
    ];

    public static bool IsUsable(string sourceText, string? translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText)) return false;
        var text = Normalize(translatedText);
        if (text.Length < 1 || text.Length > 4000) return false;

        var lower = text.ToLowerInvariant();
        if (ChromePhrases.Any(p => lower.Contains(p.ToLowerInvariant(), StringComparison.Ordinal)))
            return false;

        var languageHits = LanguageNames.Count(name => lower.Contains(name.ToLowerInvariant(), StringComparison.Ordinal));
        if (languageHits >= 3) return false;

        string[] exactUi =
        [
            "papago", "papago+", "로그인", "번역기록", "즐겨찾기", "용어집", "번역 설정",
            "google 번역", "google translate", "deepl", "translator", "텍스트", "이미지", "문서", "웹사이트",
            "copy", "복사", "share", "공유"
        ];
        if (text.Length <= 120 && exactUi.Any(x => lower.Equals(x, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Do not reject an untranslated proper noun/very short game label here. Provider-specific
        // validation already checks target script; this common guard only blocks obvious page chrome/failure text.
        return true;
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
