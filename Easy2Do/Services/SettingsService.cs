using System;
using System.IO;
using System.Text.Json;
using Easy2Do;

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
                var settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings);
                if (settings != null)
                {
                    ApplyDefaultUrlsIfMissing(settings);
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
            var json = JsonSerializer.Serialize(_settings, AppJsonContext.Default.AppSettings);
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
        var defaults = new AppSettings
        {
            StorageLocation = Path.Combine(documentsPath, "Easy2Do"),
            SyncEnabled = false,
            PowerSyncUrl = "https://sync.normnet.cc",
            PowerSyncDevToken = string.Empty,
            SyncBackendUrl = string.Empty,
            SupabaseUrl = "https://sitbwplgthilakgspfab.supabase.co",
            SupabaseApiKey = string.Empty
        };
        return defaults;
    }

    private void ApplyDefaultUrlsIfMissing(AppSettings settings)
    {
        var updated = false;
        if (string.IsNullOrWhiteSpace(settings.PowerSyncUrl))
        {
            settings.PowerSyncUrl = "https://sync.normnet.cc";
            updated = true;
        }
        if (string.IsNullOrWhiteSpace(settings.SupabaseUrl))
        {
            settings.SupabaseUrl = "https://sitbwplgthilakgspfab.supabase.co";
            updated = true;
        }
        if (updated) SaveSettings();
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

    public bool GetSyncEnabled() => _settings.SyncEnabled;
    public void SetSyncEnabled(bool enabled) { _settings.SyncEnabled = enabled; SaveSettings(); }

    public string GetPowerSyncUrl() => _settings.PowerSyncUrl;
    public void SetPowerSyncUrl(string url) { _settings.PowerSyncUrl = url?.Trim() ?? string.Empty; SaveSettings(); }

    public string GetPowerSyncDevToken() => _settings.PowerSyncDevToken;
    public void SetPowerSyncDevToken(string token) { _settings.PowerSyncDevToken = token?.Trim() ?? string.Empty; SaveSettings(); }

    public string GetSyncBackendUrl() => _settings.SyncBackendUrl;
    public void SetSyncBackendUrl(string url) { _settings.SyncBackendUrl = url?.Trim() ?? string.Empty; SaveSettings(); }

    public string GetSupabaseUrl() => _settings.SupabaseUrl;
    public void SetSupabaseUrl(string url) { _settings.SupabaseUrl = url?.Trim() ?? string.Empty; SaveSettings(); }

    public string GetSupabaseApiKey() => _settings.SupabaseApiKey;
    public void SetSupabaseApiKey(string key) { _settings.SupabaseApiKey = key?.Trim() ?? string.Empty; SaveSettings(); }
}

public class AppSettings
{
    public string StorageLocation { get; set; } = string.Empty;
    public bool SyncEnabled { get; set; }
    public string PowerSyncUrl { get; set; } = string.Empty;
    public string PowerSyncDevToken { get; set; } = string.Empty;
    public string SyncBackendUrl { get; set; } = string.Empty;
    public string SupabaseUrl { get; set; } = string.Empty;
    public string SupabaseApiKey { get; set; } = string.Empty;
}
