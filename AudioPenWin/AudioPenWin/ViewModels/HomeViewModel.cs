using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioPenWin.Data.Repositories;
using AudioPenWin.Services;

namespace AudioPenWin.ViewModels;

public class HomeViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly RecordingRepository _recordings;

    public ObservableCollection<RecordingCardViewModel> Recordings { get; } = new();

    private List<RecordingCardViewModel> _all = new();

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; Notify(); ApplyFilter(); }
    }

    public bool IsEmpty => Recordings.Count == 0;

    public HomeViewModel(RecordingRepository recordings)
    {
        _recordings = recordings;
        Recordings.CollectionChanged += (_, _) => Notify(nameof(IsEmpty));
    }

    public async Task LoadAsync()
    {
        var entities = await _recordings.GetAllAsync();
        _all = entities.Select(RecordingCardViewModel.FromEntity).ToList();
        ApplyFilter();
    }

    public async Task RefreshStatusesAsync()
    {
        var pending = _all.Where(c => c.Status is "PENDING" or "PROCESSING").ToList();
        if (pending.Count == 0) return;

        var entities = await _recordings.GetAllAsync();
        var statusMap = entities.ToDictionary(e => e.Id, e => e.Status);
        foreach (var card in pending)
        {
            if (statusMap.TryGetValue(card.Id, out var newStatus) && card.Status != newStatus)
                card.Status = newStatus;
        }
    }

    public async Task RenameAsync(string id, string newTitle)
    {
        var entity = await _recordings.GetByIdAsync(id);
        if (entity is null) return;
        entity.Title = newTitle;
        await _recordings.UpdateAsync(entity);
        var card = _all.FirstOrDefault(c => c.Id == id);
        if (card is not null) card.Title = newTitle;
    }

    public async Task DeleteAsync(string id)
    {
        await _recordings.DeleteAsync(id);
        var dir = RecordingService.GetRecordingDir(id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        var card = _all.FirstOrDefault(c => c.Id == id);
        if (card is not null)
        {
            _all.Remove(card);
            Recordings.Remove(card);
        }
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_searchText)
            ? _all
            : _all.Where(c => c.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();

        Recordings.Clear();
        foreach (var item in filtered)
            Recordings.Add(item);
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
