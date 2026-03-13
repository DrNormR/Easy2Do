using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Easy2Do.Models;

namespace Easy2Do.Services;

/// <summary>
/// Lightweight sync bootstrap service.
/// Periodically pulls from Supabase and replaces local notes.
/// Uses JsonNode for all JSON parsing — fully AOT-safe for iOS.
/// </summary>
public sealed class PowerSyncService
{
    private readonly SettingsService _settingsService;
    private readonly StorageService _storageService;
    private readonly object _stateLock = new();
    private bool _started;
    private CancellationTokenSource? _refreshCts;
    private Task? _refreshLoopTask;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly HttpClient HttpClient = new();

    public event Action? DataRefreshed;

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
                return Task.FromResult(PowerSyncStartResult.Failed("Storage path is unavailable."));

            lock (_stateLock)
            {
                if (_started)
                {
                    _ = RefreshFromSupabaseAsync();
                    return Task.FromResult(PowerSyncStartResult.Success("Sync already running; refreshing."));
                }

                _started = true;
                _refreshCts = new CancellationTokenSource();
                _refreshLoopTask = Task.Run(() => RefreshLoopAsync(_refreshCts.Token));
            }

            _ = RefreshFromSupabaseAsync();
            return Task.FromResult(PowerSyncStartResult.Success($"Sync bootstrap ready for '{powerSyncUrl}'."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(PowerSyncStartResult.Failed($"Sync start failed: {ex.Message}"));
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            _refreshCts?.Cancel();
            _started = false;
        }
    }

    private async Task RefreshLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RefreshInterval, token);
                if (token.IsCancellationRequested) break;
                await RefreshFromSupabaseAsync();
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sync] Refresh loop error: {ex.Message}");
            }
        }
    }

    private async Task RefreshFromSupabaseAsync()
    {
        var supabaseUrl = _settingsService.GetSupabaseUrl();
        var supabaseKey = _settingsService.GetSupabaseApiKey();
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseKey))
            return;

        try
        {
            if (!await _refreshGate.WaitAsync(0))
                return;

            var notesJson  = await GetSupabaseRawAsync(supabaseUrl, supabaseKey, "notes?select=*");
            var itemsJson  = await GetSupabaseRawAsync(supabaseUrl, supabaseKey, "note_items?select=*&order=position.asc");
            var orderJson  = await GetSupabaseRawAsync(supabaseUrl, supabaseKey, "note_order?select=note_id,sort_order&order=sort_order.asc");

            var notesArray  = JsonNode.Parse(notesJson)  as JsonArray ?? new JsonArray();
            var itemsArray  = JsonNode.Parse(itemsJson)  as JsonArray ?? new JsonArray();
            var orderArray  = JsonNode.Parse(orderJson)  as JsonArray ?? new JsonArray();

            Console.WriteLine($"[Sync] Pulled {notesArray.Count} notes, {itemsArray.Count} items, {orderArray.Count} order rows.");

            var notesById = new Dictionary<Guid, Note>();
            foreach (var noteNode in notesArray)
            {
                if (noteNode is not JsonObject obj) continue;
                if (!TryGetGuid(obj, "id", out var id) || id == Guid.Empty) continue;

                var note = new Note
                {
                    Id            = id,
                    Title         = obj["title"]?.GetValue<string>() ?? "New Note",
                    Color         = obj["color"]?.GetValue<string>()  ?? "#FFFFE680",
                    CreatedDate   = ParseDate(obj["created_date"]?.GetValue<string>()) ?? DateTime.Now,
                    ModifiedDate  = ParseDate(obj["modified_date"]?.GetValue<string>()) ?? DateTime.Now,
                    WindowX       = obj["window_x"]?.GetValue<double>() ?? double.NaN,
                    WindowY       = obj["window_y"]?.GetValue<double>() ?? double.NaN,
                    WindowWidth   = obj["window_width"]?.GetValue<double>()  ?? double.NaN,
                    WindowHeight  = obj["window_height"]?.GetValue<double>() ?? double.NaN,
                    IsPinned      = obj["is_pinned"]?.GetValue<bool>()  ?? false
                };
                notesById[note.Id] = note;
            }

            foreach (var itemNode in itemsArray)
            {
                if (itemNode is not JsonObject obj) continue;
                if (!TryGetGuid(obj, "id",      out var itemId)   || itemId  == Guid.Empty) continue;
                if (!TryGetGuid(obj, "note_id", out var noteId)   || noteId  == Guid.Empty) continue;
                if (!notesById.TryGetValue(noteId, out var note)) continue;

                var item = new TodoItem
                {
                    Id               = itemId,
                    Text             = obj["text"]?.GetValue<string>()          ?? string.Empty,
                    IsCompleted      = obj["is_completed"]?.GetValue<bool>()    ?? false,
                    IsHeading        = obj["is_heading"]?.GetValue<bool>()      ?? false,
                    IsImportant      = obj["is_important"]?.GetValue<bool>()    ?? false,
                    TextAttachment   = obj["text_attachment"]?.GetValue<string>() ?? string.Empty,
                    DueDate          = ParseDate(obj["due_date"]?.GetValue<string>()),
                    IsAlarmDismissed = obj["is_alarm_dismissed"]?.GetValue<bool>() ?? false,
                    SnoozeUntil      = ParseDate(obj["snooze_until"]?.GetValue<string>()),
                    CreatedAtUtc     = ParseDate(obj["created_at_utc"]?.GetValue<string>()) ?? DateTime.UtcNow,
                    UpdatedAtUtc     = ParseDate(obj["updated_at_utc"]?.GetValue<string>()) ?? DateTime.UtcNow,
                    DeletedAtUtc     = ParseDate(obj["deleted_at_utc"]?.GetValue<string>())
                };
                note.Items.Add(item);
            }

            var orderedIds = new List<Guid>();
            foreach (var orderNode in orderArray)
            {
                if (orderNode is not JsonObject obj) continue;
                if (TryGetGuid(obj, "note_id", out var oid) && oid != Guid.Empty)
                    orderedIds.Add(oid);
            }

            if (notesById.Count == 0)
            {
                Console.WriteLine("[Sync] No remote notes found; skipping local replace.");
                return;
            }

            Console.WriteLine("[Sync] Replacing local notes with remote snapshot.");
            await _storageService.ReplaceAllNotesAsync(new List<Note>(notesById.Values), orderedIds);
            DataRefreshed?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sync] Supabase refresh failed: {ex.Message}");
        }
        finally
        {
            if (_refreshGate.CurrentCount == 0)
                _refreshGate.Release();
        }
    }

    private static async Task<string> GetSupabaseRawAsync(string supabaseUrl, string supabaseKey, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{supabaseUrl.TrimEnd('/')}/rest/v1/{path}");
        request.Headers.TryAddWithoutValidation("apikey", supabaseKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {supabaseKey}");
        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static bool TryGetGuid(JsonObject obj, string key, out Guid result)
    {
        result = Guid.Empty;
        var val = obj[key]?.GetValue<string>();
        return val != null && Guid.TryParse(val, out result);
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, out var dt)) return dt;
        return null;
    }
}

public sealed record PowerSyncStartResult(bool IsSuccess, string Message)
{
    public static PowerSyncStartResult Success(string message) => new(true, message);
    public static PowerSyncStartResult Failed(string message) => new(false, message);
}
