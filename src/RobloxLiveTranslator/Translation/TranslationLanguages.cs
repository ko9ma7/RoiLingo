namespace RobloxLiveTranslator.Translation;

public static class TranslationLanguages
{
    public static string Normalize(string? value, bool allowAuto = true)
    {
        var v = (value ?? string.Empty).Trim();
        if (allowAuto && (v.Length == 0 || v.Equals("auto", StringComparison.OrdinalIgnoreCase))) return "auto";
        return v.ToLowerInvariant() switch
        {
            "ko" or "kr" => "ko",
            "en" => "en",
            "ja" or "jp" => "ja",
            "zh" or "zh-cn" or "zh-hans" => "zh-CN",
            "zh-tw" or "zh-hant" => "zh-TW",
            "es" => "es",
            "fr" => "fr",
            "de" => "de",
            "ru" => "ru",
            "pt" or "pt-br" or "pt-pt" => "pt",
            "it" => "it",
            "vi" => "vi",
            "th" => "th",
            "id" => "id",
            "hi" => "hi",
            "ar" => "ar",
            _ => allowAuto ? "auto" : "ko"
        };
    }

    public static string ToTesseract(string language) => Normalize(language) switch
    {
        "ko" => "kor",
        "en" => "eng",
        "ja" => "jpn",
        "zh-CN" => "chi_sim",
        "zh-TW" => "chi_tra",
        "es" => "spa",
        "fr" => "fra",
        "de" => "deu",
        "ru" => "rus",
        "pt" => "por",
        "it" => "ita",
        "vi" => "vie",
        "th" => "tha",
        "id" => "ind",
        "hi" => "hin",
        "ar" => "ara",
        _ => "eng"
    };

    public static string ToGoogle(string language) => Normalize(language, false) switch
    {
        "zh-CN" => "zh-CN",
        "zh-TW" => "zh-TW",
        var v => v
    };

    public static string ToPapago(string language) => Normalize(language, false) switch
    {
        "zh-CN" => "zh-CN",
        "zh-TW" => "zh-TW",
        var v => v
    };

    public static string ToDeepL(string language) => Normalize(language, false) switch
    {
        "zh-CN" => "zh-hans",
        "zh-TW" => "zh-hant",
        "en" => "en",
        var v => v.ToLowerInvariant()
    };

    public static string ToDeepLApi(string language) => Normalize(language, false) switch
    {
        "ko" => "KO",
        "ja" => "JA",
        "zh-CN" => "ZH-HANS",
        "zh-TW" => "ZH-HANT",
        "en" => "EN-US",
        "pt" => "PT-BR",
        var v => v.ToUpperInvariant()
    };

    public static string ToDeepLApiSource(string language) => Normalize(language, false) switch
    {
        "zh-CN" or "zh-TW" => "ZH",
        "en" => "EN",
        "pt" => "PT",
        var v => v.ToUpperInvariant()
    };

    public static string DisplayName(string language) => Normalize(language) switch
    {
        "auto" => "자동 감지",
        "ko" => "한국어",
        "en" => "영어",
        "ja" => "일본어",
        "zh-CN" => "중국어(간체)",
        "zh-TW" => "중국어(번체)",
        "es" => "스페인어",
        "fr" => "프랑스어",
        "de" => "독일어",
        "ru" => "러시아어",
        "pt" => "포르투갈어",
        "it" => "이탈리아어",
        "vi" => "베트남어",
        "th" => "태국어",
        "id" => "인도네시아어",
        "hi" => "힌디어",
        "ar" => "아랍어",
        _ => language
    };
}
