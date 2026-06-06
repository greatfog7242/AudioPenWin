using Dapper;
using AudioPenWin.Data.Entities;

namespace AudioPenWin.Data.Dao;

public class RecordingDao
{
    private readonly AppDatabase _db;

    public RecordingDao(AppDatabase db) => _db = db;

    public async Task InsertAsync(RecordingEntity r)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO recordings
                (Id, Title, SourceType, FilePath, DurationMs, CreatedAt, Status, SttProvider, Language, SpeakerCount, WordCount, CostCents)
            VALUES
                (@Id, @Title, @SourceType, @FilePath, @DurationMs, @CreatedAt, @Status, @SttProvider, @Language, @SpeakerCount, @WordCount, @CostCents)
            """, r);
    }

    public async Task<RecordingEntity?> GetByIdAsync(string id)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<RecordingEntity>(
            "SELECT * FROM recordings WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<RecordingEntity>> GetAllAsync()
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<RecordingEntity>("SELECT * FROM recordings ORDER BY CreatedAt DESC");
    }

    public async Task<IEnumerable<RecordingEntity>> SearchAsync(string query)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<RecordingEntity>(
            "SELECT * FROM recordings WHERE Title LIKE @Q ORDER BY CreatedAt DESC",
            new { Q = $"%{query}%" });
    }

    public async Task UpdateStatusAsync(string id, string status)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("UPDATE recordings SET Status = @Status WHERE Id = @Id", new { Id = id, Status = status });
    }

    public async Task UpdateAsync(RecordingEntity r)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("""
            UPDATE recordings SET
                Title=@Title, DurationMs=@DurationMs, Status=@Status, SttProvider=@SttProvider,
                Language=@Language, SpeakerCount=@SpeakerCount, WordCount=@WordCount, CostCents=@CostCents
            WHERE Id=@Id
            """, r);
    }

    public async Task DeleteAsync(string id)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM recordings WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<CostRecord>> GetCostSummaryAsync()
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<CostRecord>("""
            SELECT SttProvider, SUM(CostCents) AS TotalCostCents, SUM(DurationMs) AS TotalDurationMs
            FROM recordings WHERE Status = 'DONE' AND SttProvider != ''
            GROUP BY SttProvider
            """);
    }
}
