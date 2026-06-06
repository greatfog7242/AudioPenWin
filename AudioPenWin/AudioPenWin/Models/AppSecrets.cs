using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AudioPenWin.Models;

public class AppSecrets
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioPen", "secrets.bin");

    private Dictionary<string, string> _secrets = new();

    public string GcsApiKey
    {
        get => Get(nameof(GcsApiKey));
        set => Set(nameof(GcsApiKey), value);
    }

    public string AssemblyAiApiKey
    {
        get => Get(nameof(AssemblyAiApiKey));
        set => Set(nameof(AssemblyAiApiKey), value);
    }

    private string Get(string key) => _secrets.TryGetValue(key, out var v) ? v : "";
    private void Set(string key, string value) => _secrets[key] = value;

    public static AppSecrets Load()
    {
        var s = new AppSecrets();
        try
        {
            if (File.Exists(FilePath))
            {
                var decrypted = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
                s._secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(decrypted)) ?? new();
            }
        }
        catch { }
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_secrets)), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }
}
