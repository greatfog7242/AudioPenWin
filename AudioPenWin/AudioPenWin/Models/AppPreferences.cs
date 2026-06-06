using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioPenWin.Models;

public enum SttProviderKind { GCS, AssemblyAI, Whisper }

public class AppPreferences
{
    public SttProviderKind SttProvider { get; set; } = SttProviderKind.GCS;
    public string Language { get; set; } = "en-US";
    public string OutputFolder { get; set; } = "";
    public bool EnableDiarization { get; set; } = false;
    public int SpeakerCount { get; set; } = 2;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioPen", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static AppPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(FilePath), JsonOpts) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
