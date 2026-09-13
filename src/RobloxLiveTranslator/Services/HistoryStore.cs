using RobloxLiveTranslator.Models;

namespace RobloxLiveTranslator.Services;

public sealed class HistoryStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string HistoryDirectory { get; }
    public string RuntimeLogDirectory { get; }

    public HistoryStore(string appDirectory)
    {
        HistoryDirectory = Path.Combine(appDirectory, "history");
        RuntimeLogDirectory = Path.Combine(appDirectory, "logs");
        Directory.CreateDirectory(HistoryDirectory);
        Directory.CreateDirectory(RuntimeLogDirectory);
    }

    public async Task AppendTranslationAsync(TranslationHistoryRecord record, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(HistoryDirectory, $"translations-{record.Timestamp:yyyy-MM-dd}.jsonl");
        var line = JsonSerializer.Serialize(record, _json);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendRuntimeAsync(string line, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(RuntimeLogDirectory, $"runtime-{DateTimeOffset.Now:yyyy-MM-dd}.log");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TranslationHistoryRecord>> LoadRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 10, 5000);
        var output = new List<TranslationHistoryRecord>(limit);
        var files = Directory.EnumerateFiles(HistoryDirectory, "translations-*.jsonl")
            .OrderByDescending(Path.GetFileName)
            .Take(14)
            .ToArray();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = await File.ReadAllLinesAsync(file, cancellationToken);
            for (var i = lines.Length - 1; i >= 0 && output.Count < limit; i--)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                try
                {
                    var item = JsonSerializer.Deserialize<TranslationHistoryRecord>(lines[i], _json);
                    if (item is not null) output.Add(item);
                }
                catch (JsonException)
                {
                    // One malformed line must not make the rest of the history unreadable.
                }
            }

            if (output.Count >= limit) break;
        }

        return output;
    }

    public async Task<string> ExportCsvAsync(IEnumerable<TranslationHistoryRecord> records, CancellationToken cancellationToken = default)
    {
        var exportDirectory = Path.Combine(SettingsStore.AppDirectory, "exports");
        Directory.CreateDirectory(exportDirectory);
        var path = Path.Combine(exportDirectory, $"translations-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.csv");
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Session,ROI,Source,Translation,Provider,OCR Confidence,Agreement,All Providers");
        foreach (var record in records.OrderBy(x => x.Timestamp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var providers = string.Join(" | ", record.Results.Select(x => $"{x.Provider}: {x.Text}"));
            sb.Append(Csv(record.Timestamp.ToString("O"))).Append(',')
              .Append(Csv(record.SessionId)).Append(',')
              .Append(Csv(record.RoiName)).Append(',')
              .Append(Csv(record.SourceText)).Append(',')
              .Append(Csv(record.TargetText)).Append(',')
              .Append(Csv(record.Provider)).Append(',')
              .Append(record.OcrConfidence.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(record.AgreementScore.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(providers)).AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true), cancellationToken);
        return path;
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
