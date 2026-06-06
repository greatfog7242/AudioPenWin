using System.Diagnostics;
using Whisper.net;
using Whisper.net.Ggml;
using AudioPenWin.Data.Entities;

namespace AudioPenWin.Services.Stt;

public class WhisperSttService : ISttProvider
{
    private static readonly string ModelsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioPen", "models");

    public static readonly string ModelPath = Path.Combine(ModelsDir, "ggml-base.en.bin");

    public bool IsModelDownloaded => File.Exists(ModelPath);

    public async Task DownloadModelAsync(IProgress<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ModelsDir);
        var tmpPath = ModelPath + ".tmp";

        using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.BaseEn);
        await using var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        int read;
        long downloaded = 0;
        while ((read = await modelStream.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            // WhisperGgmlDownloader doesn't expose content-length, so show indeterminate progress
            progress.Report(downloaded % 1 == 0 ? -1 : 0);
        }

        await dst.FlushAsync(ct);
        dst.Close();
        File.Move(tmpPath, ModelPath, overwrite: true);
    }

    public async Task<IReadOnlyList<TranscriptSegmentEntity>> TranscribeAsync(
        string recordingId, string audioFilePath, string language,
        bool enableDiarization, int speakerCount, CancellationToken ct)
    {
        if (!IsModelDownloaded)
            throw new InvalidOperationException(
                "Whisper model not downloaded. Open Settings and tap \"Download model\".");

        var wavPath = Path.Combine(Path.GetDirectoryName(audioFilePath)!, "whisper_tmp.wav");
        await ConvertToWavAsync(audioFilePath, wavPath, ct);

        try
        {
            return await RunWhisperAsync(recordingId, wavPath, language, ct);
        }
        finally
        {
            try { File.Delete(wavPath); } catch { }
        }
    }

    private static async Task<IReadOnlyList<TranscriptSegmentEntity>> RunWhisperAsync(
        string recordingId, string wavPath, string language, CancellationToken ct)
    {
        var lang = MapLanguage(language);
        var results = new List<TranscriptSegmentEntity>();

        using var factory = WhisperFactory.FromPath(ModelPath);
        await using var processor = factory.CreateBuilder()
            .WithLanguage(lang)
            .Build();

        await using var fileStream = File.OpenRead(wavPath);
        await foreach (var seg in processor.ProcessAsync(fileStream, ct))
        {
            var text = seg.Text.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            results.Add(new TranscriptSegmentEntity
            {
                RecordingId = recordingId,
                Word = text,
                StartMs = (long)seg.Start.TotalMilliseconds,
                EndMs = (long)seg.End.TotalMilliseconds,
                Confidence = 1.0,
                SpeakerLabel = ""
            });
        }

        return results;
    }

    private static async Task ConvertToWavAsync(string inputPath, string outputWavPath, CancellationToken ct)
    {
        var ffmpeg = FindFfmpeg();
        var args = $"-y -i \"{inputPath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{outputWavPath}\"";
        var psi = new ProcessStartInfo(ffmpeg, args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi) ?? throw new Exception("Failed to start ffmpeg");
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync(ct);
            throw new Exception($"ffmpeg failed: {err[..Math.Min(300, err.Length)]}");
        }
    }

    private static string FindFfmpeg()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "Assets", "ffmpeg.exe");
        if (File.Exists(local)) return local;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            var p = Path.Combine(dir.Trim(), "ffmpeg.exe");
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException(
            "ffmpeg.exe not found. Place ffmpeg.exe in the app's Assets folder or in system PATH.");
    }

    private static string MapLanguage(string langCode) => langCode.ToLowerInvariant() switch
    {
        "en-us" or "en-gb" or "en" => "en",
        "zh"                       => "zh",
        "auto"                     => "auto",
        _                          => "auto"
    };
}
