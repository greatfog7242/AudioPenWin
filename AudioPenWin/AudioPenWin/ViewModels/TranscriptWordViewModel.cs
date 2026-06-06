using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AudioPenWin.ViewModels;

public class TranscriptWordViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Word { get; init; } = null!;
    public long StartMs { get; init; }
    public long EndMs { get; init; }
    public string Speaker { get; init; } = "";

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            Notify();
            Notify(nameof(TextBrush));
        }
    }

    public SolidColorBrush TextBrush => _isActive
        ? new SolidColorBrush(Color.FromArgb(255, 251, 191, 36))
        : new SolidColorBrush(Color.FromArgb(255, 210, 210, 210));

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
