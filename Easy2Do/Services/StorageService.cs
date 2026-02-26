using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    // Advisory locking for cross-device editing
    private readonly object _noteLockSync = new();
    private readonly Dictionary<Guid, int> _ownedLockRefCounts = new();
    private Timer? _lockHeartbeatTimer;
    private int _lockHeartbeatRunning;
    private const int LockHeartbeatMs = 4000;
    private const int LockStaleSeconds = 20;
    private const string LockExtension = ".lock.json";
    private readonly string _deviceId;
    private readonly string _deviceName;

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
    /// Fired when another device requests takeover for a note lock this device currently owns.
    /// </summary>
    public event Action<Guid, string>? NoteLockTakeoverRequested;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public StorageService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _backupService = new BackupService(settingsService);
        _deviceId = settingsService.GetDeviceId();
        _deviceName = settingsService.GetDeviceName();
        _lockHeartbeatTimer = new Timer(_ => LockHeartbeatTick(), null, LockHeartbeatMs, LockHeartbeatMs);
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

    private string NoteLockPath(Guid id) => Path.Combine(GetNotesDirectory(), $"{id}{LockExtension}");

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
            System.Diagnostics.Debug.WriteLine($"LoadNoteAsync: file content length {json.Length} for note {id}");
            try
            {
                var note = JsonSerializer.Deserialize<Note>(json, JsonOptions);
                if (note == null)
                    System.Diagnostics.Debug.WriteLine($"LoadNoteAsync: failed to deserialize note {id}");
                else
                {
                    note.LastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                    System.Diagnostics.Debug.WriteLine($"LoadNoteAsync: deserialized note {id} - {note.Title}");
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
            var json = JsonSerializer.Serialize(note, JsonOptions);

            if (File.Exists(path))
            {
                var existingJson = await File.ReadAllTextAsync(path);
                if (string.Equals(existingJson, json, StringComparison.Ordinal))
                {
                    note.LastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                    UpdateKnownState(note.Id, path);
                    return;
                }
            }

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
                _isSelfWriting = true;
                await File.WriteAllTextAsync(path, json);
                await _backupService.BackupNoteAsync(note.Id, json);
                note.LastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                UpdateKnownState(note.Id, path);
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
            lock (_lock)
            {
                _knownFileStates.Remove(id);
            }
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
            var json = JsonSerializer.Serialize(noteIds, JsonOptions);
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

    public async Task<NoteLockAcquireResult> TryAcquireNoteLockAsync(Guid noteId, bool requestTakeover)
    {
        EnsureNotesDirectory();
        var path = NoteLockPath(noteId);
        var now = DateTime.UtcNow;
        NoteLockInfo? existing = null;

        if (File.Exists(path))
        {
            existing = await ReadLockAsync(path);
        }

        if (existing == null || IsStale(existing, now) || existing.DeviceId == _deviceId)
        {
            var mine = new NoteLockInfo
            {
                NoteId = noteId,
                DeviceId = _deviceId,
                DeviceName = _deviceName,
                LastHeartbeatUtc = now
            };
            await WriteLockAsync(path, mine);
            return new NoteLockAcquireResult(true, false, null, null);
        }

        if (!requestTakeover)
        {
            return new NoteLockAcquireResult(false, false, existing.DeviceId, existing.DeviceName);
        }

        existing.TakeoverRequestedByDeviceId = _deviceId;
        existing.TakeoverRequestedByDeviceName = _deviceName;
        existing.TakeoverRequestedUtc = now;
        await WriteLockAsync(path, existing);
        return new NoteLockAcquireResult(false, true, existing.DeviceId, existing.DeviceName);
    }

    public async Task<NoteLockInfo?> GetNoteLockInfoAsync(Guid noteId)
    {
        EnsureNotesDirectory();
        var path = NoteLockPath(noteId);
        if (!File.Exists(path))
            return null;
        var info = await ReadLockAsync(path);
        if (info == null)
            return null;
        if (IsStale(info, DateTime.UtcNow))
            return null;
        return info;
    }

    public bool IsOwnedByThisDevice(NoteLockInfo? info)
    {
        return info != null && string.Equals(info.DeviceId, _deviceId, StringComparison.Ordinal);
    }

    public string ThisDeviceName => _deviceName;

    public async Task<bool> WaitForLockAsync(Guid noteId, TimeSpan timeout, TimeSpan pollDelay)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            var result = await TryAcquireNoteLockAsync(noteId, requestTakeover: false);
            if (result.Acquired)
                return true;

            await Task.Delay(pollDelay);
        }

        return false;
    }

    public async Task<bool> ForceTakeoverAsync(Guid noteId, TimeSpan minRequestAge)
    {
        EnsureNotesDirectory();
        var path = NoteLockPath(noteId);
        var now = DateTime.UtcNow;
        var existing = File.Exists(path) ? await ReadLockAsync(path) : null;

        if (existing == null || IsStale(existing, now) || existing.DeviceId == _deviceId)
        {
            var mine = new NoteLockInfo
            {
                NoteId = noteId,
                DeviceId = _deviceId,
                DeviceName = _deviceName,
                LastHeartbeatUtc = now
            };
            await WriteLockAsync(path, mine);
            return true;
        }

        var requestedByMe = string.Equals(existing.TakeoverRequestedByDeviceId, _deviceId, StringComparison.Ordinal);
        var requestAgeOk = existing.TakeoverRequestedUtc.HasValue &&
                           (now - existing.TakeoverRequestedUtc.Value) >= minRequestAge;

        if (requestedByMe && requestAgeOk)
        {
            var mine = new NoteLockInfo
            {
                NoteId = noteId,
                DeviceId = _deviceId,
                DeviceName = _deviceName,
                LastHeartbeatUtc = now
            };
            await WriteLockAsync(path, mine);
            return true;
        }

        return false;
    }

    public void StartOwnedLockHeartbeat(Guid noteId)
    {
        lock (_noteLockSync)
        {
            if (_ownedLockRefCounts.TryGetValue(noteId, out var count))
                _ownedLockRefCounts[noteId] = count + 1;
            else
                _ownedLockRefCounts[noteId] = 1;
        }
    }

    public async Task StopOwnedLockHeartbeatAsync(Guid noteId)
    {
        var shouldRelease = false;
        lock (_noteLockSync)
        {
            if (_ownedLockRefCounts.TryGetValue(noteId, out var count))
            {
                count--;
                if (count <= 0)
                {
                    _ownedLockRefCounts.Remove(noteId);
                    shouldRelease = true;
                }
                else
                {
                    _ownedLockRefCounts[noteId] = count;
                }
            }
        }

        if (shouldRelease)
        {
            await ReleaseNoteLockIfOwnedAsync(noteId);
        }
    }

    public async Task ReleaseNoteLockIfOwnedAsync(Guid noteId)
    {
        var path = NoteLockPath(noteId);
        if (!File.Exists(path)) return;
        var lockInfo = await ReadLockAsync(path);
        if (lockInfo?.DeviceId == _deviceId)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // best-effort
            }
        }
    }

    private async void LockHeartbeatTick()
    {
        if (Interlocked.Exchange(ref _lockHeartbeatRunning, 1) == 1)
            return;

        try
        {
            Guid[] noteIds;
            lock (_noteLockSync)
            {
                noteIds = _ownedLockRefCounts.Keys.ToArray();
            }

            foreach (var noteId in noteIds)
            {
                if (!IsStillOwned(noteId))
                    continue;

                var path = NoteLockPath(noteId);
                if (!File.Exists(path))
                {
                    if (!IsStillOwned(noteId))
                        continue;
                    var mine = new NoteLockInfo
                    {
                        NoteId = noteId,
                        DeviceId = _deviceId,
                        DeviceName = _deviceName,
                        LastHeartbeatUtc = DateTime.UtcNow
                    };
                    await WriteLockAsync(path, mine);
                    continue;
                }

                var lockInfo = await ReadLockAsync(path);
                if (lockInfo == null)
                {
                    continue;
                }

                if (!IsStillOwned(noteId))
                    continue;

                if (lockInfo.DeviceId != _deviceId)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(lockInfo.TakeoverRequestedByDeviceId) &&
                    !string.Equals(lockInfo.TakeoverRequestedByDeviceId, _deviceId, StringComparison.Ordinal))
                {
                    NoteLockTakeoverRequested?.Invoke(noteId, lockInfo.TakeoverRequestedByDeviceName ?? "another device");
                }

                lockInfo.LastHeartbeatUtc = DateTime.UtcNow;
                await WriteLockAsync(path, lockInfo);
            }
        }
        catch
        {
            // heartbeat is best-effort
        }
        finally
        {
            Interlocked.Exchange(ref _lockHeartbeatRunning, 0);
        }
    }

    private static bool IsStale(NoteLockInfo lockInfo, DateTime nowUtc)
    {
        return (nowUtc - lockInfo.LastHeartbeatUtc).TotalSeconds > LockStaleSeconds;
    }

    private async Task<NoteLockInfo?> ReadLockAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<NoteLockInfo>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteLockAsync(string path, NoteLockInfo lockInfo)
    {
        try
        {
            var json = JsonSerializer.Serialize(lockInfo, JsonOptions);
            await File.WriteAllTextAsync(path, json);
        }
        catch
        {
            // best-effort
        }
    }

    private bool IsStillOwned(Guid noteId)
    {
        lock (_noteLockSync)
        {
            return _ownedLockRefCounts.ContainsKey(noteId);
        }
    }

    private void UpdateKnownState(Guid id, string path)
    {
        try
        {
            var info = new FileInfo(path);
            lock (_lock)
            {
                _knownFileStates[id] = new FileState(info.LastWriteTimeUtc, info.Length);
            }
        }
        catch
        {
            // best-effort cache update
        }
    }

    private readonly record struct FileState(DateTime LastWriteTimeUtc, long Length);

    public void Dispose()
    {
        StopWatching();
        _lockHeartbeatTimer?.Dispose();
        _lockHeartbeatTimer = null;
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
