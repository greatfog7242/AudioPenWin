using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AudioPenWin.Data.Entities;

namespace AudioPenWin.Services;

public static class AudioFileUtil
{
    private static string FindFfmpeg()
    {
        var local = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "ffmpeg.exe");
        if (File.Exists(local)) return local;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            var p = System.IO.Path.Combine(dir.Trim(), "ffmpeg.exe");
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException(
            "ffmpeg.exe not found. Place ffmpeg.exe in the app's Assets folder or in system PATH.");
    }

    public static async Task ExtractAudioFromVideoAsync(string videoPath, string outputM4aPath, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(videoPath).ToLowerInvariant();

        if (ext is ".mov" or ".mp4")
        {
            // Zoom .mov files: moov atom at end + variable resolution/fps.
            // Pass 1: remux to a clean MP4 — broad stream copy (-map 0 -c copy) with
            //         -movflags faststart writes moov at the front without decoding anything.
            // Pass 2: extract audio from the normalized copy.
            // Fallback: if the remux itself fails, attempt direct audio extraction.
            var tempPath = Path.ChangeExtension(
                Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), ".mp4");
            try
            {
                var remuxOk = await TryRunAsync(FindFfmpeg(),
                    $"-y -i \"{videoPath}\" -map 0 -c copy -movflags +faststart \"{tempPath}\"", ct);

                if (remuxOk)
                {
                    await RunAsync(FindFfmpeg(),
                        $"-y -i \"{tempPath}\" -vn -acodec aac -b:a 192k \"{outputM4aPath}\"", ct);
                }
                else
                {
                    await RunAsync(FindFfmpeg(),
                        $"-y -i \"{videoPath}\" -vn -acodec aac -b:a 192k \"{outputM4aPath}\"", ct);
                }
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        else
        {
            await RunAsync(FindFfmpeg(),
                $"-y -i \"{videoPath}\" -vn -acodec aac -b:a 192k \"{outputM4aPath}\"", ct);
        }
    }

    private static async Task<bool> TryRunAsync(string exe, string args, CancellationToken ct)
    {
        try { await RunAsync(exe, args, ct); return true; }
        catch { return false; }
    }

    public static async Task ExtractPcmAsync(string inputPath, string outputPcmPath, CancellationToken ct = default)
    {
        var args = $"-y -i \"{inputPath}\" -ar 16000 -ac 1 -f s16le \"{outputPcmPath}\"";
        await RunAsync(FindFfmpeg(), args, ct);
    }

    public static async Task<string> ExportAsync(
        RecordingEntity recording,
        IEnumerable<TranscriptSegmentEntity> segments,
        string outputRootFolder)
    {
        var date = DateTimeOffset.FromUnixTimeMilliseconds(recording.CreatedAt).ToString("yyyy-MM-dd");
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(recording.Title.Select(c => invalid.Contains(c) ? '_' : c));
        if (safeName.Length > 50) safeName = safeName[..50];
        safeName = safeName.TrimEnd('.');

        var exportDir = Path.Combine(outputRootFolder, $"{safeName}-{date}");
        Directory.CreateDirectory(exportDir);

        if (File.Exists(recording.FilePath))
        {
            var ext = Path.GetExtension(recording.FilePath);
            var destName = recording.SourceType == "VIDEO_IMPORT" ? "video" + ext : "audio" + ext;
            File.Copy(recording.FilePath, Path.Combine(exportDir, destName), overwrite: true);
        }

        var words = segments.OrderBy(s => s.StartMs).ToList();
        await WriteTranscriptTxtAsync(Path.Combine(exportDir, "transcript.txt"), words);
        await WriteTranscriptJsonAsync(Path.Combine(exportDir, "transcript.json"), recording, words);

        return exportDir;
    }

    private static async Task WriteTranscriptTxtAsync(string path, List<TranscriptSegmentEntity> words)
    {
        if (words.Count == 0) { await File.WriteAllTextAsync(path, ""); return; }

        var sb = new StringBuilder();
        bool hasSpeakers = words.Any(w => !string.IsNullOrEmpty(w.SpeakerLabel));

        if (hasSpeakers)
        {
            string? currentSpeaker = null;
            foreach (var w in words)
            {
                if (w.SpeakerLabel != currentSpeaker)
                {
                    if (sb.Length > 0) sb.Append("\n\n");
                    var label = string.IsNullOrEmpty(w.SpeakerLabel) ? "Unknown" : w.SpeakerLabel;
                    sb.AppendLine($"[{label}]");
                    currentSpeaker = w.SpeakerLabel;
                }
                sb.Append(w.Word);
                sb.Append(' ');
            }
        }
        else
        {
            foreach (var w in words) { sb.Append(w.Word); sb.Append(' '); }
        }

        await File.WriteAllTextAsync(path, sb.ToString().TrimEnd());
    }

    private static async Task WriteTranscriptJsonAsync(
        string path, RecordingEntity recording, List<TranscriptSegmentEntity> words)
    {
        var doc = new
        {
            id = recording.Id,
            title = recording.Title,
            createdAt = recording.CreatedAt,
            sourceType = recording.SourceType,
            durationMs = recording.DurationMs,
            words = words.Select(w => new
            {
                word = w.Word,
                startMs = w.StartMs,
                endMs = w.EndMs,
                speaker = w.SpeakerLabel ?? ""
            }).ToArray()
        };
        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private static async Task RunAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, "-hide_banner " + args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi) ?? throw new Exception("Failed to start ffmpeg");
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            var err = await stderr;
            throw new Exception($"ffmpeg failed: {err[..Math.Min(1000, err.Length)]}");
        }
    }
}
