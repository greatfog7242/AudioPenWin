using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioPenWin.Data.Entities;

namespace AudioPenWin.Services.Stt;

public class AssemblySttService : ISttProvider
{
    private const string BaseUrl = "https://api.assemblyai.com/v2";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<IReadOnlyList<TranscriptSegmentEntity>> TranscribeAsync(
        string recordingId, string audioFilePath, string language,
        bool enableDiarization, int speakerCount, CancellationToken ct)
    {
        var apiKey = App.Secrets.AssemblyAiApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("AssemblyAI API key not configured.");

        // 1. Upload audio
        var uploadUrl = await UploadAsync(audioFilePath, apiKey, ct);

        // 2. Submit transcript request
        var transcriptReq = new AaiTranscriptRequest
        {
            AudioUrl = uploadUrl,
            LanguageCode = NormalizeLanguage(language),
            SpeakerLabels = enableDiarization,
            SpeakersExpected = enableDiarization ? speakerCount : null
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/transcript");
        msg.Headers.Authorization = new AuthenticationHeaderValue(apiKey);
        msg.Content = JsonContent.Create(transcriptReq, options: JsonOpts);
        using var submitResp = await Http.SendAsync(msg, ct);
        submitResp.EnsureSuccessStatusCode();

        var submitted = await submitResp.Content.ReadFromJsonAsync<AaiTranscript>(JsonOpts, ct)
                        ?? throw new Exception("Empty transcript response from AssemblyAI");

        // 3. Poll until completed
        AaiTranscript? final = null;
        while (true)
        {
            await Task.Delay(5000, ct);
            using var poll = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/transcript/{submitted.Id}");
            poll.Headers.Authorization = new AuthenticationHeaderValue(apiKey);
            using var pollResp = await Http.SendAsync(poll, ct);
            pollResp.EnsureSuccessStatusCode();
            var t = await pollResp.Content.ReadFromJsonAsync<AaiTranscript>(JsonOpts, ct);
            if (t?.Status == "completed") { final = t; break; }
            if (t?.Status == "error") throw new Exception($"AssemblyAI error: {t.Error}");
        }

        return ParseSegments(recordingId, final?.Words);
    }

    private static async Task<string> UploadAsync(string filePath, string apiKey, CancellationToken ct)
    {
        using var stream = File.OpenRead(filePath);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/upload");
        req.Headers.Authorization = new AuthenticationHeaderValue(apiKey);
        req.Content = new StreamContent(stream);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AaiUploadResponse>(JsonOpts, ct)
                   ?? throw new Exception("Empty upload response from AssemblyAI");
        return body.UploadUrl ?? throw new Exception("No upload_url in AssemblyAI response");
    }

    private static IReadOnlyList<TranscriptSegmentEntity> ParseSegments(
        string recordingId, AaiWord[]? words)
    {
        if (words is null) return [];
        return words.Select(w => new TranscriptSegmentEntity
        {
            RecordingId = recordingId,
            Word = w.Text ?? "",
            StartMs = w.Start,
            EndMs = w.End,
            Confidence = w.Confidence,
            SpeakerLabel = w.Speaker ?? ""
        }).ToList();
    }

    private static string NormalizeLanguage(string lang) =>
        lang.Contains('-') ? lang[..lang.IndexOf('-')] : lang;

    // ── Models ──────────────────────────────────────────────────────────────
    private class AaiTranscriptRequest
    {
        public string AudioUrl { get; set; } = null!;
        public string? LanguageCode { get; set; }
        public bool SpeakerLabels { get; set; }
        public int? SpeakersExpected { get; set; }
    }

    private record AaiUploadResponse(
        [property: JsonPropertyName("upload_url")] string? UploadUrl);

    private record AaiTranscript(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("words")] AaiWord[]? Words);

    private record AaiWord(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("start")] long Start,
        [property: JsonPropertyName("end")] long End,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("speaker")] string? Speaker);
}
