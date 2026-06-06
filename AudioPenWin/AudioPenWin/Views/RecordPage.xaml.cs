using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Media.Capture;
using AudioPenWin.Data.Entities;
using AudioPenWin.Data.Repositories;
using AudioPenWin.Services;
using AudioPenWin.ViewModels;
using TranscriptionService = AudioPenWin.Services.TranscriptionService;

namespace AudioPenWin.Views;

public sealed partial class RecordPage : Page
{
    public RecordViewModel ViewModel { get; } =
        App.Services.GetRequiredService<RecordViewModel>();

    private readonly RecordingService _service =
        App.Services.GetRequiredService<RecordingService>();
    private readonly RecordingRepository _recordings =
        App.Services.GetRequiredService<RecordingRepository>();

    private readonly DispatcherQueue _dq;
    private DispatcherQueueTimer? _timer;

    public RecordPage()
    {
        InitializeComponent();
        _dq = DispatcherQueue.GetForCurrentThread();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await RequestMicrophoneAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopTimer();
        _service.AmplitudeChanged -= OnAmplitude;
        if (_service.IsRecording) _ = _service.StopAsync();
    }

    private async Task RequestMicrophoneAsync()
    {
        try
        {
            var mc = new MediaCapture();
            await mc.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio
            });
            mc.Dispose();
        }
        catch (UnauthorizedAccessException)
        {
            await ShowDialog("Microphone access denied",
                "Please allow microphone access in Windows Privacy Settings → Microphone.");
        }
        catch { /* device may already be in use; NAudio will surface errors on record */ }
    }

    private async void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_service.IsRecording)
        {
            StopTimer();
            SetRecordingUi(false);
            var duration = await _service.StopAsync();
            _service.AmplitudeChanged -= OnAmplitude;
            await SaveAndNavigateAsync(duration);
        }
        else
        {
            await StartRecordingAsync();
        }
    }

    private async Task StartRecordingAsync()
    {
        try
        {
            Waveform.Reset();
            _service.AmplitudeChanged += OnAmplitude;
            await _service.StartAsync();
            SetRecordingUi(true);
            StartTimer();
        }
        catch (Exception ex)
        {
            _service.AmplitudeChanged -= OnAmplitude;
            await ShowDialog("Recording failed", ex.Message);
        }
    }

    private async Task SaveAndNavigateAsync(TimeSpan duration)
    {
        var id = _service.CurrentRecordingId!;
        var entity = new RecordingEntity
        {
            Id = id,
            Title = $"Recording {DateTime.Now:yyyy-MM-dd HH:mm}",
            SourceType = "MIC",
            FilePath = System.IO.Path.Combine(RecordingService.GetRecordingDir(id), "audio.m4a"),
            DurationMs = (long)duration.TotalMilliseconds,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Status = "PENDING",
            SttProvider = App.Preferences.SttProvider.ToString(),
            Language = App.Preferences.Language,
            SpeakerCount = App.Preferences.SpeakerCount
        };
        await _recordings.InsertAsync(entity);
        _ = App.Services.GetRequiredService<TranscriptionService>().EnqueueAsync(id);
        App.MainWindow.Navigate(typeof(PlaybackPage), id);
    }

    private void OnAmplitude(float amplitude) =>
        _dq.TryEnqueue(() => Waveform.AddSample(amplitude));

    private void StartTimer()
    {
        _timer = _dq.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (s, e) =>
        {
            var t = _service.Elapsed;
            TbTimer.Text = $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        };
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void SetRecordingUi(bool recording)
    {
        if (recording)
        {
            BtnRecord.Background = new SolidColorBrush(Colors.DarkRed);
            RecordIcon.Width = 18;
            RecordIcon.Height = 18;
            RecordIcon.RadiusX = 2;
            RecordIcon.RadiusY = 2;
            PulseStoryboard.Begin();
        }
        else
        {
            BtnRecord.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 229, 57, 53));
            RecordIcon.Width = 24;
            RecordIcon.Height = 24;
            RecordIcon.RadiusX = 12;
            RecordIcon.RadiusY = 12;
            PulseStoryboard.Stop();
            TbTimer.Text = "0:00";
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        App.MainWindow.Navigate(typeof(HomePage));

    private async Task ShowDialog(string title, string message)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dlg.ShowAsync();
    }
}
