using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Easy2Do.Models;

namespace Easy2Do.Services;

/// <summary>
/// Stores each note as an individual JSON file inside a "notes" subdirectory.
/// A lightweight manifest.json keeps the display order.
/// A FileSystemWatcher detects external changes (OneDrive / Dropbox sync).
/// </summary>
public class StorageService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly BackupService _backupService;
    private const string NotesFolder = "notes";
    private const string ManifestFile = "manifest.json";
    private const string LegacyFile = "notes.json";

    private FileSystemWatcher? _watcher;
    private bool _isSelfWriting;

    // Polling fallback to catch changes missed by FileSystemWatcher (e.g., cloud sync behavior)
    private Timer? _pollTimer;
    private readonly Dictionary<Guid, FileState> _knownFileStates = new();
    private readonly object _lock = new();
    private const int PollIntervalMs = 3000;

    /// <summary>
    /// Fired on the thread-pool when an external change is detected for a specific note file.
    /// The Guid is the note Id parsed from the filename.
    /// </summary>
    public event Action<Guid>? NoteFileChanged;

    /// <summary>
    /// Fired when a note file is deleted externally.
    /// </summary>
    public event Action<Guid>? NoteFileDeleted;

    /// <summary>
    /// Fired when a new note file appears externally.
    /// </summary>
    public event Action<Guid>? NoteFileCreated;

    /// <summary>
    /// Fired with a human-readable status message when a sync operation completes.
    /// </summary>
    public event Action<string>? SyncStatusChanged;

    // Use the source-generated context so serialization is AOT-safe (required for iOS).
    private static readonly JsonSerializerOptions JsonOptions = AppJsonContext.Default.Options;

    public StorageService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _backupService = new BackupService(settingsService);
    }

    /// <summary>Returns the notes directory path (used by PowerSyncService as a proxy for DB path).</summary>
    public string GetDatabasePath()
    {
        return GetNotesDirectory();
    }

    private string GetNotesDirectory()
    {
        var storageLocation = _settingsService.GetStorageLocation();
        return Path.Combine(storageLocation, NotesFolder);
    }

    private string GetManifestPath()
    {
        var storageLocation = _settingsService.GetStorageLocation();
        return Path.Combine(storageLocation, ManifestFile);
    }

    private static string NoteFileName(Guid id) => $"{id}.json";

    private string NoteFilePath(Guid id) => Path.Combine(GetNotesDirectory(), NoteFileName(id));

    // ──────────────────────────── Migration ────────────────────────────

    /// <summary>
    /// One-time migration from the legacy single-file format.
    /// </summary>
    public async Task MigrateIfNeededAsync()
    {
        var storageLocation = _settingsService.GetStorageLocation();
        var legacyPath = Path.Combine(storageLocation, LegacyFile);

        if (!File.Exists(legacyPath))
            return;

        // Already migrated?
        var notesDir = GetNotesDirectory();
        if (Directory.Exists(notesDir) && Directory.GetFiles(notesDir, "*.json").Length > 0)
            return;

        try
        {
            var json = await File.ReadAllTextAsync(legacyPath);
            var notes = JsonSerializer.Deserialize<List<Note>>(json, JsonOptions);
            if (notes is { Count: > 0 })
            {
                EnsureNotesDirectory();
                foreach (var note in notes)
                {
                    await SaveNoteAsync(note);
                }
                await SaveManifestAsync(notes.Select(n => n.Id).ToList());
            }

            // Rename legacy file so it's not re-migrated
            var backupPath = legacyPath + ".bak";
            if (!File.Exists(backupPath))
                File.Move(legacyPath, backupPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Migration error: {ex.Message}");
        }
    }

    // ──────────────────────────── Load ────────────────────────────

    public async Task<List<Note>> LoadAllNotesAsync()
    {
        try
        {
            var notesDir = GetNotesDirectory();
            if (!Directory.Exists(notesDir))
                return new List<Note>();

            var manifest = await LoadManifestAsync();
            var notes = new List<Note>();
            var foundIds = new HashSet<Guid>();

            // Load in manifest order
            if (manifest.Count > 0)
            {
                foreach (var id in manifest)
                {
                    var note = await LoadNoteAsync(id);
                    if (note != null)
                    {
                        notes.Add(note);
                        foundIds.Add(id);
                    }
                }
            }

            // Pick up any note files not in the manifest (created on another machine)
            foreach (var file in Directory.GetFiles(notesDir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (Guid.TryParse(name, out var id) && foundIds.Add(id))
                {
                    var note = await LoadNoteAsync(id);
                    if (note != null)
                        notes.Add(note);
                }
            }

            return notes;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading notes: {ex.Message}");
            return new List<Note>();
        }
    }

    public async Task<Note?> LoadNoteAsync(Guid id)
    {
        try
        {
            var path = NoteFilePath(id);
            System.Diagnostics.Debug.WriteLine($"LoadNoteAsync: loading note file {path}");
            if (!File.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine($"LoadNoteAsync: file does not exist for note {id}");
                return null;
            }
            var json = await File.ReadAllTextAsync(path);
            Console.WriteLine($"[Load] note {id}: len={json.Length}, preview={json.Substring(0, Math.Min(120, json.Length))}");
            try
            {
                var note = JsonSerializer.Deserialize<Note>(json, JsonOptions);
                if (note == null)
                    Console.WriteLine($"[Load] FAILED to deserialize note {id}");
                else
                {
                    note.LastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                    Console.WriteLine($"[Load] note {id}: title='{note.Title}' items={note.Items?.Count}");
                }
                return note;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadNoteAsync: exception for note {id}: {ex.Message}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading note {id}: {ex.Message}");
            return null;
        }
    }

    // ──────────────────────────── Save ────────────────────────────

    public async Task SaveNoteAsync(Note note)
    {
        try
        {
            EnsureNotesDirectory();
            var path = NoteFilePath(note.Id);
            DateTime? fileLastWrite = null;
            if (File.Exists(path))
            {
                fileLastWrite = File.GetLastWriteTimeUtc(path);
            }
            // Only allow save if file is unchanged or does not exist
            if (fileLastWrite == null ||
                note.LastWriteTimeUtc == null ||
                Math.Abs((fileLastWrite.Value - note.LastWriteTimeUtc.Value).TotalSeconds) < 2)
            {
                note.ModifiedDate = DateTime.UtcNow;
                _isSelfWriting = true;
                var json = JsonSerializer.Serialize(note, JsonOptions);
                Console.WriteLine($"[Save] note {note.Id}: title='{note.Title}' items={note.Items?.Count} json_len={json.Length} preview={json.Substring(0, Math.Min(120, json.Length))}");
                await File.WriteAllTextAsync(path, json);
                await _backupService.BackupNoteAsync(note.Id, json);
                note.LastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            }
            else
            {
                throw new InvalidOperationException("The note has been modified elsewhere. Please reload before saving.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving note {note.Id}: {ex.Message}");
            throw;
        }
        finally
        {
            _isSelfWriting = false;
        }
    }

    public async Task DeleteNoteFileAsync(Guid id)
    {
        try
        {
            _isSelfWriting = true;
            var path = NoteFilePath(id);
            if (File.Exists(path))
                File.Delete(path);
            _backupService.DeleteBackupsForNote(id);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting note {id}: {ex.Message}");
        }
        finally
        {
            _isSelfWriting = false;
        }
        await Task.CompletedTask;
    }

    public async Task SaveManifestAsync(IList<Guid> noteIds)
    {
        try
        {
            var storageLocation = _settingsService.GetStorageLocation();
            if (!Directory.Exists(storageLocation))
                Directory.CreateDirectory(storageLocation);

            _isSelfWriting = true;
            // Must use the concrete List<Guid> typed accessor — IList<Guid> is not
            // registered in AppJsonContext and fails on iOS AOT.
            var json = JsonSerializer.Serialize(
                noteIds as List<Guid> ?? noteIds.ToList(),
                AppJsonContext.Default.ListGuid);
            await File.WriteAllTextAsync(GetManifestPath(), json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving manifest: {ex.Message}");
        }
        finally
        {
            _isSelfWriting = false;
        }
    }

    private async Task<List<Guid>> LoadManifestAsync()
    {
        try
        {
            var path = GetManifestPath();
            if (!File.Exists(path))
                return new List<Guid>();

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? new List<Guid>();
        }
        catch
        {
            return new List<Guid>();
        }
    }

    private void EnsureNotesDirectory()
    {
        var dir = GetNotesDirectory();
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    // ──────────────────────────── File Watcher ────────────────────────────

    public void StartWatching()
    {
        StopWatching();

        var notesDir = GetNotesDirectory();
        EnsureNotesDirectory();

        _watcher = new FileSystemWatcher(notesDir, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileCreated;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;

        // Initialize known file timestamps for polling
        lock (_lock)
        {
            _knownFileStates.Clear();
            foreach (var file in Directory.GetFiles(notesDir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (Guid.TryParse(name, out var id))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        _knownFileStates[id] = new FileState(info.LastWriteTimeUtc, info.Length);
                    }
                    catch { }
                }
            }
        }

        // Start polling timer as a fallback for cloud syncs that may bypass FS events
        _pollTimer = new Timer(_ => PollDirectory(), null, PollIntervalMs, PollIntervalMs);
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileCreated;
            _watcher.Deleted -= OnFileDeleted;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Dispose();
            _watcher = null;
        }

        if (_pollTimer != null)
        {
            _pollTimer.Dispose();
            _pollTimer = null;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_isSelfWriting) return;
        if (TryParseNoteId(e.Name, out var id))
            NoteFileChanged?.Invoke(id);
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (_isSelfWriting) return;
        if (TryParseNoteId(e.Name, out var id))
            NoteFileCreated?.Invoke(id);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (_isSelfWriting) return;
        if (TryParseNoteId(e.Name, out var id))
            NoteFileDeleted?.Invoke(id);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (_isSelfWriting) return;
        // Treat rename-in as creation of the new name
        if (TryParseNoteId(e.Name, out var id))
            NoteFileCreated?.Invoke(id);
    }

    private void PollDirectory()
    {
        try
        {
            var notesDir = GetNotesDirectory();
            if (!Directory.Exists(notesDir)) return;

            var currentFiles = Directory.GetFiles(notesDir, "*.json");
            var currentSet = new HashSet<Guid>();

            lock (_lock)
            {
                // Check for created or changed files
                foreach (var file in currentFiles)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!Guid.TryParse(name, out var id)) continue;
                    currentSet.Add(id);
                    FileState state;
                    try
                    {
                        var info = new FileInfo(file);
                        state = new FileState(info.LastWriteTimeUtc, info.Length);
                    }
                    catch { continue; }

                    if (_knownFileStates.TryGetValue(id, out var known))
                    {
                        if (state.LastWriteTimeUtc != known.LastWriteTimeUtc || state.Length != known.Length)
                        {
                            _knownFileStates[id] = state;
                            if (!_isSelfWriting)
                                NoteFileChanged?.Invoke(id);
                        }
                    }
                    else
                    {
                        // New file
                        _knownFileStates[id] = state;
                        if (!_isSelfWriting)
                            NoteFileCreated?.Invoke(id);
                    }
                }

                // Check for deleted files
                var knownIds = _knownFileStates.Keys.ToList();
                foreach (var knownId in knownIds)
                {
                    if (!currentSet.Contains(knownId))
                    {
                        _knownFileStates.Remove(knownId);
                        if (!_isSelfWriting)
                            NoteFileDeleted?.Invoke(knownId);
                    }
                }
            }
        }
        catch
        {
            // ignore polling errors
        }
    }

    private static bool TryParseNoteId(string? fileName, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrEmpty(fileName)) return false;
        var name = Path.GetFileNameWithoutExtension(fileName);
        return Guid.TryParse(name, out id);
    }

    private readonly record struct FileState(DateTime LastWriteTimeUtc, long Length);

    // ── Supabase direct-upload helpers ──────────────────────────────────────

    private static readonly HttpClient SupabaseHttpClient = new();

    private bool IsSupabaseDevSyncEnabled()
    {
        if (!_settingsService.GetSyncEnabled()) return false;
        var url = _settingsService.GetSupabaseUrl();
        var key = _settingsService.GetSupabaseApiKey();
        return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key);
    }

    public async Task TryUpsertNoteToSupabaseAsync(Note note)
    {
        if (!IsSupabaseDevSyncEnabled()) return;
        try
        {
            // Build JSON using JsonArray/JsonObject — AOT-safe, no reflection needed.
            var payload = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = note.Id.ToString(),
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
            await PostUpsertAsync("notes", payload.ToJsonString());
        }
        catch (Exception ex)
        {
            var msg = $"Supabase note upsert failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Supabase] {msg}");
            SyncStatusChanged?.Invoke(msg);
        }
    }

    public async Task TryUpsertNoteItemsToSupabaseAsync(Note note)
    {
        if (!IsSupabaseDevSyncEnabled()) return;
        try
        {
            var payload = new JsonArray();
            for (var i = 0; i < note.Items.Count; i++)
            {
                var item = note.Items[i];
                var obj = new JsonObject
                {
                    ["id"] = item.Id.ToString(),
                    ["note_id"] = note.Id.ToString(),
                    ["text"] = item.Text ?? string.Empty,
                    ["is_completed"] = item.IsCompleted,
                    ["is_heading"] = item.IsHeading,
                    ["is_important"] = item.IsImportant,
                    ["text_attachment"] = item.TextAttachment ?? string.Empty,
                    ["is_alarm_dismissed"] = item.IsAlarmDismissed,
                    ["created_at_utc"] = item.CreatedAtUtc.ToString("O"),
                    ["updated_at_utc"] = item.UpdatedAtUtc.ToString("O"),
                    ["position"] = i
                };
                if (item.DueDate.HasValue) obj["due_date"] = item.DueDate.Value.ToString("O");
                if (item.SnoozeUntil.HasValue) obj["snooze_until"] = item.SnoozeUntil.Value.ToString("O");
                if (item.DeletedAtUtc.HasValue) obj["deleted_at_utc"] = item.DeletedAtUtc.Value.ToString("O");
                payload.Add(obj);
            }
            if (payload.Count > 0)
                await PostUpsertAsync("note_items", payload.ToJsonString());
        }
        catch (Exception ex)
        {
            var msg = $"Supabase item upsert failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Supabase] {msg}");
            SyncStatusChanged?.Invoke(msg);
        }
    }

    public async Task TryDeleteNoteFromSupabaseAsync(Guid noteId)
    {
        if (!IsSupabaseDevSyncEnabled()) return;
        try
        {
            var supabaseUrl = _settingsService.GetSupabaseUrl().TrimEnd('/');
            var supabaseKey = _settingsService.GetSupabaseApiKey();

            var req = new HttpRequestMessage(HttpMethod.Delete, $"{supabaseUrl}/rest/v1/note_items?note_id=eq.{noteId}");
            req.Headers.TryAddWithoutValidation("apikey", supabaseKey);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {supabaseKey}");
            req.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
            await SupabaseHttpClient.SendAsync(req);

            var req2 = new HttpRequestMessage(HttpMethod.Delete, $"{supabaseUrl}/rest/v1/notes?id=eq.{noteId}");
            req2.Headers.TryAddWithoutValidation("apikey", supabaseKey);
            req2.Headers.TryAddWithoutValidation("Authorization", $"Bearer {supabaseKey}");
            req2.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
            await SupabaseHttpClient.SendAsync(req2);

            SyncStatusChanged?.Invoke($"Supabase delete OK (note {noteId}).");
        }
        catch (Exception ex)
        {
            var msg = $"Supabase delete failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Supabase] {msg}");
            SyncStatusChanged?.Invoke(msg);
        }
    }

    private async Task PostUpsertAsync(string table, string json)
    {
        var supabaseUrl = _settingsService.GetSupabaseUrl().TrimEnd('/');
        var supabaseKey = _settingsService.GetSupabaseApiKey();
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Post, $"{supabaseUrl}/rest/v1/{table}");
        req.Headers.TryAddWithoutValidation("apikey", supabaseKey);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {supabaseKey}");
        req.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");
        req.Content = content;
        var resp = await SupabaseHttpClient.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        SyncStatusChanged?.Invoke($"Supabase upsert OK ({table}).");
    }

    /// <summary>
    /// Replaces all local notes with the given list (used by PowerSyncService on remote pull).
    /// Saves each note to disk and updates the manifest.
    /// </summary>
    public async Task ReplaceAllNotesAsync(IReadOnlyList<Note> notes, IReadOnlyList<Guid> orderedIds)
    {
        foreach (var note in notes)
        {
            await SaveNoteAsync(note);
        }
        var ids = orderedIds.Count > 0 ? new List<Guid>(orderedIds) : notes.Select(n => n.Id).ToList();
        await SaveManifestAsync(ids);
        SyncStatusChanged?.Invoke($"Replaced local storage with {notes.Count} remote notes.");
    }

    public void Dispose()
    {
        StopWatching();
    }
}
