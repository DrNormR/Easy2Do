using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Easy2Do.Models;
using Microsoft.Data.Sqlite;

namespace Easy2Do.Services;

/// <summary>
/// SQLite-backed storage for notes and items.
/// A legacy JSON import is supported for one-time migration.
/// </summary>
public class StorageService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly BackupService _backupService;

    private const string DatabaseFile = "easy2do.db";
    private const string NotesFolder = "notes";
    private const string ManifestFile = "manifest.json";
    private const string LegacyFile = "notes.json";

    private bool _initialized;
    private readonly object _initLock = new();
    private string? _initializedForPath;

    private readonly string _deviceName;

    /// <summary>
    /// Fired on the thread-pool when an external change is detected for a specific note file.
    /// No-op in SQLite mode (reserved for future sync integration).
    /// </summary>
    public event Action<Guid>? NoteFileChanged;

    /// <summary>
    /// Fired when a note file is deleted externally.
    /// No-op in SQLite mode (reserved for future sync integration).
    /// </summary>
    public event Action<Guid>? NoteFileDeleted;

    /// <summary>
    /// Fired when a new note file appears externally.
    /// No-op in SQLite mode (reserved for future sync integration).
    /// </summary>
    public event Action<Guid>? NoteFileCreated;

    /// <summary>
    /// Fired when another device requests takeover for a note lock this device currently owns.
    /// No-op in SQLite mode.
    /// </summary>
    public event Action<Guid, string>? NoteLockTakeoverRequested;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly HttpClient HttpClient = new();

    public event Action<string>? SyncStatusChanged;

    public StorageService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _backupService = new BackupService(settingsService);
        _deviceName = settingsService.GetDeviceName();
        EnsureDatabaseInitialized();
    }

    public string GetDatabasePath()
    {
        var storageLocation = _settingsService.GetStorageLocation();
        return Path.Combine(storageLocation, DatabaseFile);
    }

    private void EnsureDatabaseInitialized()
    {
        lock (_initLock)
        {
            var dbPath = GetDatabasePath();
            if (_initialized && string.Equals(_initializedForPath, dbPath, StringComparison.OrdinalIgnoreCase))
                return;

            var storageLocation = _settingsService.GetStorageLocation();
            if (!Directory.Exists(storageLocation))
                Directory.CreateDirectory(storageLocation);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS notes (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    color TEXT NOT NULL,
    created_date TEXT NOT NULL,
    modified_date TEXT NOT NULL,
    window_x REAL NOT NULL,
    window_y REAL NOT NULL,
    window_width REAL NOT NULL,
    window_height REAL NOT NULL,
    is_pinned INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS note_items (
    id TEXT PRIMARY KEY,
    note_id TEXT NOT NULL,
    text TEXT NOT NULL,
    is_completed INTEGER NOT NULL,
    is_heading INTEGER NOT NULL,
    is_important INTEGER NOT NULL,
    text_attachment TEXT NOT NULL,
    due_date TEXT NULL,
    is_alarm_dismissed INTEGER NOT NULL,
    snooze_until TEXT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    deleted_at_utc TEXT NULL,
    position INTEGER NOT NULL,
    FOREIGN KEY (note_id) REFERENCES notes(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_note_items_note_id ON note_items(note_id);

CREATE TABLE IF NOT EXISTS note_order (
    id TEXT PRIMARY KEY,
    note_id TEXT NOT NULL,
    sort_order INTEGER NOT NULL
);
";
            command.ExecuteNonQuery();
            EnsureNoteOrderHasIdColumn(connection);

            _initialized = true;
            _initializedForPath = dbPath;
        }
    }

    private static void EnsureNoteOrderHasIdColumn(SqliteConnection connection)
    {
        var hasIdColumn = false;
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(note_order);";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "id", StringComparison.OrdinalIgnoreCase))
                {
                    hasIdColumn = true;
                    break;
                }
            }
        }

        if (hasIdColumn) return;

        using var transaction = connection.BeginTransaction();
        using (var rename = connection.CreateCommand())
        {
            rename.Transaction = transaction;
            rename.CommandText = "ALTER TABLE note_order RENAME TO note_order_old;";
            rename.ExecuteNonQuery();
        }
        using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = @"
CREATE TABLE note_order (
    id TEXT PRIMARY KEY,
    note_id TEXT NOT NULL,
    sort_order INTEGER NOT NULL
);";
            create.ExecuteNonQuery();
        }
        using (var copy = connection.CreateCommand())
        {
            copy.Transaction = transaction;
            copy.CommandText = @"
INSERT INTO note_order (id, note_id, sort_order)
SELECT note_id, note_id, sort_order FROM note_order_old;";
            copy.ExecuteNonQuery();
        }
        using (var drop = connection.CreateCommand())
        {
            drop.Transaction = transaction;
            drop.CommandText = "DROP TABLE note_order_old;";
            drop.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = GetDatabasePath(),
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static string ToDb(DateTime value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string? ToDb(DateTime? value)
    {
        return value?.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTime FromDb(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static DateTime? FromDbNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    // ──────────────────────────── Migration ────────────────────────────

    /// <summary>
    /// One-time migration from the legacy JSON storage format.
    /// </summary>
    public async Task MigrateIfNeededAsync()
    {
        EnsureDatabaseInitialized();

        if (await HasAnyNotesAsync())
            return;

        var storageLocation = _settingsService.GetStorageLocation();
        var legacyPath = Path.Combine(storageLocation, LegacyFile);
        var notesDir = Path.Combine(storageLocation, NotesFolder);
        var manifestPath = Path.Combine(storageLocation, ManifestFile);

        var importedNotes = new List<Note>();

        if (File.Exists(legacyPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(legacyPath);
                var notes = JsonSerializer.Deserialize<List<Note>>(json, JsonOptions) ?? new List<Note>();
                importedNotes.AddRange(notes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Migration error (legacy): {ex.Message}");
            }
        }

        if (Directory.Exists(notesDir))
        {
            foreach (var file in Directory.GetFiles(notesDir, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var note = JsonSerializer.Deserialize<Note>(json, JsonOptions);
                    if (note != null)
                        importedNotes.Add(note);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Migration error (file {file}): {ex.Message}");
                }
            }
        }

        if (importedNotes.Count == 0)
            return;

        var manifestOrder = new List<Guid>();
        if (File.Exists(manifestPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath);
                var ids = JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions);
                if (ids != null)
                    manifestOrder.AddRange(ids);
            }
            catch
            {
                // ignore manifest parse errors
            }
        }

        // Deduplicate by Id (prefer first)
        var uniqueNotes = new Dictionary<Guid, Note>();
        foreach (var note in importedNotes)
        {
            if (!uniqueNotes.ContainsKey(note.Id))
                uniqueNotes[note.Id] = note;
        }

        // Insert notes in manifest order, then any remaining.
        var ordered = new List<Note>();
        foreach (var id in manifestOrder)
        {
            if (uniqueNotes.TryGetValue(id, out var note))
            {
                ordered.Add(note);
                uniqueNotes.Remove(id);
            }
        }
        ordered.AddRange(uniqueNotes.Values);

        foreach (var note in ordered)
        {
            NormalizeItems(note);
            await SaveNoteAsync(note);
        }

        if (manifestOrder.Count > 0)
            await SaveManifestAsync(manifestOrder);
    }

    private async Task<bool> HasAnyNotesAsync()
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM notes;";
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table: notes", StringComparison.OrdinalIgnoreCase))
        {
            EnsureDatabaseInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM notes;";
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
    }

    // ──────────────────────────── Load ────────────────────────────

    public async Task<List<Note>> LoadAllNotesAsync()
    {
        EnsureDatabaseInitialized();

        using var connection = OpenConnection();

        var orderedIds = new List<Guid>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT note_id FROM note_order ORDER BY sort_order;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var idText = reader.GetString(0);
                if (Guid.TryParse(idText, out var id))
                    orderedIds.Add(id);
            }
        }

        var allIds = new List<Guid>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM notes;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var idText = reader.GetString(0);
                if (Guid.TryParse(idText, out var id))
                    allIds.Add(id);
            }
        }

        var seen = new HashSet<Guid>();
        var result = new List<Note>();

        foreach (var id in orderedIds)
        {
            if (!seen.Add(id)) continue;
            var note = await LoadNoteAsync(connection, id);
            if (note != null)
                result.Add(note);
        }

        foreach (var id in allIds)
        {
            if (!seen.Add(id)) continue;
            var note = await LoadNoteAsync(connection, id);
            if (note != null)
                result.Add(note);
        }

        Console.WriteLine($"[Storage] Loaded {result.Count} notes from {GetDatabasePath()}");
        foreach (var note in result)
            Console.WriteLine($"[Storage] Note {note.Id} '{note.Title}'");
        return result;
    }

    public async Task<Note?> LoadNoteAsync(Guid id)
    {
        EnsureDatabaseInitialized();
        using var connection = OpenConnection();
        return await LoadNoteAsync(connection, id);
    }

    private async Task<Note?> LoadNoteAsync(SqliteConnection connection, Guid id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id, title, color, created_date, modified_date, window_x, window_y, window_width, window_height, is_pinned
FROM notes
WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var note = new Note
        {
            Id = Guid.Parse(reader.GetString(0)),
            Title = reader.GetString(1),
            Color = reader.GetString(2),
            CreatedDate = FromDb(reader.GetString(3)),
            ModifiedDate = FromDb(reader.GetString(4)),
            WindowX = reader.GetDouble(5),
            WindowY = reader.GetDouble(6),
            WindowWidth = reader.GetDouble(7),
            WindowHeight = reader.GetDouble(8),
            IsPinned = reader.GetInt32(9) != 0
        };

        await reader.CloseAsync();

        using var itemsCommand = connection.CreateCommand();
        itemsCommand.CommandText = @"
SELECT id, text, is_completed, is_heading, is_important, text_attachment, due_date,
       is_alarm_dismissed, snooze_until, created_at_utc, updated_at_utc, deleted_at_utc
FROM note_items
WHERE note_id = $note_id
ORDER BY position;";
        itemsCommand.Parameters.AddWithValue("$note_id", id.ToString());

        using var itemReader = await itemsCommand.ExecuteReaderAsync();
        while (await itemReader.ReadAsync())
        {
            var item = new TodoItem
            {
                Id = Guid.Parse(itemReader.GetString(0)),
                Text = itemReader.GetString(1),
                IsCompleted = itemReader.GetInt32(2) != 0,
                IsHeading = itemReader.GetInt32(3) != 0,
                IsImportant = itemReader.GetInt32(4) != 0,
                TextAttachment = itemReader.GetString(5),
                DueDate = FromDbNullable(itemReader.IsDBNull(6) ? null : itemReader.GetString(6)),
                IsAlarmDismissed = itemReader.GetInt32(7) != 0,
                SnoozeUntil = FromDbNullable(itemReader.IsDBNull(8) ? null : itemReader.GetString(8)),
                CreatedAtUtc = FromDb(itemReader.GetString(9)),
                UpdatedAtUtc = FromDb(itemReader.GetString(10)),
                DeletedAtUtc = FromDbNullable(itemReader.IsDBNull(11) ? null : itemReader.GetString(11))
            };

            note.Items.Add(item);
        }

        return note;
    }

    // ──────────────────────────── Save ────────────────────────────

    public async Task SaveNoteAsync(Note note)
    {
        EnsureDatabaseInitialized();
        NormalizeItems(note);
        NormalizeWindowBounds(note);
        Console.WriteLine($"[Storage] SaveNote {note.Id} '{note.Title}' to {GetDatabasePath()}");

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO notes (id, title, color, created_date, modified_date, window_x, window_y, window_width, window_height, is_pinned)
VALUES ($id, $title, $color, $created_date, $modified_date, $window_x, $window_y, $window_width, $window_height, $is_pinned)
ON CONFLICT(id) DO UPDATE SET
    title = excluded.title,
    color = excluded.color,
    created_date = excluded.created_date,
    modified_date = excluded.modified_date,
    window_x = excluded.window_x,
    window_y = excluded.window_y,
    window_width = excluded.window_width,
    window_height = excluded.window_height,
    is_pinned = excluded.is_pinned;";

            command.Parameters.AddWithValue("$id", note.Id.ToString());
            command.Parameters.AddWithValue("$title", note.Title ?? string.Empty);
            command.Parameters.AddWithValue("$color", note.Color ?? "#FFFFE680");
            command.Parameters.AddWithValue("$created_date", ToDb(note.CreatedDate));
            command.Parameters.AddWithValue("$modified_date", ToDb(note.ModifiedDate));
            command.Parameters.AddWithValue("$window_x", note.WindowX);
            command.Parameters.AddWithValue("$window_y", note.WindowY);
            command.Parameters.AddWithValue("$window_width", note.WindowWidth);
            command.Parameters.AddWithValue("$window_height", note.WindowHeight);
            command.Parameters.AddWithValue("$is_pinned", note.IsPinned ? 1 : 0);
            await command.ExecuteNonQueryAsync();
        }

        using (var deleteItems = connection.CreateCommand())
        {
            deleteItems.Transaction = transaction;
            deleteItems.CommandText = "DELETE FROM note_items WHERE note_id = $note_id;";
            deleteItems.Parameters.AddWithValue("$note_id", note.Id.ToString());
            await deleteItems.ExecuteNonQueryAsync();
        }

        for (var i = 0; i < note.Items.Count; i++)
        {
            var item = note.Items[i];
            using var insertItem = connection.CreateCommand();
            insertItem.Transaction = transaction;
            insertItem.CommandText = @"
INSERT INTO note_items (
    id, note_id, text, is_completed, is_heading, is_important, text_attachment,
    due_date, is_alarm_dismissed, snooze_until, created_at_utc, updated_at_utc, deleted_at_utc, position
) VALUES (
    $id, $note_id, $text, $is_completed, $is_heading, $is_important, $text_attachment,
    $due_date, $is_alarm_dismissed, $snooze_until, $created_at_utc, $updated_at_utc, $deleted_at_utc, $position
);";

            insertItem.Parameters.AddWithValue("$id", item.Id.ToString());
            insertItem.Parameters.AddWithValue("$note_id", note.Id.ToString());
            insertItem.Parameters.AddWithValue("$text", item.Text ?? string.Empty);
            insertItem.Parameters.AddWithValue("$is_completed", item.IsCompleted ? 1 : 0);
            insertItem.Parameters.AddWithValue("$is_heading", item.IsHeading ? 1 : 0);
            insertItem.Parameters.AddWithValue("$is_important", item.IsImportant ? 1 : 0);
            insertItem.Parameters.AddWithValue("$text_attachment", item.TextAttachment ?? string.Empty);
            insertItem.Parameters.AddWithValue("$due_date", (object?)ToDb(item.DueDate) ?? DBNull.Value);
            insertItem.Parameters.AddWithValue("$is_alarm_dismissed", item.IsAlarmDismissed ? 1 : 0);
            insertItem.Parameters.AddWithValue("$snooze_until", (object?)ToDb(item.SnoozeUntil) ?? DBNull.Value);
            insertItem.Parameters.AddWithValue("$created_at_utc", ToDb(item.CreatedAtUtc));
            insertItem.Parameters.AddWithValue("$updated_at_utc", ToDb(item.UpdatedAtUtc));
            insertItem.Parameters.AddWithValue("$deleted_at_utc", (object?)ToDb(item.DeletedAtUtc) ?? DBNull.Value);
            insertItem.Parameters.AddWithValue("$position", i);
            await insertItem.ExecuteNonQueryAsync();
        }

        using (var ensureOrder = connection.CreateCommand())
        {
            ensureOrder.Transaction = transaction;
            ensureOrder.CommandText = @"
INSERT INTO note_order (id, note_id, sort_order)
VALUES ($note_id, $note_id, COALESCE((SELECT MAX(sort_order) FROM note_order), -1) + 1)
ON CONFLICT(id) DO NOTHING;";
            ensureOrder.Parameters.AddWithValue("$note_id", note.Id.ToString());
            await ensureOrder.ExecuteNonQueryAsync();
        }

        transaction.Commit();

        try
        {
            var json = JsonSerializer.Serialize(note, JsonOptions);
            await _backupService.BackupNoteAsync(note.Id, json);
        }
        catch
        {
            // best-effort backup
        }

        await LogPersistedTitleAsync(note.Id);
        await TryUpsertNoteToSupabaseAsync(note);
        await TryUpsertNoteItemsToSupabaseAsync(note);
    }

    public async Task DeleteNoteFileAsync(Guid id)
    {
        EnsureDatabaseInitialized();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteItems = connection.CreateCommand())
        {
            deleteItems.Transaction = transaction;
            deleteItems.CommandText = "DELETE FROM note_items WHERE note_id = $note_id;";
            deleteItems.Parameters.AddWithValue("$note_id", id.ToString());
            await deleteItems.ExecuteNonQueryAsync();
        }

        using (var deleteNote = connection.CreateCommand())
        {
            deleteNote.Transaction = transaction;
            deleteNote.CommandText = "DELETE FROM notes WHERE id = $id;";
            deleteNote.Parameters.AddWithValue("$id", id.ToString());
            await deleteNote.ExecuteNonQueryAsync();
        }

        using (var deleteOrder = connection.CreateCommand())
        {
            deleteOrder.Transaction = transaction;
            deleteOrder.CommandText = "DELETE FROM note_order WHERE note_id = $note_id;";
            deleteOrder.Parameters.AddWithValue("$note_id", id.ToString());
            await deleteOrder.ExecuteNonQueryAsync();
        }

        transaction.Commit();

        _backupService.DeleteBackupsForNote(id);
        await TryDeleteNoteFromSupabaseAsync(id);
    }

    public async Task SaveManifestAsync(IList<Guid> noteIds)
    {
        EnsureDatabaseInitialized();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM note_order;";
            await clear.ExecuteNonQueryAsync();
        }

        for (var i = 0; i < noteIds.Count; i++)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO note_order (id, note_id, sort_order) VALUES ($id, $note_id, $sort_order);";
            insert.Parameters.AddWithValue("$id", noteIds[i].ToString());
            insert.Parameters.AddWithValue("$note_id", noteIds[i].ToString());
            insert.Parameters.AddWithValue("$sort_order", i);
            await insert.ExecuteNonQueryAsync();
        }

        transaction.Commit();
        await TryUpsertNoteOrderToSupabaseAsync(noteIds);
    }

    public async Task RestoreBackupAsync(string backupFilePath)
    {
        try
        {
            var noteJson = await File.ReadAllTextAsync(backupFilePath);
            var note = JsonSerializer.Deserialize<Note>(noteJson, JsonOptions);
            if (note == null) return;

            NormalizeItems(note);
            await SaveNoteAsync(note);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Restore error: {ex.Message}");
        }
    }

    private static bool NormalizeItems(Note note)
    {
        if (note.Items == null || note.Items.Count == 0)
            return false;

        var changed = false;
        var nowUtc = DateTime.UtcNow;

        foreach (var item in note.Items)
        {
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
                changed = true;
            }

            if (item.CreatedAtUtc == default)
            {
                item.CreatedAtUtc = nowUtc;
                changed = true;
            }

            if (item.UpdatedAtUtc == default)
            {
                item.UpdatedAtUtc = nowUtc;
                changed = true;
            }
        }

        return changed;
    }

    private static void NormalizeWindowBounds(Note note)
    {
        if (double.IsNaN(note.WindowX)) note.WindowX = 0;
        if (double.IsNaN(note.WindowY)) note.WindowY = 0;
        if (double.IsNaN(note.WindowWidth)) note.WindowWidth = 320;
        if (double.IsNaN(note.WindowHeight)) note.WindowHeight = 420;
    }

    // ──────────────────────────── File Watcher (no-op) ────────────────────────────

    public void StartWatching()
    {
    }

    public void StopWatching()
    {
    }

    // ──────────────────────────── Locks (no-op, single-device) ────────────────────────────

    public async Task<NoteLockAcquireResult> TryAcquireNoteLockAsync(Guid noteId, bool requestTakeover)
    {
        await Task.CompletedTask;
        return new NoteLockAcquireResult(true, false, null, null);
    }

    public async Task<NoteLockInfo?> GetNoteLockInfoAsync(Guid noteId)
    {
        await Task.CompletedTask;
        return null;
    }

    public bool IsOwnedByThisDevice(NoteLockInfo? info)
    {
        return true;
    }

    public string ThisDeviceName => _deviceName;

    public async Task<bool> WaitForLockAsync(Guid noteId, TimeSpan timeout, TimeSpan pollDelay)
    {
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> ForceTakeoverAsync(Guid noteId, TimeSpan minRequestAge)
    {
        await Task.CompletedTask;
        return true;
    }

    public void StartOwnedLockHeartbeat(Guid noteId)
    {
    }

    public async Task StopOwnedLockHeartbeatAsync(Guid noteId)
    {
        await Task.CompletedTask;
    }

    public async Task ReleaseNoteLockIfOwnedAsync(Guid noteId)
    {
        await Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    public async Task ReplaceAllNotesAsync(IReadOnlyList<Note> notes, IReadOnlyList<Guid> orderedIds)
    {
        EnsureDatabaseInitialized();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var clearItems = connection.CreateCommand())
        {
            clearItems.Transaction = transaction;
            clearItems.CommandText = "DELETE FROM note_items;";
            await clearItems.ExecuteNonQueryAsync();
        }

        using (var clearNotes = connection.CreateCommand())
        {
            clearNotes.Transaction = transaction;
            clearNotes.CommandText = "DELETE FROM notes;";
            await clearNotes.ExecuteNonQueryAsync();
        }

        using (var clearOrder = connection.CreateCommand())
        {
            clearOrder.Transaction = transaction;
            clearOrder.CommandText = "DELETE FROM note_order;";
            await clearOrder.ExecuteNonQueryAsync();
        }

        foreach (var note in notes)
        {
            NormalizeItems(note);
            NormalizeWindowBounds(note);

            using (var insertNote = connection.CreateCommand())
            {
                insertNote.Transaction = transaction;
                insertNote.CommandText = @"
INSERT INTO notes (id, title, color, created_date, modified_date, window_x, window_y, window_width, window_height, is_pinned)
VALUES ($id, $title, $color, $created_date, $modified_date, $window_x, $window_y, $window_width, $window_height, $is_pinned);";
                insertNote.Parameters.AddWithValue("$id", note.Id.ToString());
                insertNote.Parameters.AddWithValue("$title", note.Title ?? string.Empty);
                insertNote.Parameters.AddWithValue("$color", note.Color ?? "#FFFFE680");
                insertNote.Parameters.AddWithValue("$created_date", ToDb(note.CreatedDate));
                insertNote.Parameters.AddWithValue("$modified_date", ToDb(note.ModifiedDate));
                insertNote.Parameters.AddWithValue("$window_x", note.WindowX);
                insertNote.Parameters.AddWithValue("$window_y", note.WindowY);
                insertNote.Parameters.AddWithValue("$window_width", note.WindowWidth);
                insertNote.Parameters.AddWithValue("$window_height", note.WindowHeight);
                insertNote.Parameters.AddWithValue("$is_pinned", note.IsPinned ? 1 : 0);
                await insertNote.ExecuteNonQueryAsync();
            }

            for (var i = 0; i < note.Items.Count; i++)
            {
                var item = note.Items[i];
                using var insertItem = connection.CreateCommand();
                insertItem.Transaction = transaction;
                insertItem.CommandText = @"
INSERT INTO note_items (
    id, note_id, text, is_completed, is_heading, is_important, text_attachment,
    due_date, is_alarm_dismissed, snooze_until, created_at_utc, updated_at_utc, deleted_at_utc, position
) VALUES (
    $id, $note_id, $text, $is_completed, $is_heading, $is_important, $text_attachment,
    $due_date, $is_alarm_dismissed, $snooze_until, $created_at_utc, $updated_at_utc, $deleted_at_utc, $position
);";
                insertItem.Parameters.AddWithValue("$id", item.Id.ToString());
                insertItem.Parameters.AddWithValue("$note_id", note.Id.ToString());
                insertItem.Parameters.AddWithValue("$text", item.Text ?? string.Empty);
                insertItem.Parameters.AddWithValue("$is_completed", item.IsCompleted ? 1 : 0);
                insertItem.Parameters.AddWithValue("$is_heading", item.IsHeading ? 1 : 0);
                insertItem.Parameters.AddWithValue("$is_important", item.IsImportant ? 1 : 0);
                insertItem.Parameters.AddWithValue("$text_attachment", item.TextAttachment ?? string.Empty);
                insertItem.Parameters.AddWithValue("$due_date", (object?)ToDb(item.DueDate) ?? DBNull.Value);
                insertItem.Parameters.AddWithValue("$is_alarm_dismissed", item.IsAlarmDismissed ? 1 : 0);
                insertItem.Parameters.AddWithValue("$snooze_until", (object?)ToDb(item.SnoozeUntil) ?? DBNull.Value);
                insertItem.Parameters.AddWithValue("$created_at_utc", ToDb(item.CreatedAtUtc));
                insertItem.Parameters.AddWithValue("$updated_at_utc", ToDb(item.UpdatedAtUtc));
                insertItem.Parameters.AddWithValue("$deleted_at_utc", (object?)ToDb(item.DeletedAtUtc) ?? DBNull.Value);
                insertItem.Parameters.AddWithValue("$position", i);
                await insertItem.ExecuteNonQueryAsync();
            }
        }

        var orderSource = orderedIds.Count > 0 ? orderedIds : notes.Select(n => n.Id).ToList();
        for (var i = 0; i < orderSource.Count; i++)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO note_order (id, note_id, sort_order) VALUES ($id, $note_id, $sort_order);";
            insert.Parameters.AddWithValue("$id", orderSource[i].ToString());
            insert.Parameters.AddWithValue("$note_id", orderSource[i].ToString());
            insert.Parameters.AddWithValue("$sort_order", i);
            await insert.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }

    private bool IsSupabaseDevSyncEnabled()
    {
        if (!_settingsService.GetSyncEnabled()) return false;
        var url = _settingsService.GetSupabaseUrl();
        var key = _settingsService.GetSupabaseApiKey();
        return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key);
    }

    private async Task TryUpsertNoteToSupabaseAsync(Note note)
    {
        if (!IsSupabaseDevSyncEnabled()) return;
        try
        {
            var payload = new[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = note.Id,
                    ["title"] = note.Title ?? string.Empty,
                    ["color"] = note.Color ?? "#FFFFE680",
                    ["created_date"] = note.CreatedDate.ToString("O"),
                    ["modified_date"] = note.ModifiedDate.ToString("O"),
                    ["window_x"] = double.IsNaN(note.WindowX) ? 0 : note.WindowX,
                    ["window_y"] = double.IsNaN(note.WindowY) ? 0 : note.WindowY,
                    ["window_width"] = double.IsNaN(note.WindowWidth) ? 320 : note.WindowWidth,
                    ["window_height"] = double.IsNaN(note.WindowHeight) ? 420 : note.WindowHeight,
                    ["is_pinned"] = note.IsPinned
                }
            };

            await PostUpsertAsync("notes", payload);
        }
        catch (Exception ex)
        {
            var message = $"Supabase note upsert failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Supabase] {message}");
            SyncStatusChanged?.Invoke(message);
        }
    }

    private async Task TryUpsertNoteItemsToSupabaseAsync(Note note)
    {
        if (!IsSupabaseDevSyncEnabled()) return;
        try
        {
            var payload = new List<Dictionary<string, object?>>();
            for (var i = 0; i < note.Items.Count; i++)
            {
                var item = note.Items[i];
                payload.Add(new Dictionary<string, object?>
                {
                    ["id"] = item.Id,
                    ["note_id"] = note.Id,
                    ["text"] = item.Text ?? string.Empty,
                    ["is_completed"] = item.IsCompleted,
                    ["is_heading"] = item.IsHeading,
                    ["is_important"] = item.IsImportant,
                    ["text_attachment"] = item.TextAttachment ?? string.Empty,
                    ["due_date"] = item.DueDate?.ToString("O"),
                    ["is_alarm_dismissed"] = item.IsAlarmDismissed,
                    ["snooze_until"] = item.SnoozeUntil?.ToString("O"),
                    ["created_at_utc"] = item.CreatedAtUtc.ToString("O"),
                    ["updated_at_utc"] = item.UpdatedAtUtc.ToString("O"),
                    ["deleted_at_utc"] = item.DeletedAtUtc?.ToString("O"),
                    ["position"] = i
                });
            }

            if (payload.Count > 0)
                await PostUpsertAsync("note_items", payload);
        }
        catch (Exception ex)
        {
            var message = $"Supabase item upsert failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Supabase] {message}");
            SyncStatusChanged?.Invoke(message);
        }
    }

    private async Task TryUpsertNoteOrderToSupabaseAsync(IList<Guid> noteIds)
    {
        if (!IsSupabaseDevSyncEnabled()) return;
        try
        {
            var payload = new List<Dictionary<string, object?>>();
            for (var i = 0; i < noteIds.Count; i++)
            {
                payload.Add(new Dictionary<string, object?>
                {
                    ["id"] = noteIds[i],
                    ["note_id"] = noteIds[i],
                    ["sort_order"] = i
                });
            }

            if (payload.Count > 0)
                await PostUpsertAsync("note_order", payload);
        }
        catch (Exception ex)
        {
            var message = $"Supabase order upsert failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Supabase] {message}");
            SyncStatusChanged?.Invoke(message);
        }
    }

    private async Task TryDeleteNoteFromSupabaseAsync(Guid id)
    {
        if (!IsSupabaseDevSyncEnabled()) return;
        try
        {
            await DeleteAsync("notes", id);
        }
        catch (Exception ex)
        {
            var message = $"Supabase delete failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Supabase] {message}");
            SyncStatusChanged?.Invoke(message);
        }
    }

    private async Task PostUpsertAsync(string table, object payload)
    {
        var supabaseUrl = _settingsService.GetSupabaseUrl();
        var supabaseKey = _settingsService.GetSupabaseApiKey();
        var request = new HttpRequestMessage(HttpMethod.Post, $"{supabaseUrl.TrimEnd('/')}/rest/v1/{table}");
        request.Headers.TryAddWithoutValidation("apikey", supabaseKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {supabaseKey}");
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates");
        var json = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        SyncStatusChanged?.Invoke($"Supabase upsert OK ({table}).");
    }

    private async Task DeleteAsync(string table, Guid id)
    {
        var supabaseUrl = _settingsService.GetSupabaseUrl();
        var supabaseKey = _settingsService.GetSupabaseApiKey();
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{supabaseUrl.TrimEnd('/')}/rest/v1/{table}?id=eq.{id}");
        request.Headers.TryAddWithoutValidation("apikey", supabaseKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {supabaseKey}");
        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        SyncStatusChanged?.Invoke($"Supabase delete OK ({table}).");
    }

    private async Task LogPersistedTitleAsync(Guid id)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT title FROM notes WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            var result = await command.ExecuteScalarAsync();
            Console.WriteLine($"[Storage] Persisted title for {id}: '{result}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Persisted title read failed: {ex.Message}");
        }
    }
}

public record NoteLockAcquireResult(
    bool Acquired,
    bool TakeoverRequested,
    string? OwnerDeviceId,
    string? OwnerDeviceName);

public class NoteLockInfo
{
    public Guid NoteId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public DateTime LastHeartbeatUtc { get; set; }
    public string? TakeoverRequestedByDeviceId { get; set; }
    public string? TakeoverRequestedByDeviceName { get; set; }
    public DateTime? TakeoverRequestedUtc { get; set; }
}
