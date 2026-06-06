using AudioPenWin.Data.Dao;
using AudioPenWin.Data.Entities;

namespace AudioPenWin.Data.Repositories;

public class RecordingRepository
{
    private readonly RecordingDao _dao;

    public RecordingRepository(RecordingDao dao) => _dao = dao;

    public Task InsertAsync(RecordingEntity recording) => _dao.InsertAsync(recording);
    public Task<RecordingEntity?> GetByIdAsync(string id) => _dao.GetByIdAsync(id);
    public Task<IEnumerable<RecordingEntity>> GetAllAsync() => _dao.GetAllAsync();
    public Task<IEnumerable<RecordingEntity>> SearchAsync(string query) => _dao.SearchAsync(query);
    public Task UpdateStatusAsync(string id, string status) => _dao.UpdateStatusAsync(id, status);
    public Task UpdateAsync(RecordingEntity recording) => _dao.UpdateAsync(recording);
    public Task DeleteAsync(string id) => _dao.DeleteAsync(id);
    public Task<IEnumerable<CostRecord>> GetCostSummaryAsync() => _dao.GetCostSummaryAsync();
}
