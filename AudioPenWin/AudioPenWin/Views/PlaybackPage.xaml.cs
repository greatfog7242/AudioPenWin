using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;
using AudioPenWin.Data.Entities;
using AudioPenWin.Services;
using AudioPenWin.ViewModels;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace AudioPenWin.Views;

public sealed partial class PlaybackPage : Page
{
    public PlaybackViewModel ViewModel { get; } =
        App.Services.GetRequiredService<PlaybackViewModel>();

    private MediaPlayer? _mediaPlayer;
    private DispatcherQueueTimer? _positionTimer;
    private DispatcherQueueTimer? _pollTimer;
    private DispatcherQueue _dq = null!;
    private bool _ignoreSliderChange;

    private readonly List<(TranscriptWordViewModel Word, Hyperlink Link)> _wordLinks = new();
    private int _activeHlIndex = -1;
    private static readonly SolidColorBrush _activeBrush =
        new(Color.FromArgb(255, 251, 191, 36));
    private static readonly SolidColorBrush _defaultBrush =
        new(Color.FromArgb(255, 210, 210, 210));

    public PlaybackPage()
    {
        InitializeComponent();
        _dq = DispatcherQueue.GetForCurrentThread();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not string recordingId || string.IsNullOrEmpty(recordingId))
            return;

        await ViewModel.LoadAsync(recordingId);
        UpdateStatusPanels();

        if (ViewModel.Recording is not null)
        {
            Player.Visibility = ViewModel.IsVideo ? Visibility.Visible : Visibility.Collapsed;
            SetupMediaPlayer(ViewModel.Recording.FilePath, ViewModel.Recording.DurationMs);
            TbDuration.Text = FormatTime(TimeSpan.FromMilliseconds(ViewModel.Recording.DurationMs));
        }

        if (ViewModel.Words.Count > 0)
            BuildTranscript();

        if (ViewModel.Status is "PENDING" or "PROCESSING")
            StartPolling();

