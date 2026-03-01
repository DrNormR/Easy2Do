using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Easy2Do.Models;

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
                return Task.FromResult(PowerSyncStartResult.Failed("Database path is unavailable."));

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
            return Task.FromResult(PowerSyncStartResult.Success($"Sync bootstrap ready for '{powerSyncUrl}' using '{dbPath}'."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(PowerSyncStartResult.Failed($"Sync start failed: {ex.Message}"));
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

            var notes = await GetSupabaseAsync<List<NoteDto>>(supabaseUrl, supabaseKey, "notes?select=*");
            var items = await GetSupabaseAsync<List<ItemDto>>(supabaseUrl, supabaseKey, "note_items?select=*&order=position.asc");
            var order = await GetSupabaseAsync<List<OrderDto>>(supabaseUrl, supabaseKey, "note_order?select=note_id,sort_order&order=sort_order.asc");
            Console.WriteLine($"[Sync] Pulled {notes.Count} notes, {items.Count} items, {order.Count} order rows.");

            var notesById = new Dictionary<Guid, Note>();
            foreach (var noteDto in notes)
            {
                if (noteDto.Id == Guid.Empty)
                    continue;
                var note = new Note
                {
                    Id = noteDto.Id,
                    Title = noteDto.Title ?? "New Note",
                    Color = noteDto.Color ?? "#FFFFE680",
                    CreatedDate = ParseDate(noteDto.CreatedDate) ?? DateTime.Now,
                    ModifiedDate = ParseDate(noteDto.ModifiedDate) ?? DateTime.Now,
                    WindowX = noteDto.WindowX,
                    WindowY = noteDto.WindowY,
                    WindowWidth = noteDto.WindowWidth,
                    WindowHeight = noteDto.WindowHeight,
                    IsPinned = noteDto.IsPinned
                };
                notesById[note.Id] = note;
            }

            foreach (var itemDto in items)
            {
                if (itemDto.Id == Guid.Empty || itemDto.NoteId == Guid.Empty)
                    continue;
                if (!notesById.TryGetValue(itemDto.NoteId, out var note))
                    continue;

                var item = new TodoItem
                {
                    Id = itemDto.Id,
                    Text = itemDto.Text ?? string.Empty,
                    IsCompleted = itemDto.IsCompleted,
                    IsHeading = itemDto.IsHeading,
                    IsImportant = itemDto.IsImportant,
                    TextAttachment = itemDto.TextAttachment ?? string.Empty,
                    DueDate = ParseDate(itemDto.DueDate),
                    IsAlarmDismissed = itemDto.IsAlarmDismissed,
                    SnoozeUntil = ParseDate(itemDto.SnoozeUntil),
                    CreatedAtUtc = ParseDate(itemDto.CreatedAtUtc) ?? DateTime.UtcNow,
                    UpdatedAtUtc = ParseDate(itemDto.UpdatedAtUtc) ?? DateTime.UtcNow,
                    DeletedAtUtc = ParseDate(itemDto.DeletedAtUtc)
                };
                note.Items.Add(item);
            }

            var orderedIds = new List<Guid>();
            foreach (var o in order)
            {
                if (o.NoteId == Guid.Empty)
                    continue;
                orderedIds.Add(o.NoteId);
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

    private static async Task<T> GetSupabaseAsync<T>(string supabaseUrl, string supabaseKey, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{supabaseUrl.TrimEnd('/')}/rest/v1/{path}");
        request.Headers.TryAddWithoutValidation("apikey", supabaseKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {supabaseKey}");
        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Supabase JSON parse failed.");
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, out var dt)) return dt;
        return null;
    }

    private sealed class NoteDto
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("color")]
        public string? Color { get; set; }
        [JsonPropertyName("created_date")]
        public string? CreatedDate { get; set; }
        [JsonPropertyName("modified_date")]
        public string? ModifiedDate { get; set; }
        [JsonPropertyName("window_x")]
        public double WindowX { get; set; }
        [JsonPropertyName("window_y")]
        public double WindowY { get; set; }
        [JsonPropertyName("window_width")]
        public double WindowWidth { get; set; }
        [JsonPropertyName("window_height")]
        public double WindowHeight { get; set; }
        [JsonPropertyName("is_pinned")]
        public bool IsPinned { get; set; }
    }

    private sealed class ItemDto
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }
        [JsonPropertyName("note_id")]
        public Guid NoteId { get; set; }
        [JsonPropertyName("text")]
        public string? Text { get; set; }
        [JsonPropertyName("is_completed")]
        public bool IsCompleted { get; set; }
        [JsonPropertyName("is_heading")]
        public bool IsHeading { get; set; }
        [JsonPropertyName("is_important")]
        public bool IsImportant { get; set; }
        [JsonPropertyName("text_attachment")]
        public string? TextAttachment { get; set; }
        [JsonPropertyName("due_date")]
        public string? DueDate { get; set; }
        [JsonPropertyName("is_alarm_dismissed")]
        public bool IsAlarmDismissed { get; set; }
        [JsonPropertyName("snooze_until")]
        public string? SnoozeUntil { get; set; }
        [JsonPropertyName("created_at_utc")]
        public string? CreatedAtUtc { get; set; }
        [JsonPropertyName("updated_at_utc")]
        public string? UpdatedAtUtc { get; set; }
        [JsonPropertyName("deleted_at_utc")]
        public string? DeletedAtUtc { get; set; }
    }

    private sealed class OrderDto
    {
        [JsonPropertyName("note_id")]
        public Guid NoteId { get; set; }
        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }
    }
}

public sealed record PowerSyncStartResult(bool IsSuccess, string Message)
{
    public static PowerSyncStartResult Success(string message) => new(true, message);
    public static PowerSyncStartResult Failed(string message) => new(false, message);
}
