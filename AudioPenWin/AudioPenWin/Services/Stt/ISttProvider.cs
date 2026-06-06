using AudioPenWin.Data.Entities;

namespace AudioPenWin.Services.Stt;

public interface ISttProvider
{
    Task<IReadOnlyList<TranscriptSegmentEntity>> TranscribeAsync(
        string recordingId,
        string audioFilePath,
        string language,
        bool enableDiarization,
        int speakerCount,
        CancellationToken ct);
}
