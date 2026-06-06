using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioPenWin.Data.Entities;
using AudioPenWin.Data.Repositories;
using AudioPenWin.Services;

namespace AudioPenWin.ViewModels;

public class PlaybackViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly RecordingRepository _recordings;
    private readonly TranscriptRepository _transcripts;

    public RecordingEntity? Recording { get; private set; }
    public ObservableCollection<TranscriptWordViewModel> Words { get; } = new();

    private string _title = "";
    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            Notify();
        }
    }

    private string _status = "";
    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            Notify();
            Notify(nameof(StatusText));
            Notify(nameof(ShowRetry));
            Notify(nameof(ShowTranscribingSpinner));
            Notify(nameof(ShowFailedPlaceholder));
        }
    }

    public string StatusText => Status switch
    {
        "DONE" => "Done",
        "PROCESSING" => "Transcribing…",
        "FAILED" => "Transcription failed",
        _ => "Pending…"
    };

    public string ErrorMessage { get; private set; } = "";
    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);
    public bool ShowRetry => Status is "FAILED" or "PENDING";
    public bool ShowTranscribingSpinner => Status is "PENDING" or "PROCESSING";
    public bool ShowFailedPlaceholder => Status == "FAILED";
    public bool HasTranscript => Words.Count > 0;
    public bool IsVideo => Recording?.SourceType == "VIDEO_IMPORT";

    private int _activeWordIndex = -1;

    public PlaybackViewModel(RecordingRepository recordings, TranscriptRepository transcripts)
    {
        _recordings = recordings;
        _transcripts = transcripts;
        Words.CollectionChanged += (_, _) => Notify(nameof(HasTranscript));
    }

    public async Task LoadAsync(string recordingId)
    {
        Recording = await _recordings.GetByIdAsync(recordingId);
        if (Recording is null) return;

        Title = Recording.Title;
        Status = Recording.Status;
        ErrorMessage = Recording.ErrorMessage;
        Notify(nameof(IsVideo));

        Words.Clear();
        _activeWordIndex = -1;

        if (Recording.Status == "DONE")
        {
            var segments = await _transcripts.GetByRecordingIdAsync(recordingId);
            foreach (var s in segments.OrderBy(s => s.StartMs))
                Words.Add(new TranscriptWordViewModel
                {
                    Word = s.Word,
                    StartMs = s.StartMs,
                    EndMs = s.EndMs,
                    Speaker = s.SpeakerLabel
                });
        }
    }

    public async Task<bool> RefreshStatusAsync()
    {
        if (Recording is null) return false;
        var updated = await _recordings.GetByIdAsync(Recording.Id);
        if (updated is null) return false;

        bool changed = updated.Status != Status;
        Recording = updated;
        Status = updated.Status;

        if (changed && updated.Status == "DONE" && Words.Count == 0)
        {
            await LoadAsync(Recording.Id);
            return true;
        }

        return changed;
    }

    public async Task SaveTitleAsync()
    {
        if (Recording is null) return;
        var trimmed = Title.Trim();
        if (string.IsNullOrEmpty(trimmed)) { Title = Recording.Title; return; }
        Recording.Title = trimmed;
        Title = trimmed;
        await _recordings.UpdateAsync(Recording);
    }

    public void Retry()
    {
        if (Recording is null) return;
        Status = "PENDING";
        _ = App.Services.GetRequiredService<TranscriptionService>().EnqueueAsync(Recording.Id);
    }

    // Returns new active word index (>=0) if it changed, -1 if unchanged.
    public int UpdatePosition(long positionMs)
    {
        if (Words.Count == 0) return -1;

        // Binary search: find last word whose StartMs <= positionMs
        int lo = 0, hi = Words.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Words[mid].StartMs <= positionMs) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }

        // If positionMs is in a gap after the word's end, no active word
        if (found >= 0 && positionMs > Words[found].EndMs + 300)
            found = -1;

        if (found == _activeWordIndex) return -1;

        if (_activeWordIndex >= 0 && _activeWordIndex < Words.Count)
            Words[_activeWordIndex].IsActive = false;

        _activeWordIndex = found;

        if (_activeWordIndex >= 0)
            Words[_activeWordIndex].IsActive = true;

        return _activeWordIndex;
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
