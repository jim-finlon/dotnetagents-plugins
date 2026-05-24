using System.Text;
using System.Text.Json;
using DotNetAgents.MediaGeneration.Adapters;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.MediaGeneration.ComfyUI;

/// <summary>HTTP client for ComfyUI API: queue prompt, history, view output, upload image.</summary>
public sealed class ComfyUIClient
{
    private readonly HttpClient _http;
    private readonly ComfyUIOptions _options;
    private readonly ILogger<ComfyUIClient>? _logger;

    public ComfyUIClient(HttpClient http, ComfyUIOptions options, ILogger<ComfyUIClient>? logger = null)
    {
        _http = http;
        _options = options;
        _logger = logger;
        if (!string.IsNullOrEmpty(_options.BaseUrl))
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    /// <summary>Submit a workflow (prompt) to the queue. Returns prompt_id.</summary>
    public async Task<string> QueuePromptAsync(Dictionary<string, object> prompt, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["prompt"] = prompt,
            ["client_id"] = _options.ClientId ?? Guid.NewGuid().ToString("N")
        };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("prompt", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var doc = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        var promptId = doc.GetProperty("prompt_id").GetString() ?? throw new InvalidOperationException("ComfyUI did not return prompt_id.");
        _logger?.LogDebug("ComfyUI queued prompt_id={PromptId}", promptId);
        return promptId;
    }

    /// <summary>Get history for a prompt_id. Returns node outputs when complete.</summary>
    public async Task<ComfyUIHistoryEntry?> GetHistoryAsync(string promptId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"history/{promptId}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var doc = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (doc.ValueKind != JsonValueKind.Object || !doc.TryGetProperty(promptId, out var entry))
            return null;
        return ComfyUIHistoryEntry.Parse(entry);
    }

    /// <summary>Poll until execution completes or timeout. Returns final history entry.</summary>
    public async Task<ComfyUIHistoryEntry?> WaitForCompletionAsync(string promptId, IProgress<GenerationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + _options.QueueTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await GetHistoryAsync(promptId, cancellationToken).ConfigureAwait(false);
            if (entry != null)
            {
                progress?.Report(new GenerationProgress
                {
                    JobId = promptId,
                    CurrentStep = entry.Status?.Executed ?? 0,
                    TotalSteps = entry.Status?.Total ?? 1,
                    CurrentNode = entry.Status?.CurrentNode
                });
                if (entry.Status?.Completed == true)
                    return entry;
            }
            await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
        }
        _logger?.LogWarning("ComfyUI prompt_id={PromptId} did not complete within timeout", promptId);
        return null;
    }

    /// <summary>Download output file (image or video) as bytes.</summary>
    public async Task<byte[]?> DownloadOutputAsync(string filename, string? subfolder = null, string type = "output", CancellationToken cancellationToken = default)
    {
        var q = new List<string> { $"filename={Uri.EscapeDataString(filename)}", $"type={Uri.EscapeDataString(type)}" };
        if (!string.IsNullOrEmpty(subfolder))
            q.Add($"subfolder={Uri.EscapeDataString(subfolder)}");
        var url = "view?" + string.Join("&", q);
        var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Upload image for use in workflow (e.g. I2V). Returns filename as stored by ComfyUI.</summary>
    public async Task<string?> UploadImageAsync(string localPath, string? subfolder = null, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(localPath);
        var fileName = Path.GetFileName(localPath);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StreamContent(stream), "image", fileName);
        if (!string.IsNullOrEmpty(subfolder))
            multipart.Add(new StringContent(subfolder), "subfolder");
        var response = await _http.PostAsync("upload/image", multipart, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var doc = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        return doc.TryGetProperty("name", out var n) ? n.GetString() : null;
    }
}
