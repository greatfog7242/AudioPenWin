using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace AudioPenWin.Services;

public class RecordingService : IDisposable
{
    public event Action<float>? AmplitudeChanged;

    private WasapiCapture? _capture;
    private WaveFileWriter? _wavWriter;
    private TaskCompletionSource? _stoppedTcs;
    private readonly object _writeLock = new();

    private string? _recordingId;
    private string? _wavPath;
    private DateTime _startTime;
    private float _peakSample;
    private long _samplesSinceEmit;
    private bool _isRecording;

    public bool IsRecording => _isRecording;
    public string? CurrentRecordingId => _recordingId;
    public TimeSpan Elapsed => _isRecording ? DateTime.UtcNow - _startTime : TimeSpan.Zero;

    public Task<string> StartAsync()
    {
        _recordingId = Guid.NewGuid().ToString();
        var dir = GetRecordingDir(_recordingId);
        Directory.CreateDirectory(dir);

        _capture = new WasapiCapture();
        var cf = _capture.WaveFormat;
        var pcmFormat = new WaveFormat(cf.SampleRate, 16, cf.Channels);

        _wavPath = Path.Combine(dir, "audio_tmp.wav");
        _wavWriter = new WaveFileWriter(_wavPath, pcmFormat);

        _peakSample = 0f;
        _samplesSinceEmit = 0;
        _startTime = DateTime.UtcNow;
        _isRecording = true;

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnCaptureStopped;
        _capture.StartRecording();

        return Task.FromResult(_recordingId);
    }

    public async Task<TimeSpan> StopAsync()
    {
        if (!_isRecording) return TimeSpan.Zero;

        var duration = DateTime.UtcNow - _startTime;
        _isRecording = false;
        _stoppedTcs = new TaskCompletionSource();
        _capture!.StopRecording();
        await _stoppedTcs.Task;

        // Convert WAV → M4A on a background thread
        var m4aPath = Path.Combine(GetRecordingDir(_recordingId!), "audio.m4a");
        await Task.Run(() =>
        {
            using var reader = new AudioFileReader(_wavPath!);
            MediaFoundationEncoder.EncodeToAac(reader, m4aPath);
        });
        File.Delete(_wavPath!);

        return duration;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_isRecording) return;

        var cf = _capture!.WaveFormat;
        byte[] pcm16 = cf.Encoding == WaveFormatEncoding.IeeeFloat
            ? ConvertFloat32To16(e.Buffer, e.BytesRecorded)
            : e.Buffer[..e.BytesRecorded];

        lock (_writeLock)
            _wavWriter?.Write(pcm16, 0, pcm16.Length);

        UpdateAmplitude(pcm16, cf.SampleRate * cf.Channels);
    }

    private void UpdateAmplitude(byte[] pcm16, int samplesPerSecond)
    {
        for (int i = 0; i + 1 < pcm16.Length; i += 2)
        {
            float abs = Math.Abs(BitConverter.ToInt16(pcm16, i) / 32768f);
            if (abs > _peakSample) _peakSample = abs;
        }
        _samplesSinceEmit += pcm16.Length / 2;
        if (_samplesSinceEmit >= samplesPerSecond * 50L / 1000)
        {
            AmplitudeChanged?.Invoke(_peakSample);
            _peakSample = 0f;
            _samplesSinceEmit = 0;
        }
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        lock (_writeLock)
        {
            _wavWriter?.Dispose();
            _wavWriter = null;
        }
        _stoppedTcs?.TrySetResult();
    }

    public static string GetRecordingDir(string id) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioPen", "recordings", id);

    private static byte[] ConvertFloat32To16(byte[] buffer, int byteCount)
    {
        int n = byteCount / 4;
        var result = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            float f = BitConverter.ToSingle(buffer, i * 4);
            short s = (short)(Math.Clamp(f, -1f, 1f) * 32767);
            BitConverter.TryWriteBytes(result.AsSpan(i * 2), s);
        }
        return result;
    }

    public void Dispose()
    {
        if (_isRecording) _capture?.StopRecording();
        _capture?.Dispose();
        lock (_writeLock) { _wavWriter?.Dispose(); }
    }
}
