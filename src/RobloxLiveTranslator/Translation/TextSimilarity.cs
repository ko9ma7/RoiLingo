namespace RobloxLiveTranslator.Translation;

public static class TextSimilarity
{
    public static double Ratio(string? a, string? b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (a.Length == 0 && b.Length == 0) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return 1.0 - prev[b.Length] / (double)Math.Max(a.Length, b.Length);
    }

    private static string Normalize(string? s)
        => new((s ?? "").Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).Select(char.ToLowerInvariant).ToArray());
}
