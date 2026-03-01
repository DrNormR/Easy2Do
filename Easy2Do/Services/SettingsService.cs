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
                    ApplyDefaultUrlsIfMissing(settings);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading settings: {ex.Message}");
        }

        var defaults = GetDefaultSettings();
        ApplyDefaultUrlsIfMissing(defaults);
        return defaults;
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
            StorageLocation = Path.Combine(documentsPath, "Easy2Do"),
            DeviceId = Guid.NewGuid().ToString("N"),
            SyncEnabled = false,
            PowerSyncUrl = "https://sync.normnet.cc",
            PowerSyncDevToken = string.Empty,
            SyncBackendUrl = string.Empty,
            SupabaseUrl = "https://sitbwplgthilakgspfab.supabase.co",
            SupabaseApiKey = string.Empty
        };
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

        if (updated)
            SaveSettings();
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

    public string GetDeviceId()
    {
        if (string.IsNullOrWhiteSpace(_settings.DeviceId))
        {
            _settings.DeviceId = Guid.NewGuid().ToString("N");
            SaveSettings();
        }
        return _settings.DeviceId;
    }

    public string GetDeviceName()
    {
        return Environment.MachineName;
    }

    public bool GetSyncEnabled()
    {
        return _settings.SyncEnabled;
    }

    public void SetSyncEnabled(bool enabled)
    {
        _settings.SyncEnabled = enabled;
        SaveSettings();
    }

    public string GetPowerSyncUrl()
    {
        return _settings.PowerSyncUrl;
    }

    public void SetPowerSyncUrl(string url)
    {
        _settings.PowerSyncUrl = url?.Trim() ?? string.Empty;
        SaveSettings();
    }

    public string GetPowerSyncDevToken()
    {
        return _settings.PowerSyncDevToken;
    }

    public void SetPowerSyncDevToken(string token)
    {
        _settings.PowerSyncDevToken = token?.Trim() ?? string.Empty;
        SaveSettings();
    }

    public string GetSyncBackendUrl()
    {
        return _settings.SyncBackendUrl;
    }

    public void SetSyncBackendUrl(string url)
    {
        _settings.SyncBackendUrl = url?.Trim() ?? string.Empty;
        SaveSettings();
    }

    public string GetSupabaseUrl()
    {
        return _settings.SupabaseUrl;
    }

    public void SetSupabaseUrl(string url)
    {
        _settings.SupabaseUrl = url?.Trim() ?? string.Empty;
        SaveSettings();
    }

    public string GetSupabaseApiKey()
    {
        return _settings.SupabaseApiKey;
    }

    public void SetSupabaseApiKey(string key)
    {
        _settings.SupabaseApiKey = key?.Trim() ?? string.Empty;
        SaveSettings();
    }
}

public class AppSettings
{
    public string StorageLocation { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public bool SyncEnabled { get; set; }
    public string PowerSyncUrl { get; set; } = string.Empty;
    public string PowerSyncDevToken { get; set; } = string.Empty;
    public string SyncBackendUrl { get; set; } = string.Empty;
    public string SupabaseUrl { get; set; } = string.Empty;
    public string SupabaseApiKey { get; set; } = string.Empty;
}
