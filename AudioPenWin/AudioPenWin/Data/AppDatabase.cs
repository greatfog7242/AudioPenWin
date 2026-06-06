using Dapper;
using Microsoft.Data.Sqlite;

namespace AudioPenWin.Data;

public class AppDatabase
{
    private readonly string _connectionString;

    public AppDatabase()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioPen");
        Directory.CreateDirectory(dir);
        _connectionString = $"Data Source={Path.Combine(dir, "AudioPen.db")}";
        Initialize();
    }

    public SqliteConnection CreateConnection() => new(_connectionString);

    private void Initialize()
    {
        using var conn = CreateConnection();
        conn.Open();
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS recordings (
                Id          TEXT    PRIMARY KEY,
                Title       TEXT    NOT NULL,
                SourceType  TEXT    NOT NULL,
                FilePath    TEXT    NOT NULL,
                DurationMs  INTEGER NOT NULL DEFAULT 0,
                CreatedAt   INTEGER NOT NULL,
                Status      TEXT    NOT NULL DEFAULT 'PENDING',
                SttProvider TEXT    NOT NULL DEFAULT '',
                Language    TEXT    NOT NULL DEFAULT '',
                SpeakerCount INTEGER NOT NULL DEFAULT 0,
                WordCount   INTEGER NOT NULL DEFAULT 0,
                CostCents   INTEGER NOT NULL DEFAULT 0
            )
            """);
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS transcript_segments (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                RecordingId TEXT    NOT NULL REFERENCES recordings(Id) ON DELETE CASCADE,
                SpeakerLabel TEXT   NOT NULL DEFAULT '',
                StartMs     INTEGER NOT NULL,
                EndMs       INTEGER NOT NULL,
                Word        TEXT    NOT NULL,
                Confidence  REAL    NOT NULL DEFAULT 0
            )
            """);
        conn.Execute("PRAGMA foreign_keys = ON");
        conn.Execute("""
            CREATE INDEX IF NOT EXISTS idx_segments_recording_id
            ON transcript_segments(RecordingId)
            """);
    }
}
