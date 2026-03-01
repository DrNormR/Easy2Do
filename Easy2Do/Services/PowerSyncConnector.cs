using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.DB.Crud;

namespace Easy2Do.Services;

public class PowerSyncConnector : IPowerSyncBackendConnector
{
    private static readonly HttpClient HttpClient = new();
    private readonly SettingsService _settingsService;

    public PowerSyncConnector(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<PowerSyncCredentials?> FetchCredentials()
    {
        var powerSyncUrl = _settingsService.GetPowerSyncUrl();
        if (string.IsNullOrWhiteSpace(powerSyncUrl))
            throw new InvalidOperationException("PowerSync URL is not configured.");

        var devToken = _settingsService.GetPowerSyncDevToken();
        if (!string.IsNullOrWhiteSpace(devToken))
            return new PowerSyncCredentials(powerSyncUrl, devToken);

        var backendUrl = _settingsService.GetSyncBackendUrl();
        if (string.IsNullOrWhiteSpace(backendUrl))
            throw new InvalidOperationException("PowerSync dev token or backend URL is required.");

        var token = await FetchTokenAsync(backendUrl);
        return new PowerSyncCredentials(powerSyncUrl, token);
    }

    public async Task UploadData(IPowerSyncDatabase database)
    {
        var transaction = await database.GetNextCrudTransaction();
        while (transaction != null)
        {
            await UploadTransactionAsync(transaction);
            await transaction.Complete();
            transaction = await database.GetNextCrudTransaction();
        }
    }

    private static async Task<string> FetchTokenAsync(string backendUrl)
    {
        var response = await HttpClient.GetAsync($"{backendUrl.TrimEnd('/')}/sync/token");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);
        if (string.IsNullOrWhiteSpace(tokenResponse?.Token))
            throw new InvalidOperationException("Backend did not return a PowerSync token.");
        return tokenResponse!.Token!;
    }

    private async Task UploadTransactionAsync(CrudTransaction transaction)
    {
        var backendUrl = _settingsService.GetSyncBackendUrl();
        if (!string.IsNullOrWhiteSpace(backendUrl))
        {
            var payload = new
            {
                transactionId = transaction.TransactionId,
                crud = transaction.Crud
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync($"{backendUrl.TrimEnd('/')}/sync/upload", content);
            response.EnsureSuccessStatusCode();
            return;
        }

        var supabaseUrl = _settingsService.GetSupabaseUrl();
        var supabaseKey = _settingsService.GetSupabaseApiKey();
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseKey))
            throw new InvalidOperationException("Upload requires either a backend URL or Supabase URL + API key.");

        foreach (var operation in transaction.Crud)
        {
            await ApplySupabaseOperationAsync(supabaseUrl, supabaseKey, operation);
        }
    }

    private static async Task ApplySupabaseOperationAsync(string supabaseUrl, string supabaseKey, CrudEntry operation)
    {
        var baseUrl = $"{supabaseUrl.TrimEnd('/')}/rest/v1/{operation.Table}";
        using var request = new HttpRequestMessage();
        request.Headers.TryAddWithoutValidation("apikey", supabaseKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {supabaseKey}");

        switch (operation.Op)
        {
            case UpdateType.PUT:
            {
                var data = ToDictionary(operation.OpData);
                data["id"] = operation.Id;
                var json = JsonSerializer.Serialize(data);
                request.Method = HttpMethod.Post;
                request.RequestUri = new Uri(baseUrl);
                request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                break;
            }
            case UpdateType.PATCH:
            {
                var json = JsonSerializer.Serialize(operation.OpData);
                request.Method = HttpMethod.Patch;
                request.RequestUri = new Uri($"{baseUrl}?id=eq.{operation.Id}");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                break;
            }
            case UpdateType.DELETE:
            {
                request.Method = HttpMethod.Delete;
                request.RequestUri = new Uri($"{baseUrl}?id=eq.{operation.Id}");
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported operation: {operation.Op}");
        }

        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static Dictionary<string, object?> ToDictionary(object? opData)
    {
        if (opData == null)
            return new Dictionary<string, object?>();

        if (opData is IDictionary<string, object?> dict)
            return new Dictionary<string, object?>(dict);

        if (opData is IReadOnlyDictionary<string, object?> roDict)
            return new Dictionary<string, object?>(roDict);

        if (opData is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText());
            return parsed ?? new Dictionary<string, object?>();
        }

        var json = JsonSerializer.Serialize(opData);
        var result = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        return result ?? new Dictionary<string, object?>();
    }

    private sealed record TokenResponse(string Token);
}
