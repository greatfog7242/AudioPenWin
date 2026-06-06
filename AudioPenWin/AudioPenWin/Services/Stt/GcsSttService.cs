using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioPenWin.Data.Entities;

namespace AudioPenWin.Services.Stt;

public class GcsSttService : ISttProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<IReadOnlyList<TranscriptSegmentEntity>> TranscribeAsync(
        string recordingId, string audioFilePath, string language,
        bool enableDiarization, int speakerCount, CancellationToken ct)
    {
        var apiKey = App.Secrets.GcsApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("GCS API key not configured.");

        // Extract 16 kHz mono PCM
        var pcmPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(audioFilePath)!, "audio.pcm");
        await AudioFileUtil.ExtractPcmAsync(audioFilePath, pcmPath, ct);

        var audioBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(pcmPath, ct));

        // POST long-running recognize
        var req = new GcsLongRunRequest
        {
            Config = new GcsRecognitionConfig
            {
                Encoding = "LINEAR16",
                SampleRateHertz = 16000,
                AudioChannelCount = 1,
                LanguageCode = language == "auto" ? "en-US" : language,
                EnableWordTimeOffsets = true,
                EnableAutomaticPunctuation = true,
                DiarizationConfig = enableDiarization ? new GcsDiarizationConfig
                {
                    EnableSpeakerDiarization = true,
                    MinSpeakerCount = 1,
                    MaxSpeakerCount = speakerCount
                } : null
            },
            Audio = new GcsAudio { Content = audioBase64 }
        };

        var url = $"https://speech.googleapis.com/v1/speech:longrunningrecognize?key={Uri.EscapeDataString(apiKey)}";
        using var postResp = await Http.PostAsJsonAsync(url, req, JsonOpts, ct);
        postResp.EnsureSuccessStatusCode();

        var op = await postResp.Content.ReadFromJsonAsync<GcsOperation>(JsonOpts, ct)
                 ?? throw new Exception("Empty LRO response from GCS");
        if (string.IsNullOrEmpty(op.Name))
            throw new Exception("GCS returned no operation name.");

        // Poll until done
        var pollUrl = $"https://speech.googleapis.com/v1/operations/{op.Name}?key={Uri.EscapeDataString(apiKey)}";
        GcsOperation? done = null;
        while (true)
        {
            await Task.Delay(5000, ct);
            var poll = await Http.GetFromJsonAsync<GcsOperation>(pollUrl, JsonOpts, ct);
            if (poll?.Done == true) { done = poll; break; }
        }

        try { File.Delete(pcmPath); } catch { }

        return ParseSegments(recordingId, done?.Response?.Results);
    }

    private static IReadOnlyList<TranscriptSegmentEntity> ParseSegments(
        string recordingId, GcsResult[]? results)
    {
        var list = new List<TranscriptSegmentEntity>();
        if (results is null) return list;

        foreach (var result in results)
        {
            var alt = result.Alternatives?.FirstOrDefault();
            if (alt?.Words is null) continue;
            foreach (var w in alt.Words)
            {
                list.Add(new TranscriptSegmentEntity
                {
                    RecordingId = recordingId,
                    Word = w.Word ?? "",
                    StartMs = ParseMs(w.StartTime),
                    EndMs = ParseMs(w.EndTime),
                    Confidence = alt.Confidence,
                    SpeakerLabel = w.SpeakerTag?.ToString() ?? ""
                });
            }
        }
        return list;
    }

    private static long ParseMs(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return 0;
        var s = duration.TrimEnd('s');
        return (long)(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture) * 1000);
    }

    // ── Request models ──────────────────────────────────────────────────────
    private class GcsLongRunRequest
    {
        public GcsRecognitionConfig Config { get; set; } = null!;
        public GcsAudio Audio { get; set; } = null!;
    }

    private class GcsRecognitionConfig
    {
        public string Encoding { get; set; } = null!;
        public int SampleRateHertz { get; set; }
        public int AudioChannelCount { get; set; }
        public string LanguageCode { get; set; } = null!;
        public bool EnableWordTimeOffsets { get; set; }
        public bool EnableAutomaticPunctuation { get; set; }
        public GcsDiarizationConfig? DiarizationConfig { get; set; }
    }

    private class GcsDiarizationConfig
    {
        public bool EnableSpeakerDiarization { get; set; }
        public int MinSpeakerCount { get; set; }
        public int MaxSpeakerCount { get; set; }
    }

    private class GcsAudio { public string Content { get; set; } = null!; }

    // ── Response models ─────────────────────────────────────────────────────
    private record GcsOperation(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("response")] GcsResponse? Response);

    private record GcsResponse(
        [property: JsonPropertyName("results")] GcsResult[]? Results);

    private record GcsResult(
        [property: JsonPropertyName("alternatives")] GcsAlternative[]? Alternatives);

    private record GcsAlternative(
        [property: JsonPropertyName("transcript")] string? Transcript,
        [property: JsonPropertyName("confidence")] float Confidence,
        [property: JsonPropertyName("words")] GcsWord[]? Words);

    private record GcsWord(
        [property: JsonPropertyName("word")] string? Word,
        [property: JsonPropertyName("startTime")] string? StartTime,
        [property: JsonPropertyName("endTime")] string? EndTime,
        [property: JsonPropertyName("speakerTag")] int? SpeakerTag);
}
