using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public StorageService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _backupService = new BackupService(settingsService);
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
            System.Diagnostics.Debug.WriteLine($"LoadNoteAsync: file content length {json.Length} for note {id}");
            try
            {
                var note = JsonSerializer.Deserialize<Note>(json, JsonOptions);
                if (note == null)
                    System.Diagnostics.Debug.WriteLine($"LoadNoteAsync: failed to deserialize note {id}");
                else
                    System.Diagnostics.Debug.WriteLine($"LoadNoteAsync: deserialized note {id} - {note.Title}");
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
            if (fileLastWrite == null || Math.Abs((fileLastWrite.Value - note.ModifiedDate.ToUniversalTime()).TotalSeconds) < 2)
            {
                note.ModifiedDate = DateTime.UtcNow;
                _isSelfWriting = true;
                var json = JsonSerializer.Serialize(note, JsonOptions);
                await File.WriteAllTextAsync(path, json);
                await _backupService.BackupNoteAsync(note.Id, json);
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

    private static bool TryParseNoteId(string? fileName, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrEmpty(fileName)) return false;
        var name = Path.GetFileNameWithoutExtension(fileName);
        return Guid.TryParse(name, out id);
    }

    public void Dispose()
    {
        StopWatching();
    }
}
