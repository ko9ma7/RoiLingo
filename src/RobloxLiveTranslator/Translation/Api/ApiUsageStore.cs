namespace RobloxLiveTranslator.Translation.Api;

public sealed class ApiUsageStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, UsageRecord> _records;

    public ApiUsageStore(string appDirectory)
    {
        _path = Path.Combine(appDirectory, "api-usage.json");
        _records = Load();
    }

    public (bool Allowed, string Reason) CanUseSnapshot(
        string provider,
        int dailyRequestLimit,
        int monthlyCharacterLimit,
        int textLength)
    {
        lock (_records)
        {
            var record = GetNormalized(provider);
            if (dailyRequestLimit > 0 && record.DailyRequests >= dailyRequestLimit)
                return (false, $"일일 요청 한도 {dailyRequestLimit:N0}회 도달");
            if (monthlyCharacterLimit > 0 && record.MonthlyCharacters + textLength > monthlyCharacterLimit)
                return (false, $"월 문자 한도 {monthlyCharacterLimit:N0}자 도달");
            return (true, string.Empty);
        }
    }

    public async Task<(bool Allowed, string Reason)> CanUseAsync(
        string provider,
        int dailyRequestLimit,
        int monthlyCharacterLimit,
        int textLength,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            lock (_records)
            {
                var record = GetNormalized(provider);
                if (dailyRequestLimit > 0 && record.DailyRequests >= dailyRequestLimit)
                    return (false, $"일일 요청 한도 {dailyRequestLimit:N0}회 도달");
                if (monthlyCharacterLimit > 0 && record.MonthlyCharacters + textLength > monthlyCharacterLimit)
                    return (false, $"월 문자 한도 {monthlyCharacterLimit:N0}자 도달");
                return (true, string.Empty);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordAsync(string provider, int characters, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            lock (_records)
            {
                var record = GetNormalized(provider);
                record = record with
                {
                    DailyRequests = record.DailyRequests + 1,
                    MonthlyCharacters = record.MonthlyCharacters + Math.Max(0, characters)
                };
                _records[provider] = record;
                Save();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            lock (_records)
            {
                _records.Clear();
                Save();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyDictionary<string, UsageRecord> Snapshot()
    {
        lock (_records)
            return _records.ToDictionary(x => x.Key, x => x.Value with { });
    }

    private UsageRecord GetNormalized(string provider)
    {
        var today = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        var month = DateTimeOffset.Now.ToString("yyyy-MM");
        if (!_records.TryGetValue(provider, out var record))
            record = new UsageRecord(today, month, 0, 0);
        if (!string.Equals(record.Day, today, StringComparison.Ordinal))
            record = record with { Day = today, DailyRequests = 0 };
        if (!string.Equals(record.Month, month, StringComparison.Ordinal))
            record = record with { Month = month, MonthlyCharacters = 0 };
        _records[provider] = record;
        return record;
    }

    private Dictionary<string, UsageRecord> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
            return JsonSerializer.Deserialize<Dictionary<string, UsageRecord>>(File.ReadAllText(_path))
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception) when (File.Exists(_path))
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_records, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }
}

public sealed record UsageRecord(string Day, string Month, int DailyRequests, long MonthlyCharacters);
