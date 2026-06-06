namespace AudioPenWin.Data.Entities;

public class RecordingEntity
{
    public string Id { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string SourceType { get; set; } = null!;   // MIC | VIDEO_IMPORT | AUDIO_IMPORT
    public string FilePath { get; set; } = null!;
    public long DurationMs { get; set; }
    public long CreatedAt { get; set; }               // epoch ms
    public string Status { get; set; } = null!;       // PENDING | PROCESSING | DONE | FAILED
    public string SttProvider { get; set; } = "";
    public string Language { get; set; } = "";
    public int SpeakerCount { get; set; }
    public int WordCount { get; set; }
    public int CostCents { get; set; }
}
