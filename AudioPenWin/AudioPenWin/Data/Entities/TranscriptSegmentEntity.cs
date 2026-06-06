namespace AudioPenWin.Data.Entities;

public class TranscriptSegmentEntity
{
    public long Id { get; set; }
    public string RecordingId { get; set; } = null!;
    public string SpeakerLabel { get; set; } = "";
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string Word { get; set; } = null!;
    public double Confidence { get; set; }
}
