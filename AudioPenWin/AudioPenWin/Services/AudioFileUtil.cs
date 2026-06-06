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
            // Zoom recordings often start with a QuickTime `wide` atom before an extended-size
            // mdat, causing FFmpeg's prober to score the file at 1 and then fail to find moov.
            // Strategy:
            //   1. Sniff magic bytes to pick the most likely format specifier.
            //   2. Try a remux pass (-map 0 -c copy -movflags +faststart) to normalise the
            //      container into a clean MP4 with moov at the front.
            //   3. If remux works, extract audio from the clean copy.
            //   4. If remux fails, cascade through plausible format overrides for direct
            //      extraction — the container may not match its .mov/.mp4 extension.
            var fmt = await SniffContainerFormatAsync(videoPath);

            var tempPath = Path.ChangeExtension(
                Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), ".mp4");
            try
            {
                var remuxOk = await TryRunAsync(FindFfmpeg(),
                    $"-y -f {fmt} -i \"{videoPath}\" -map 0 -c copy -movflags +faststart \"{tempPath}\"", ct);

                if (remuxOk)
                {
                    await RunAsync(FindFfmpeg(),
                        $"-y -i \"{tempPath}\" -vn -acodec aac -b:a 192k \"{outputM4aPath}\"", ct);
                    return;
                }
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }

            // Remux failed — try direct audio extraction with each plausible format.
            var candidates = new[] { fmt, "mpegts", "avi", "matroska" }.Distinct();
            foreach (var f in candidates)
            {
                if (await TryRunAsync(FindFfmpeg(),
                    $"-y -f {f} -i \"{videoPath}\" -vn -acodec aac -b:a 192k \"{outputM4aPath}\"", ct))
                    return;
            }

            throw new Exception(
                "Could not extract audio from this file. " +
                "The recording may be incomplete (moov atom missing) — " +
                "ensure the Zoom recording has fully saved before importing.");
        }
        else
        {
            await RunAsync(FindFfmpeg(),
                $"-y -i \"{videoPath}\" -vn -acodec aac -b:a 192k \"{outputM4aPath}\"", ct);
        }
    }

    // Reads the first 12 bytes to identify the real container format, so we can pass
    // an explicit -f flag and skip FFmpeg's unreliable low-score format detection.
    private static async Task<string> SniffContainerFormatAsync(string filePath)
    {
        try
        {
            var hdr = new byte[12];
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var n = await fs.ReadAsync(hdr.AsMemory(0, 12));
            if (n < 8) return "mov";

            // MPEG-TS: 188-byte packets; sync byte 0x47 at offset 0 (or offset 4 for M2TS)
            if (hdr[0] == 0x47 || hdr[4] == 0x47) return "mpegts";

            // AVI: RIFF....AVI
            if (hdr[0] == 0x52 && hdr[1] == 0x49 && hdr[2] == 0x46 && hdr[3] == 0x46) return "avi";

            // MKV / WebM
            if (hdr[0] == 0x1A && hdr[1] == 0x45 && hdr[2] == 0xDF && hdr[3] == 0xA3) return "matroska";

            // QuickTime / MP4 family: atom type sits at bytes [4..8].
            // Recognised first atoms: ftyp wide free mdat moov skip junk pnot uuid
            // Also covers extended-size atoms where bytes [0..4] == 00 00 00 01.
            var atom = Encoding.ASCII.GetString(hdr, 4, 4);
            if (atom is "ftyp" or "wide" or "free" or "mdat" or "moov" or
                        "skip" or "junk" or "pnot" or "uuid" or "styp" or "sidx")
                return "mov";

            if (hdr[0] == 0 && hdr[1] == 0 && hdr[2] == 0 && hdr[3] == 1) return "mov"; // extended-size

            return "mov"; // safe default for .mov / .mp4
        }
        catch
        {
            return "mov";
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
