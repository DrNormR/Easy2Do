using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Easy2Do.Services;

/// <summary>
/// Creates timestamped backup copies of note files and prunes old ones
/// using a tiered retention policy:
///   - Keep the most recent N backups (current editing cluster)
///   - Keep 1 backup per hour for anything older than 1 hour
///   - Delete anything older than MaxAge
/// </summary>
public class BackupService
{
    private const string BackupsFolder = "backups";
    private const string TimestampFormat = "yyyyMMdd-HHmmss";
    private const int RecentToKeep = 3;
    private static readonly TimeSpan HourlyThreshold = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(48);

    private readonly SettingsService _settingsService;

    public BackupService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    private string GetBackupsDirectory()
    {
        var storageLocation = _settingsService.GetStorageLocation();
        return Path.Combine(storageLocation, BackupsFolder);
    }

    private void EnsureBackupsDirectory()
    {
        var dir = GetBackupsDirectory();
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Creates a backup of the given note file and prunes old backups.
    /// </summary>
    public async Task BackupNoteAsync(Guid noteId, string noteJson)
    {
        try
        {
            EnsureBackupsDirectory();

            var timestamp = DateTime.Now.ToString(TimestampFormat);
            var fileName = $"{noteId}.{timestamp}.json";
            var path = Path.Combine(GetBackupsDirectory(), fileName);

            await File.WriteAllTextAsync(path, noteJson);

            PruneBackups(noteId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Backup error for {noteId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes all backups for a given note (used when a note is deleted).
    /// </summary>
    public void DeleteBackupsForNote(Guid noteId)
    {
        try
        {
            var dir = GetBackupsDirectory();
            if (!Directory.Exists(dir)) return;

            var pattern = $"{noteId}.*.json";
            foreach (var file in Directory.GetFiles(dir, pattern))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting backups for {noteId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies the tiered retention policy for a specific note's backups.
    /// </summary>
    private void PruneBackups(Guid noteId)
    {
        try
        {
            var dir = GetBackupsDirectory();
            if (!Directory.Exists(dir)) return;

            var pattern = $"{noteId}.*.json";
            var backups = Directory.GetFiles(dir, pattern)
                .Select(f => new BackupFile(f, ParseTimestamp(f)))
                .Where(b => b.Timestamp.HasValue)
                .OrderByDescending(b => b.Timestamp!.Value)
                .ToList();

            if (backups.Count <= RecentToKeep)
                return;

            var now = DateTime.Now;
            var toKeep = new HashSet<string>();

            // Tier 1: Keep the most recent N backups
            foreach (var b in backups.Take(RecentToKeep))
                toKeep.Add(b.FilePath);

            // Tier 2: For older backups, keep 1 per hour
            var olderBackups = backups.Skip(RecentToKeep).ToList();
            var keptHours = new HashSet<string>();

            foreach (var b in olderBackups)
            {
                var age = now - b.Timestamp!.Value;

                // Tier 3: Delete anything older than MaxAge
                if (age > MaxAge)
                    continue;

                // Keep 1 per hour block
                var hourKey = b.Timestamp.Value.ToString("yyyyMMdd-HH");
                if (keptHours.Add(hourKey))
                    toKeep.Add(b.FilePath);
            }

            // Delete everything not in the keep set
            foreach (var b in backups)
            {
                if (!toKeep.Contains(b.FilePath))
                {
                    try { File.Delete(b.FilePath); }
                    catch { /* best effort */ }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Prune error for {noteId}: {ex.Message}");
        }
    }

    private static DateTime? ParseTimestamp(string filePath)
    {
        // Filename format: {guid}.{yyyyMMdd-HHmmss}.json
        var name = Path.GetFileNameWithoutExtension(filePath);
        var lastDot = name.LastIndexOf('.');
        if (lastDot < 0) return null;

        var timestampStr = name[(lastDot + 1)..];
        if (DateTime.TryParseExact(timestampStr, TimestampFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt;
        }
        return null;
    }

    private record BackupFile(string FilePath, DateTime? Timestamp);
}
