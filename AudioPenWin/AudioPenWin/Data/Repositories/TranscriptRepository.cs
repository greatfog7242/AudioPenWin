using AudioPenWin.Data.Dao;
using AudioPenWin.Data.Entities;

namespace AudioPenWin.Data.Repositories;

public class TranscriptRepository
{
    private readonly TranscriptSegmentDao _dao;

    public TranscriptRepository(TranscriptSegmentDao dao) => _dao = dao;

    public Task InsertManyAsync(IEnumerable<TranscriptSegmentEntity> segments) => _dao.InsertManyAsync(segments);
    public Task<IEnumerable<TranscriptSegmentEntity>> GetByRecordingIdAsync(string recordingId) => _dao.GetByRecordingIdAsync(recordingId);
    public Task DeleteByRecordingIdAsync(string recordingId) => _dao.DeleteByRecordingIdAsync(recordingId);
}
