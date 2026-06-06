using AudioPenWin.Data.Repositories;
using AudioPenWin.Services.Stt;

namespace AudioPenWin.Services;

public class TranscriptionService
{
    private readonly RecordingRepository _recordings;
    private readonly TranscriptRepository _transcripts;
    private readonly GcsSttService _gcs;
    private readonly AssemblySttService _assemblyAi;
    private readonly WhisperSttService _whisper;

    private readonly Dictionary<string, CancellationTokenSource> _active = new();
    private readonly object _lock = new();

    public TranscriptionService(
        RecordingRepository recordings,
        TranscriptRepository transcripts,
        GcsSttService gcs,
        AssemblySttService assemblyAi,
        WhisperSttService whisper)
    {
        _recordings = recordings;
        _transcripts = transcripts;
        _gcs = gcs;
        _assemblyAi = assemblyAi;
        _whisper = whisper;
    }

    public async Task EnqueueAsync(string recordingId)
    {
        var cts = new CancellationTokenSource();
        lock (_lock) _active[recordingId] = cts;

        await _recordings.UpdateStatusAsync(recordingId, "PROCESSING");

        _ = Task.Run(async () => await RunAsync(recordingId, cts.Token), cts.Token);
    }

    public void Cancel(string recordingId)
    {
        lock (_lock)
        {
            if (_active.TryGetValue(recordingId, out var cts))
                cts.Cancel();
        }
    }

    public async Task RequeueStaleAsync()
    {
        var all = await _recordings.GetAllAsync();
        foreach (var r in all.Where(r => r.Status == "PROCESSING"))
            await _recordings.UpdateStatusAsync(r.Id, "PENDING");
    }

    private async Task RunAsync(string recordingId, CancellationToken ct)
    {
        try
        {
            var recording = await _recordings.GetByIdAsync(recordingId);
            if (recording is null) return;

            ISttProvider provider = recording.SttProvider switch
            {
                "AssemblyAI" => _assemblyAi,
                "Whisper"    => _whisper,
                _            => _gcs
            };

            var segments = await provider.TranscribeAsync(
                recordingId,
                recording.FilePath,
                string.IsNullOrEmpty(recording.Language) ? App.Preferences.Language : recording.Language,
                App.Preferences.EnableDiarization,
                recording.SpeakerCount > 0 ? recording.SpeakerCount : App.Preferences.SpeakerCount,
                ct);

            await _transcripts.InsertManyAsync(segments);

            recording.Status = "DONE";
            recording.WordCount = segments.Count;
            await _recordings.UpdateAsync(recording);
        }
        catch (OperationCanceledException) { }
        catch
        {
            try { await _recordings.UpdateStatusAsync(recordingId, "FAILED"); } catch { }
        }
        finally
        {
            lock (_lock) _active.Remove(recordingId);
        }
    }
}
