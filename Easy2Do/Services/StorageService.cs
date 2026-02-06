using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Easy2Do.Models;

namespace Easy2Do.Services;

public class StorageService
{
    private readonly SettingsService _settingsService;
    private const string DefaultFileName = "notes.json";

    public StorageService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    private string GetNotesFilePath()
    {
        var storageLocation = _settingsService.GetStorageLocation();
        return Path.Combine(storageLocation, DefaultFileName);
    }

    public async Task<List<Note>> LoadNotesAsync()
    {
        try
        {
            var filePath = GetNotesFilePath();
            
            if (!File.Exists(filePath))
            {
                return new List<Note>();
            }

            var json = await File.ReadAllTextAsync(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

            var notes = JsonSerializer.Deserialize<List<Note>>(json, options);
            return notes ?? new List<Note>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading notes: {ex.Message}");
            return new List<Note>();
        }
    }

    public async Task SaveNotesAsync(IEnumerable<Note> notes)
    {
        try
        {
            var filePath = GetNotesFilePath();
            var directory = Path.GetDirectoryName(filePath);
            
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(notes, options);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving notes: {ex.Message}");
        }
    }
}
