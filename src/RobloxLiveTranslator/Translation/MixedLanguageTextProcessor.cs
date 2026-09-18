using System.Text;

namespace RobloxLiveTranslator.Translation;

public sealed record PreparedTranslationText(
    string OriginalText,
    string TranslationInput,
    bool SkipTranslation,
    bool MixedTargetAndForeign,
    string Reason);

/// <summary>
/// Keeps text that is already in the requested target language from confusing the translator.
/// This is especially useful for bilingual pages such as "Any good ideas? 좋은 생각 있어?".
/// OCR still reads both scripts; only the foreign-language runs are sent to translation.
/// </summary>
public static class MixedLanguageTextProcessor
{
    public static PreparedTranslationText Prepare(
        string text,
        string targetLanguage,
        bool enabled)
    {
        var original = Normalize(text);
        if (string.IsNullOrWhiteSpace(original))
            return new PreparedTranslationText(original, string.Empty, true, false, "empty");

        if (!enabled)
            return new PreparedTranslationText(original, original, false, false, "disabled");

        var target = TranslationLanguages.Normalize(targetLanguage, false);
        var letters = original.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
            return new PreparedTranslationText(original, original, false, false, "no-letters");

        var targetLetters = letters.Count(c => IsTargetScript(c, target));
        var foreignLetters = letters.Length - targetLetters;

        // Nothing is already in the target script: translate the whole OCR result.
        if (targetLetters == 0)
            return new PreparedTranslationText(original, original, false, false, "foreign-only");

        // The ROI already contains only the requested target language. Do not translate it again.
        if (foreignLetters == 0)
            return new PreparedTranslationText(original, string.Empty, true, false, "already-target-language");

        // Latin target languages cannot be reliably separated from one another by Unicode script.
        // We still keep automatic source detection, but do not strip Latin runs in that case.
        if (IsLatinTarget(target))
            return new PreparedTranslationText(original, original, false, true, "mixed-latin-target");

        var extracted = ExtractForeignRuns(original, target);
        if (string.IsNullOrWhiteSpace(extracted))
            return new PreparedTranslationText(original, string.Empty, true, true, "target-only-after-filter");

        return new PreparedTranslationText(original, extracted, false, true, "mixed-filtered");
    }

    private static string ExtractForeignRuns(string text, string targetLanguage)
    {
        var runs = new List<string>();
        var current = new StringBuilder();
        bool? currentIsTarget = null;

        void Flush()
        {
            if (current.Length == 0) return;
            var value = CleanRun(current.ToString());
            if (currentIsTarget == false && value.Count(char.IsLetterOrDigit) >= 2)
                runs.Add(value);
            current.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsLetter(c))
            {
                var isTarget = IsTargetScript(c, targetLanguage);
                if (currentIsTarget is not null && currentIsTarget != isTarget)
                    Flush();
                currentIsTarget = isTarget;
                current.Append(c);
                continue;
            }

            // Digits/punctuation belong to the surrounding phrase. If no phrase has started yet,
            // look ahead to determine which script the neutral prefix belongs to.
            if (currentIsTarget is null)
            {
                var nextLetter = text.Skip(i + 1).FirstOrDefault(char.IsLetter);
                currentIsTarget = nextLetter != default && IsTargetScript(nextLetter, targetLanguage);
            }
            current.Append(c);
        }
        Flush();

        return Normalize(string.Join(" ", runs));
    }

    private static string CleanRun(string value)
    {
        value = value.Trim();
        value = value.Trim('|', '/', '\\', '-', '·', '•', ':', ';', ',', ' ');
        return value.Trim();
    }

    public static bool IsTargetScript(char c, string language) => TranslationLanguages.Normalize(language, false) switch
    {
        "ko" => c is >= '\uAC00' and <= '\uD7A3' || c is >= '\u1100' and <= '\u11FF' || c is >= '\u3130' and <= '\u318F',
        "ja" => c is >= '\u3040' and <= '\u30FF' || c is >= '\u4E00' and <= '\u9FFF',
        "zh-CN" or "zh-TW" => c is >= '\u3400' and <= '\u9FFF',
        "ru" => c is >= '\u0400' and <= '\u04FF',
        "th" => c is >= '\u0E00' and <= '\u0E7F',
        "hi" => c is >= '\u0900' and <= '\u097F',
        "ar" => c is >= '\u0600' and <= '\u06FF' || c is >= '\u0750' and <= '\u077F',
        "en" or "es" or "fr" or "de" or "pt" or "it" or "vi" or "id" =>
            c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z' || c is >= '\u00C0' and <= '\u024F',
        _ => false
    };

    private static bool IsLatinTarget(string language) => TranslationLanguages.Normalize(language, false) is
        "en" or "es" or "fr" or "de" or "pt" or "it" or "vi" or "id";

    private static string Normalize(string value) =>
        string.Join(' ', value.Replace('\r', ' ').Replace('\n', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
