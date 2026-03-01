using System;
using System.Threading.Tasks;

namespace Easy2Do.Services;

/// <summary>
/// Lightweight sync bootstrap service.
/// Keeps app startup and settings flows working even when full PowerSync
/// connector pieces are not present in this project yet.
/// </summary>
public sealed class PowerSyncService
{
    private readonly SettingsService _settingsService;
    private readonly StorageService _storageService;
    private readonly object _stateLock = new();
    private bool _started;

    public PowerSyncService(SettingsService settingsService, StorageService storageService)
    {
        _settingsService = settingsService;
        _storageService = storageService;
    }

    public async Task StartAsync()
    {
        _ = await TryStartAsync();
    }

    public Task<PowerSyncStartResult> TryStartAsync()
    {
        try
        {
            if (!_settingsService.GetSyncEnabled())
                return Task.FromResult(PowerSyncStartResult.Failed("Sync is disabled."));

            var powerSyncUrl = _settingsService.GetPowerSyncUrl();
            if (string.IsNullOrWhiteSpace(powerSyncUrl))
                return Task.FromResult(PowerSyncStartResult.Failed("PowerSync URL is not configured."));

            var dbPath = _storageService.GetDatabasePath();
            if (string.IsNullOrWhiteSpace(dbPath))
                return Task.FromResult(PowerSyncStartResult.Failed("Database path is unavailable."));

            lock (_stateLock)
            {
                if (_started)
                    return Task.FromResult(PowerSyncStartResult.Success("Sync is already running."));

                _started = true;
            }

            // Placeholder implementation:
            // SQLite persistence + optional Supabase forwarding in StorageService remains active.
            return Task.FromResult(
                PowerSyncStartResult.Success(
                    $"Sync bootstrap ready for '{powerSyncUrl}' using '{dbPath}'."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(PowerSyncStartResult.Failed($"Sync start failed: {ex.Message}"));
        }
    }
}

public sealed record PowerSyncStartResult(bool IsSuccess, string Message)
{
    public static PowerSyncStartResult Success(string message) => new(true, message);
    public static PowerSyncStartResult Failed(string message) => new(false, message);
}
