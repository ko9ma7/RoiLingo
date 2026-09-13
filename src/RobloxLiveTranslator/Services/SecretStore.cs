using System.Security.Cryptography;

namespace RobloxLiveTranslator.Services;

public sealed class ApiSecrets
{
    public string PapagoClientId { get; set; } = string.Empty;
    public string PapagoClientSecret { get; set; } = string.Empty;
    public string GoogleApiKey { get; set; } = string.Empty;
    public string DeepLApiKey { get; set; } = string.Empty;
    public string LibreTranslateApiKey { get; set; } = string.Empty;
}

public static class SecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static string SecretPath => Path.Combine(SettingsStore.AppDirectory, "api-secrets.dpapi");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RoiLingo.ApiSecrets.v1");

    public static ApiSecrets Load()
    {
        try
        {
            if (!File.Exists(SecretPath)) return new ApiSecrets();
            var encrypted = File.ReadAllBytes(SecretPath);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<ApiSecrets>(plain, JsonOptions) ?? new ApiSecrets();
        }
        catch (CryptographicException)
        {
            return new ApiSecrets();
        }
        catch (IOException)
        {
            return new ApiSecrets();
        }
    }

    public static void Save(ApiSecrets secrets)
    {
        Directory.CreateDirectory(SettingsStore.AppDirectory);
        var plain = JsonSerializer.SerializeToUtf8Bytes(secrets, JsonOptions);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        var temp = SecretPath + ".tmp";
        File.WriteAllBytes(temp, encrypted);
        File.Move(temp, SecretPath, true);
    }
}
