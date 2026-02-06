using System;
using System.IO;
using System.Text.Json;

namespace Easy2Do.Services;

public class SettingsService
{
    private const string SettingsFileName = "settings.json";
    private AppSettings _settings;
    private readonly string _settingsFilePath;

    public SettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataPath, "Easy2Do");
        
        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
        }

        _settingsFilePath = Path.Combine(appFolder, SettingsFileName);
        _settings = LoadSettings();
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading settings: {ex.Message}");
        }

        return GetDefaultSettings();
    }

    private void SaveSettings()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    private AppSettings GetDefaultSettings()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new AppSettings
        {
            StorageLocation = Path.Combine(documentsPath, "Easy2Do")
        };
    }

    public string GetStorageLocation()
    {
        return _settings.StorageLocation;
    }

    public void SetStorageLocation(string location)
    {
        if (!string.IsNullOrWhiteSpace(location))
        {
            _settings.StorageLocation = location;
            SaveSettings();
        }
    }
}

public class AppSettings
{
    public string StorageLocation { get; set; } = string.Empty;
}
