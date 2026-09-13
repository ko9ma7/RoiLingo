using System.Net.Http;
namespace RobloxLiveTranslator.Services;

public sealed class ModelManager
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    public string TessDataDirectory { get; }

    public ModelManager()
    {
        TessDataDirectory = Path.Combine(SettingsStore.AppDirectory, "tessdata");
    }

    public async Task EnsureLanguagesAsync(string languages, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(TessDataDirectory);
        foreach (var lang in ParseLanguages(languages))
        {
            var file = Path.Combine(TessDataDirectory, $"{lang}.traineddata");
            if (File.Exists(file) && new FileInfo(file).Length > 100_000) continue;

            progress?.Report($"OCR 모델 다운로드: {lang}");
            var url = $"https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/{Uri.EscapeDataString(lang)}.traineddata";
            var tempFile = file + ".download";
            try
            {
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var output = File.Create(tempFile))
                    await input.CopyToAsync(output, cancellationToken);

                if (new FileInfo(tempFile).Length <= 100_000)
                    throw new InvalidDataException($"다운로드한 OCR 모델이 비정상적으로 작습니다: {lang}");

                File.Move(tempFile, file, true);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
    }

    public static IReadOnlyList<string> ParseLanguages(string languages)
        => languages.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
