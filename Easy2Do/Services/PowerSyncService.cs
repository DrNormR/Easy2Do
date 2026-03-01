using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
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
                    return Task.FromResult(PowerSyncStartResult.Success("Sync already running; refreshing."));

                _started = true;
            }

            _ = SyncFromSupabaseAsync();
            return Task.FromResult(PowerSyncStartResult.Success($"Sync bootstrap ready for '{powerSyncUrl}' using '{dbPath}'."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(PowerSyncStartResult.Failed($"Sync start failed: {ex.Message}"));
        }
    }

    private async Task SyncFromSupabaseAsync()
    {
        var supabaseUrl = _settingsService.GetSupabaseUrl();
        var supabaseKey = _settingsService.GetSupabaseApiKey();
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseKey))
            return;

        try
        {
            var notes = await GetSupabaseAsync<List<NoteDto>>(supabaseUrl, supabaseKey, "notes?select=*");
            var items = await GetSupabaseAsync<List<ItemDto>>(supabaseUrl, supabaseKey, "note_items?select=*");
            var order = await GetSupabaseAsync<List<OrderDto>>(supabaseUrl, supabaseKey, "note_order?select=note_id,sort_order&order=sort_order.asc");

            var notesById = new Dictionary<Guid, Note>();
            foreach (var noteDto in notes)
            {
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
                orderedIds.Add(o.NoteId);
            }

            await _storageService.ReplaceAllNotesAsync(new List<Note>(notesById.Values), orderedIds);
            DataRefreshed?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] Supabase refresh failed: {ex.Message}");
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
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? Color { get; set; }
        public string? CreatedDate { get; set; }
        public string? ModifiedDate { get; set; }
        public double WindowX { get; set; }
        public double WindowY { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public bool IsPinned { get; set; }
    }

    private sealed class ItemDto
    {
        public Guid Id { get; set; }
        public Guid NoteId { get; set; }
        public string? Text { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsHeading { get; set; }
        public bool IsImportant { get; set; }
        public string? TextAttachment { get; set; }
        public string? DueDate { get; set; }
        public bool IsAlarmDismissed { get; set; }
        public string? SnoozeUntil { get; set; }
        public string? CreatedAtUtc { get; set; }
        public string? UpdatedAtUtc { get; set; }
        public string? DeletedAtUtc { get; set; }
    }

    private sealed class OrderDto
    {
        public Guid NoteId { get; set; }
        public int SortOrder { get; set; }
    }
}

public sealed record PowerSyncStartResult(bool IsSuccess, string Message)
{
    public static PowerSyncStartResult Success(string message) => new(true, message);
    public static PowerSyncStartResult Failed(string message) => new(false, message);
}
