# AudioPen for Windows — Implementation Plan

## Context
Port the Android AudioPen app to a native Windows desktop application with feature parity. The Android app records audio, imports video/audio files, transcribes via Google Cloud STT v2 / AssemblyAI / on-device Whisper, and plays back audio/video synchronized with a word-level transcript. The Windows version will be fully standalone (its own database and file storage, no Android sync).

---

## Technology Stack

| Concern | Android | Windows |
|---|---|---|
| UI | Jetpack Compose | **WinUI 3** (Windows App SDK 1.6, C# 12, .NET 9) |
| Navigation | Compose NavHost | **Frame + Page** navigation |
| Database | Room (SQLite) | **Microsoft.Data.Sqlite + Dapper** |
| Preferences | DataStore + EncryptedSharedPrefs | **System.Text.Json** settings.json + **DPAPI** (ProtectedData) for API keys |
| Audio recording | MediaRecorder + AudioRecord | **NAudio** (WasapiCapture for PCM, MediaFoundationEncoder for M4A) |
| Audio/video playback | ExoPlayer | **MediaPlayerElement** (WinUI 3 built-in) |
| Audio extraction | MediaExtractor + MediaCodec | **FFmpeg CLI** (bundled `ffmpeg.exe` via `Process`) |
| Waveform | Canvas (Compose) | **Win2D** (CanvasControl in WinUI 3) |
| HTTP | Retrofit + OkHttp | **System.Net.Http.HttpClient** |
| Background work | WorkManager | **Task + CancellationToken** (no daemon process needed) |
| Local STT | Android SpeechRecognizer | **Whisper.net** (wraps whisper.cpp, offline) |
| DI | Hilt | **Microsoft.Extensions.DependencyInjection** |
| Speaker colors | Material3 palette | Same 6-color palette in XAML brushes |

---

## Project Structure

```
AudioPenWin/
  AudioPenWin.sln
  AudioPenWin/
    AudioPenWin.csproj        (WinUI 3, net9.0-windows10.0.19041)
    App.xaml / App.xaml.cs
    MainWindow.xaml           (Frame host)
    Data/
      AppDatabase.cs          (Dapper helpers, migration runner)
      Entities/
        RecordingEntity.cs
        TranscriptSegmentEntity.cs
      Dao/
        RecordingDao.cs
        TranscriptSegmentDao.cs
      Repositories/
        RecordingRepository.cs
        TranscriptRepository.cs
    Services/
      RecordingService.cs     (NAudio capture, dual M4A+PCM)
      TranscriptionService.cs (background Task orchestrator)
      Stt/
        ISttProvider.cs
        GcsSttService.cs
        AssemblySttService.cs
        WhisperSttService.cs
      AudioFileUtil.cs        (FFmpeg extraction, export TXT/JSON)
    ViewModels/
      HomeViewModel.cs
      RecordViewModel.cs
      PlaybackViewModel.cs
      SettingsViewModel.cs
    Views/
      HomePage.xaml
      RecordPage.xaml
      PlaybackPage.xaml
      SettingsPage.xaml
      Controls/
        WaveformControl.xaml  (Win2D CanvasControl)
        TranscriptView.xaml
        AudioControlBar.xaml
    Models/
      AppPreferences.cs       (settings.json POCO)
      AppSecrets.cs           (DPAPI-encrypted API keys)
    Assets/                   (icons, ffmpeg.exe)
```

---

## Storage Layout

```
%AppData%\AudioPen\
  AudioPen.db
  settings.json
  secrets.bin          (DPAPI-encrypted blob for API keys)
  recordings\
    <uuid>\
      audio.m4a        (MIC recording)
      audio.pcm        (deleted after transcription)
      video.mp4        (VIDEO import)
      audio_import.mp3 (AUDIO import)
```

---

## Implementation Phases

### Phase 1 — Project Scaffolding & Navigation Shell
- Create WinUI 3 solution targeting `net9.0-windows10.0.19041`
- Add NuGet: `NAudio`, `Microsoft.Data.Sqlite`, `Dapper`, `Whisper.net`, `Microsoft.Win2D.Win2D.WinUI`, `Microsoft.Extensions.DependencyInjection`
- Bundle `ffmpeg.exe` (GPL build, x64) as a content asset
- `MainWindow` hosts a `Frame`; navigation helper wraps `Frame.Navigate(typeof(Page), parameter)`
- Wire DI container in `App.xaml.cs`
- **Deliverable**: app launches, navigates between 4 empty pages

### Phase 2 — Data Layer
- Create SQLite DB at `%AppData%\AudioPen\AudioPen.db` on first run
- Schema mirrors Android exactly:
  - `recordings` table: all columns from `RecordingEntity`
  - `transcript_segments` table: all columns from `TranscriptSegmentEntity` with CASCADE DELETE
- Dapper-based DAOs: `RecordingDao` and `TranscriptSegmentDao` (same method signatures as Android)
- `AppPreferences` serialized to `settings.json`; API keys encrypted with `ProtectedData.Protect` (DPAPI, user scope)
- **Deliverable**: can insert/query recordings and segments in a unit test

### Phase 3 — Settings Page
- Sections: STT provider (RadioButton), Language (RadioButton), API Keys (PasswordBox with reveal toggle), Output Folder (FolderPicker), Diarization toggle, Cost Calculator, About
- Real-time save on every change (no explicit Save button), same as Android
- Cost calculator: query DB for word counts by provider + date range, compute `minutes × rate`
- **Deliverable**: settings persist across app restarts; API keys survive restart via DPAPI

### Phase 4 — Audio Recording Service + Waveform
- `RecordingService` uses NAudio:
  - `WasapiCapture` → PCM 16kHz mono (for STT)
  - `MediaFoundationWriter` → M4A AAC (for playback)
  - Amplitude event every 50ms (normalize to 0–1)
- `WaveformControl` (Win2D `CanvasControl`): 60 bars updated on amplitude event
- `RecordPage`: permission check (microphone capability in `Package.appxmanifest`), pulsing FAB, elapsed timer
- On stop: write `RecordingEntity` (status=PENDING) to DB, navigate to `PlaybackPage`
- **Deliverable**: can record, see waveform, stop; M4A file exists on disk

### Phase 5 — Background Transcription
- `TranscriptionService.EnqueueAsync(recordingId)` starts a `Task` with `CancellationToken`
- Updates recording status to PROCESSING in DB before starting, DONE/FAILED on completion
- **GCS path** (`GcsSttService`): PCM → FLAC via `NAudio.Lame` or raw LINEAR16 → base64 → POST LRO → poll every 5s → parse words → bulk insert `TranscriptSegmentEntity`
- **AssemblyAI path** (`AssemblySttService`): upload M4A → POST transcript → poll every 5s → parse words → insert
- Status badge on `HomeCard` reflects DB status (auto-refreshed via polling or SQLite change notifications)
- **Deliverable**: GCS and AssemblyAI transcription fully functional; word-level segments in DB

### Phase 6 — Home Page
- `ItemsRepeater` or `ListView` bound to `ObservableCollection<RecordingViewModel>` loaded from DB
- `RecordingCard`: source icon, title, date+duration, status badge, speaker chip
- Search box (filter by title/transcript text)
- Right-click context menu: Rename (dialog), Delete (confirmation dialog → soft delete → delete folder)
- Primary FAB → `RecordPage`; secondary FAB → `FileOpenPicker` (video/\* or audio/\*) → `AudioFileUtil.ExtractAudio` via ffmpeg → enqueue transcription → navigate to `PlaybackPage`
- **Deliverable**: full recording list with import, rename, delete, search

### Phase 7 — Playback Page
- `MediaPlayerElement` for audio/video; for VIDEO source, render player in top 40%, transcript in bottom 60%
- Synchronized playback: `DispatcherTimer` at 50ms → `MediaPlayer.PlaybackSession.Position` → binary search on loaded transcript words → highlight active word, auto-scroll
- Tap word → `MediaPlayer.PlaybackSession.Position = TimeSpan.FromMilliseconds(word.StartMs)`
- `AudioControlBar`: seek slider, ±10s buttons, play/pause, speed selector (0.75×/1.0×/1.25×/1.5× via `MediaPlayer.PlaybackSession.PlaybackRate`)
- Editable title (inline `TextBox`)
- Share: copy M4A/MP4 to clipboard or open in Explorer
- **Deliverable**: full synchronized playback with all transport controls

### Phase 8 — Whisper.net Local STT
- `WhisperSttService`: load `ggml-base.bin` (or `ggml-small.bin`) model from `Assets\`
- Process PCM 16kHz mono float array via `WhisperFactory.CreateBuilder().WithModel(...).Build()`
- `SegmentData` → `TranscriptSegmentEntity` (Whisper provides word-level timestamps with `--word-timestamps`)
- Show model download progress on first run (download from Hugging Face via `HttpClient`)
- **Deliverable**: local transcription works offline, no API key required

### Phase 9 — Export & File Management
- `AudioFileUtil.ExportAsync(recordingId, outputFolderPath)`:
  - Create `<title>-yyyy-MM-dd\` subfolder
  - Copy M4A/MP4
  - Write `transcript.txt` (speaker blocks)
  - Write `transcript.json` (title + word array matching Android schema)
- Export triggered from PlaybackPage share button (or context menu on HomeCard)
- **Deliverable**: exported files match Android format exactly

### Phase 10 — Polish
- Speaker colors: 6 XAML `SolidColorBrush` resources matching Android palette
- Dark/light theme: respect Windows system theme via `Application.RequestedTheme`
- Proper `Package.appxmanifest`: microphone capability, `AppData` folder, file type associations
- App icon set
- Keyboard shortcuts: Space = play/pause, ←/→ = ±10s, Ctrl+R = new recording
- **Deliverable**: shippable release build

---

## Key Technical Notes

- **FFmpeg bundling**: Ship `ffmpeg.exe` (x64 GPL) in `Assets\`; call via `Process.Start` with `-i input -ar 16000 -ac 1 -f s16le output.pcm`. No LGPL linking issues.
- **Whisper model**: Download `ggml-base.en.bin` (~150MB) on first Settings open if not present; store in `%AppData%\AudioPen\models\`.
- **DPAPI encryption**: `ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.CurrentUser)` — tied to the Windows user account, no password needed.
- **WinUI 3 microphone permission**: Must declare `<DeviceCapability Name="microphone"/>` in `Package.appxmanifest`; unpackaged apps use `Windows.Media.Capture.MediaCapture.RequestAccessAsync()`.
- **No WorkManager**: Background transcription runs as a `Task`; if the app is closed mid-transcription, status stays PROCESSING and re-queues on next launch (check for PROCESSING records at startup).
- **GCS FLAC encoding**: Use `NAudio` `WaveFileWriter` → encode to FLAC with `NAudio.Flac` or ship `flac.exe`. Fallback: send raw LINEAR16 (PCM) as base64 — GCS v2 supports it natively.

---

## Verification Plan

| Phase | How to verify |
|---|---|
| 1 | App launches, all 4 pages reachable via nav |
| 2 | Write unit test: insert recording, query by id, insert segments, query by recordingId |
| 3 | Change API key, restart app, confirm key survives; change output folder, confirm path saves |
| 4 | Record 10s of speech, verify M4A + PCM files exist in `%AppData%\AudioPen\recordings\<uuid>\` |
| 5 | Record + transcribe via GCS, verify segments appear in DB and status = DONE |
| 6 | Import a video file, confirm audio extraction + transcription enqueued; search for a word |
| 7 | Play a transcribed recording, tap a word mid-sentence, confirm audio seeks to that word |
| 8 | Disable internet, transcribe via Whisper, confirm offline word-level transcript |
| 9 | Export a recording, confirm folder + TXT + JSON created matching Android schema |
| 10 | Toggle Windows dark mode, confirm app follows theme |
