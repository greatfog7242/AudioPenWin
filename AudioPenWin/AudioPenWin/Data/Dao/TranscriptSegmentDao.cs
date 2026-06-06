using Dapper;
using AudioPenWin.Data.Entities;

namespace AudioPenWin.Data.Dao;

public class TranscriptSegmentDao
{
    private readonly AppDatabase _db;

    public TranscriptSegmentDao(AppDatabase db) => _db = db;

    public async Task InsertManyAsync(IEnumerable<TranscriptSegmentEntity> segments)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("""
            INSERT INTO transcript_segments (RecordingId, SpeakerLabel, StartMs, EndMs, Word, Confidence)
            VALUES (@RecordingId, @SpeakerLabel, @StartMs, @EndMs, @Word, @Confidence)
            """, segments, tx);
        tx.Commit();
    }

    public async Task<IEnumerable<TranscriptSegmentEntity>> GetByRecordingIdAsync(string recordingId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<TranscriptSegmentEntity>(
            "SELECT * FROM transcript_segments WHERE RecordingId = @RecordingId ORDER BY StartMs",
            new { RecordingId = recordingId });
    }

    public async Task DeleteByRecordingIdAsync(string recordingId)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM transcript_segments WHERE RecordingId = @RecordingId",
            new { RecordingId = recordingId });
    }
}
