using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using AudioPenWin.Data.Entities;

namespace AudioPenWin.ViewModels;

public class RecordingCardViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; init; } = null!;
    public string DateText { get; init; } = null!;
    public string DurationText { get; init; } = null!;
    public string SourceIcon { get; init; } = null!;

    private string _title = null!;
    public string Title
    {
        get => _title;
        set { _title = value; Notify(); }
    }

    private string _status = null!;
    public string Status
    {
        get => _status;
        set { _status = value; Notify(); Notify(nameof(StatusText)); Notify(nameof(StatusBrush)); }
    }

    public string StatusText => Status switch
    {
        "DONE" => "Done",
        "PROCESSING" => "Processing…",
        "FAILED" => "Failed",
        _ => "Pending"
    };

    public SolidColorBrush StatusBrush => Status switch
    {
        "DONE" => new SolidColorBrush(Color.FromArgb(255, 34, 197, 94)),
        "PROCESSING" => new SolidColorBrush(Color.FromArgb(255, 245, 158, 11)),
        "FAILED" => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)),
        _ => new SolidColorBrush(Color.FromArgb(255, 148, 163, 184))
    };

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static RecordingCardViewModel FromEntity(RecordingEntity e)
    {
        var date = DateTimeOffset.FromUnixTimeMilliseconds(e.CreatedAt).ToLocalTime();
        var dur = TimeSpan.FromMilliseconds(e.DurationMs);
        var icon = e.SourceType switch
        {
            "VIDEO_IMPORT" => "🎬",
            "AUDIO_IMPORT" => "🎵",
            _ => "🎤"
        };
        return new RecordingCardViewModel
        {
            Id = e.Id,
            Title = e.Title,
            DateText = date.ToString("MMM d, yyyy  h:mm tt"),
            DurationText = $"{(int)dur.TotalMinutes}:{dur.Seconds:D2}",
            SourceIcon = icon,
            Status = e.Status
        };
    }
}