        StartPositionTimer();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopPositionTimer();
        StopPolling();
        _mediaPlayer?.Pause();
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;
    }

    // ==================== UI State ====================

    private void UpdateStatusPanels()
    {
        BtnRetry.Visibility = ViewModel.ShowRetry ? Visibility.Visible : Visibility.Collapsed;
        TbStatus.Text = ViewModel.StatusText;

        switch (ViewModel.Status)
        {
            case "DONE":
                SpinnerPanel.Visibility = Visibility.Collapsed;
                FailedPanel.Visibility = Visibility.Collapsed;
                break;
            case "FAILED":
                SpinnerPanel.Visibility = Visibility.Collapsed;
                FailedPanel.Visibility = Visibility.Visible;
                TranscriptScroll.Visibility = Visibility.Collapsed;
                break;
            default:
                SpinnerPanel.Visibility = Visibility.Visible;
                FailedPanel.Visibility = Visibility.Collapsed;
                TranscriptScroll.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void BuildTranscript()
    {
        TranscriptBlock.Blocks.Clear();
        _wordLinks.Clear();
        _activeHlIndex = -1;

        var para = new Paragraph { LineHeight = 30 };
        foreach (var word in ViewModel.Words)
        {
            var run = new Run { Text = word.Word + " " };
            var link = new Hyperlink { Foreground = _defaultBrush, UnderlineStyle = UnderlineStyle.None };
            link.Inlines.Add(run);
            var captured = word;
            link.Click += (_, _) => SeekToMs(captured.StartMs);
            _wordLinks.Add((word, link));
            para.Inlines.Add(link);
        }

        TranscriptBlock.Blocks.Add(para);
        TranscriptScroll.Visibility = Visibility.Visible;
        SpinnerPanel.Visibility = Visibility.Collapsed;
    }

    private void SeekToMs(long ms)
    {
        if (_mediaPlayer is null) return;
        _mediaPlayer.PlaybackSession.Position = TimeSpan.FromMilliseconds(ms);
    }

    // ==================== Media Player ====================

    private void SetupMediaPlayer(string filePath, long durationMs)
    {
        _mediaPlayer = new MediaPlayer();
        try
        {
            _mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(filePath));
        }
        catch { return; }

        Player.SetMediaPlayer(_mediaPlayer);

        _mediaPlayer.PlaybackSession.PlaybackStateChanged += (_, _) =>
            _dq.TryEnqueue(UpdatePlayPauseButton);

        _mediaPlayer.MediaOpened += (s, _) =>
            _dq.TryEnqueue(() =>
            {
                var dur = s.PlaybackSession.NaturalDuration;
                if (dur > TimeSpan.Zero)
                    TbDuration.Text = FormatTime(dur);
            });
    }

    // ==================== Timers ====================

    private void StartPositionTimer()
    {
        _positionTimer = _dq.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(50);
        _positionTimer.Tick += OnPositionTick;
        _positionTimer.Start();
    }

    private void StopPositionTimer()
    {
        _positionTimer?.Stop();
        _positionTimer = null;
    }

    private void StartPolling()
    {
        if (_pollTimer is not null) return;
        _pollTimer = _dq.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(3);
        _pollTimer.Tick += async (_, _) =>
        {
            bool changed = await ViewModel.RefreshStatusAsync();
            UpdateStatusPanels();
            if (changed && ViewModel.Status == "DONE" && ViewModel.Words.Count > 0 && _wordLinks.Count == 0)
                BuildTranscript();
            if (ViewModel.Status is not "PENDING" and not "PROCESSING")
                StopPolling();
        };
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private void OnPositionTick(DispatcherQueueTimer t, object _)
    {
        if (_mediaPlayer is null) return;
        var session = _mediaPlayer.PlaybackSession;
        var pos = session.Position;
        var dur = session.NaturalDuration;

        TbPosition.Text = FormatTime(pos);

        if (!_ignoreSliderChange && dur > TimeSpan.Zero)
        {
            _ignoreSliderChange = true;
            SeekSlider.Value = pos.TotalMilliseconds / dur.TotalMilliseconds * 1000.0;
            _ignoreSliderChange = false;
        }

        int newIndex = ViewModel.UpdatePosition((long)pos.TotalMilliseconds);
        if (newIndex >= 0)
            HighlightWord(newIndex);
    }

    private void HighlightWord(int index)
    {
        if (_activeHlIndex >= 0 && _activeHlIndex < _wordLinks.Count)
            _wordLinks[_activeHlIndex].Link.Foreground = _defaultBrush;

        _activeHlIndex = index;

        if (_activeHlIndex >= 0 && _activeHlIndex < _wordLinks.Count)
        {
            _wordLinks[_activeHlIndex].Link.Foreground = _activeBrush;
            ScrollToApproxWord(_activeHlIndex);
        }
    }

    private void ScrollToApproxWord(int index)
    {
        if (_wordLinks.Count == 0 || TranscriptScroll.ScrollableHeight <= 0) return;
        double ratio = (double)index / _wordLinks.Count;
        double target = ratio * TranscriptScroll.ScrollableHeight - TranscriptScroll.ViewportHeight * 0.4;
        if (target < 0) target = 0;
        double current = TranscriptScroll.VerticalOffset;
        double approxWordY = ratio * (TranscriptScroll.ScrollableHeight + TranscriptScroll.ViewportHeight);
        double viewBottom = current + TranscriptScroll.ViewportHeight;
        if (approxWordY < current || approxWordY > viewBottom)
            TranscriptScroll.ChangeView(null, target, null, true);
    }

    private void UpdatePlayPauseButton()
    {
        if (_mediaPlayer is null) return;
        bool playing = _mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        PlayPauseIcon.Glyph = playing ? "" : "";
    }

    // ==================== Event Handlers ====================

    private void Back_Click(object sender, RoutedEventArgs e) =>
        App.MainWindow.Navigate(typeof(HomePage));

    private async void TitleBox_LostFocus(object sender, RoutedEventArgs e) =>
        await ViewModel.SaveTitleAsync();

    private async void TitleBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            TitleBox.IsEnabled = false;
            TitleBox.IsEnabled = true;
            await ViewModel.SaveTitleAsync();
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null) return;
        if (_mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            _mediaPlayer.Pause();
        else
            _mediaPlayer.Play();
    }

    private void Rewind_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null) return;
        var pos = _mediaPlayer.PlaybackSession.Position - TimeSpan.FromSeconds(10);
        _mediaPlayer.PlaybackSession.Position = pos < TimeSpan.Zero ? TimeSpan.Zero : pos;
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null) return;
        var dur = _mediaPlayer.PlaybackSession.NaturalDuration;
        var pos = _mediaPlayer.PlaybackSession.Position + TimeSpan.FromSeconds(10);
        _mediaPlayer.PlaybackSession.Position = pos > dur ? dur : pos;
    }

    private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_ignoreSliderChange || _mediaPlayer is null) return;
        var dur = _mediaPlayer.PlaybackSession.NaturalDuration;
        if (dur <= TimeSpan.Zero) return;
        _mediaPlayer.PlaybackSession.Position =
            TimeSpan.FromMilliseconds(e.NewValue / 1000.0 * dur.TotalMilliseconds);
    }

    private void SpeedBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_mediaPlayer is null) return;
        if (SpeedBox.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Tag?.ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double rate))
        {
            _mediaPlayer.PlaybackSession.PlaybackRate = rate;
        }
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Retry();
        UpdateStatusPanels();
        StartPolling();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Recording is null) return;

        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        var segments = ViewModel.Words.Select(w => new TranscriptSegmentEntity
        {
            RecordingId = ViewModel.Recording.Id,
            Word = w.Word,
            StartMs = w.StartMs,
            EndMs = w.EndMs,
            SpeakerLabel = w.Speaker
        });

        try
        {
            var exportDir = await AudioFileUtil.ExportAsync(ViewModel.Recording, segments, folder.Path);
            var dlg = new ContentDialog
            {
                Title = "Export complete",
                Content = $"Saved to:\n{exportDir}",
                PrimaryButtonText = "Open folder",
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                System.Diagnostics.Process.Start("explorer.exe", $"\"{exportDir}\"");
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Export failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Recording is null) return;
        var dir = System.IO.Path.GetDirectoryName(ViewModel.Recording.FilePath);
        if (dir is not null && System.IO.Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
    }

    private static string FormatTime(TimeSpan t) =>
        $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
}
