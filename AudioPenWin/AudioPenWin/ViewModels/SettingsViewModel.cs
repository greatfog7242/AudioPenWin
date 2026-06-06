using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioPenWin.Data.Repositories;
using AudioPenWin.Models;
using AudioPenWin.Services.Stt;

namespace AudioPenWin.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly RecordingRepository _recordings;
    private readonly WhisperSttService _whisper;

    private SttProviderKind _sttProvider;
    private string _language = "";
    private string _gcsApiKey = "";
    private string _assemblyAiApiKey = "";
    private string _outputFolder = "";
    private bool _enableDiarization;
    private int _speakerCount = 2;
    private string _costSummary = "Loading…";

    private bool _isDownloadingModel;
    private double _whisperModelProgress;
    private string _whisperModelStatus = "";

    public SettingsViewModel(RecordingRepository recordings, WhisperSttService whisper)
    {
        _recordings = recordings;
        _whisper = whisper;
        _sttProvider = App.Preferences.SttProvider;
        _whisperModelStatus = _whisper.IsModelDownloaded ? "Model ready" : "Model not downloaded";
        _language = App.Preferences.Language;
        _outputFolder = App.Preferences.OutputFolder;
        _enableDiarization = App.Preferences.EnableDiarization;
        _speakerCount = App.Preferences.SpeakerCount;
        _gcsApiKey = App.Secrets.GcsApiKey;
        _assemblyAiApiKey = App.Secrets.AssemblyAiApiKey;
    }

    public SttProviderKind SttProvider
    {
        get => _sttProvider;
        set { _sttProvider = value; OnPropertyChanged(); SavePrefs(); }
    }

    public string Language
    {
        get => _language;
        set { _language = value; OnPropertyChanged(); SavePrefs(); }
    }

    public string GcsApiKey
    {
        get => _gcsApiKey;
        set { _gcsApiKey = value; OnPropertyChanged(); SaveSecrets(); }
    }

    public string AssemblyAiApiKey
    {
        get => _assemblyAiApiKey;
        set { _assemblyAiApiKey = value; OnPropertyChanged(); SaveSecrets(); }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set { _outputFolder = value; OnPropertyChanged(); SavePrefs(); }
    }

    public bool EnableDiarization
    {
        get => _enableDiarization;
        set { _enableDiarization = value; OnPropertyChanged(); SavePrefs(); }
    }

    public int SpeakerCount
    {
        get => _speakerCount;
        set { _speakerCount = value; OnPropertyChanged(); SavePrefs(); }
    }

    public string CostSummary
    {
        get => _costSummary;
        private set { _costSummary = value; OnPropertyChanged(); }
    }

    public bool IsModelDownloaded => _whisper.IsModelDownloaded;

    public bool IsDownloadingModel
    {
        get => _isDownloadingModel;
        private set { _isDownloadingModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDownloadModel)); }
    }

    public bool CanDownloadModel => !_isDownloadingModel && !_whisper.IsModelDownloaded;

    public double WhisperModelProgress
    {
        get => _whisperModelProgress;
        private set { _whisperModelProgress = value; OnPropertyChanged(); }
    }

    public string WhisperModelStatus
    {
        get => _whisperModelStatus;
        private set { _whisperModelStatus = value; OnPropertyChanged(); }
    }

    public async Task DownloadWhisperModelAsync()
    {
        if (_isDownloadingModel || _whisper.IsModelDownloaded) return;
        IsDownloadingModel = true;
        WhisperModelProgress = 0;
        WhisperModelStatus = "Downloading…";
        try
        {
            var downloaded = 0L;
            var progress = new Progress<double>(_ =>
            {
                downloaded++;
                WhisperModelStatus = $"Downloading… {downloaded * 81920 / 1_048_576.0:F0} MB";
            });
            await _whisper.DownloadModelAsync(progress, CancellationToken.None);
            WhisperModelStatus = "Model ready";
            OnPropertyChanged(nameof(IsModelDownloaded));
            OnPropertyChanged(nameof(CanDownloadModel));
        }
        catch (Exception ex)
        {
            WhisperModelStatus = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloadingModel = false;
        }
    }

    public async Task LoadCostSummaryAsync()
    {
        var rows = (await _recordings.GetCostSummaryAsync()).ToList();
        if (rows.Count == 0)
        {
            CostSummary = "No completed transcriptions yet.";
            return;
        }
        var lines = rows.Select(r =>
        {
            var mins = r.TotalDurationMs / 60000.0;
            var dollars = r.TotalCostCents / 100.0;
            return $"{r.SttProvider}: {mins:F1} min  —  ${dollars:F2}";
        });
        CostSummary = string.Join("\n", lines);
    }

    private void SavePrefs()
    {
        App.Preferences.SttProvider = _sttProvider;
        App.Preferences.Language = _language;
        App.Preferences.OutputFolder = _outputFolder;
        App.Preferences.EnableDiarization = _enableDiarization;
        App.Preferences.SpeakerCount = _speakerCount;
        App.Preferences.Save();
    }

    private void SaveSecrets()
    {
        App.Secrets.GcsApiKey = _gcsApiKey;
        App.Secrets.AssemblyAiApiKey = _assemblyAiApiKey;
        App.Secrets.Save();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
