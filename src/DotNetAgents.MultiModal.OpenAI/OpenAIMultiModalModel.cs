using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetAgents.Abstractions.Exceptions;
using DotNetAgents.Abstractions.Models;
using DotNetAgents.MultiModal;
using DotNetAgents.MultiModal.ContentParts;
using DotNetAgents.MultiModal.Models;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.MultiModal.OpenAI;

/// <summary>
/// OpenAI implementation of <see cref="IMultiModalModel"/>: vision (GPT-4V), Whisper, TTS.
/// </summary>
public sealed class OpenAIMultiModalModel : IMultiModalModel
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelName;
    private readonly string _visionModel;
    private readonly ILogger<OpenAIMultiModalModel>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public OpenAIMultiModalModel(
        HttpClient httpClient,
        string apiKey,
        string modelName = "gpt-4o",
        string? visionModel = null,
        ILogger<OpenAIMultiModalModel>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey)) : apiKey;
        _modelName = string.IsNullOrWhiteSpace(modelName) ? throw new ArgumentException("Model name cannot be null or empty.", nameof(modelName)) : modelName;
        _visionModel = visionModel ?? modelName;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        if (_httpClient.BaseAddress == null)
            _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        if (!_httpClient.DefaultRequestHeaders.Contains("Authorization"))
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    /// <inheritdoc />
    public string ModelName => _modelName;

    /// <inheritdoc />
    public int MaxTokens => 4096;

    /// <inheritdoc />
    public ModelCapabilities GetCapabilities() =>
        ModelCapabilities.Text | ModelCapabilities.Vision | ModelCapabilities.Transcription | ModelCapabilities.SpeechSynthesis;

    /// <inheritdoc />
    public async Task<MultiModalOutput> GenerateAsync(
        IMultiModalInput input,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var messages = BuildMessages(input);
        var request = new OpenAIChatRequest
        {
            Model = _visionModel,
            Messages = messages,
            Temperature = options?.Temperature,
            MaxTokens = options?.MaxTokens ?? 1024,
            TopP = options?.TopP,
            FrequencyPenalty = options?.FrequencyPenalty,
            PresencePenalty = options?.PresencePenalty
        };
        var response = await _httpClient.PostAsJsonAsync("chat/completions", request, _jsonOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OpenAIChatResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false);
        var content = body?.Choices?[0].Message?.Content;
        if (content == null)
            throw new AgentException("OpenAI response contained no content.", ErrorCategory.LLMError);
        return new MultiModalOutput { Text = content is string s ? s : content.ToString() ?? string.Empty };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<MultiModalOutput> GenerateStreamAsync(
        IMultiModalInput input,
        LLMOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var messages = BuildMessages(input);
        var request = new OpenAIChatRequest
        {
            Model = _visionModel,
            Messages = messages,
            Stream = true,
            Temperature = options?.Temperature,
            MaxTokens = options?.MaxTokens ?? 1024
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = JsonContent.Create(request, options: _jsonOptions) };
        var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var json = line.Substring(6);
            if (string.Equals(json, "[DONE]", StringComparison.Ordinal)) break;
            var delta = ParseStreamDelta(json);
            if (!string.IsNullOrEmpty(delta))
                yield return new MultiModalOutput { Text = delta };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MultiModalOutput>> GenerateBatchAsync(
        IEnumerable<IMultiModalInput> inputs,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = inputs?.ToList() ?? throw new ArgumentNullException(nameof(inputs));
        if (list.Count == 0) return Array.Empty<MultiModalOutput>();
        var tasks = list.Select(i => GenerateAsync(i, options, cancellationToken));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> DescribeImageAsync(ImageInput imageInput, string? prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageInput);
        string imageUrl;
        if (imageInput.ImageDataBase64 != null && imageInput.ImageDataBase64.Length > 0)
        {
            var mime = imageInput.MimeType ?? "image/jpeg";
            imageUrl = $"data:{mime};base64,{Convert.ToBase64String(imageInput.ImageDataBase64)}";
        }
        else if (imageInput.ImageUrl != null)
        {
            imageUrl = imageInput.ImageUrl.ToString();
        }
        else
            throw new ArgumentException("ImageInput must have ImageDataBase64 or ImageUrl.", nameof(imageInput));

        var content = new object[]
        {
            new { type = "text", text = prompt ?? "Describe this image in detail." },
            new { type = "image_url", image_url = new { url = imageUrl } }
        };
        var request = new OpenAIChatRequest
        {
            Model = _visionModel,
            Messages = new List<OpenAIChatMessage> { new OpenAIChatMessage { Role = "user", Content = content } },
            MaxTokens = 1024
        };
        var response = await _httpClient.PostAsJsonAsync("chat/completions", request, _jsonOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OpenAIChatResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false);
        var msgContent = body?.Choices?[0].Message?.Content;
        return msgContent is string t ? t : (msgContent?.ToString() ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<string> TranscribeAsync(AudioInput audioInput, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioInput);
        var form = new MultipartFormDataContent();
        if (audioInput.FilePath != null)
        {
            var fileBytes = await File.ReadAllBytesAsync(audioInput.FilePath, cancellationToken).ConfigureAwait(false);
            var fileName = Path.GetFileName(audioInput.FilePath);
            form.Add(new ByteArrayContent(fileBytes), "file", fileName);
        }
        else if (audioInput.Data != null && audioInput.Data.Length > 0)
            form.Add(new ByteArrayContent(audioInput.Data), "file", "audio.mp3");
        else if (audioInput.Stream != null)
        {
            var ms = new MemoryStream();
            await audioInput.Stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            ms.Position = 0;
            form.Add(new StreamContent(ms), "file", "audio.mp3");
        }
        else
            throw new ArgumentException("AudioInput must provide FilePath, Data, or Stream.", nameof(audioInput));
        form.Add(new StringContent("whisper-1"), "model");
        if (!string.IsNullOrEmpty(audioInput.Language))
            form.Add(new StringContent(audioInput.Language), "language");
        var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions") { Content = form };
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        return json.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty;
    }

    /// <inheritdoc />
    public async Task<byte[]> SynthesizeSpeechAsync(string text, VoiceOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var request = new
        {
            model = "tts-1",
            input = text,
            voice = options?.VoiceId ?? "alloy",
            speed = options?.Speed ?? 1.0
        };
        var response = await _httpClient.PostAsJsonAsync("audio/speech", request, _jsonOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<VideoAnalysis> AnalyzeVideoAsync(VideoInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        // Phase 1: stub; Phase 2 could use frame extraction + vision.
        return Task.FromResult(new VideoAnalysis { Summary = "(Video analysis not implemented for this provider.)" });
    }

    private List<OpenAIChatMessage> BuildMessages(IMultiModalInput input)
    {
        var parts = input.Parts;
        if (parts == null || parts.Count == 0)
            throw new ArgumentException("Multi-modal input must have at least one part.", nameof(input));
        var content = new List<object>();
        foreach (var part in parts)
        {
            switch (part.Type)
            {
                case ContentType.Text:
                    content.Add(new { type = "text", text = part.Content.ToString() ?? "" });
                    break;
                case ContentType.Image:
                    if (part is ImageContentPart img)
                    {
                        if (img.Content is byte[] bytes)
                            content.Add(new { type = "image_url", image_url = new { url = $"data:{img.MimeType ?? "image/jpeg"};base64,{Convert.ToBase64String(bytes)}" } });
                        else if (img.Content is Uri uri)
                            content.Add(new { type = "image_url", image_url = new { url = uri.ToString() } });
                    }
                    break;
                default:
                    _logger?.LogWarning("Unsupported content type {Type} in multi-modal message; skipping.", part.Type);
                    break;
            }
        }
        return new List<OpenAIChatMessage> { new OpenAIChatMessage { Role = "user", Content = content } };
    }

    private string? ParseStreamDelta(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var delta = choices[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var content))
                    return content.GetString();
            }
        }
        catch (JsonException) { /* ignore */ }
        return null;
    }

    private sealed record OpenAIChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;
        [JsonPropertyName("messages")]
        public List<OpenAIChatMessage> Messages { get; init; } = new();
        [JsonPropertyName("temperature")]
        public double? Temperature { get; init; }
        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; init; }
        [JsonPropertyName("top_p")]
        public double? TopP { get; init; }
        [JsonPropertyName("frequency_penalty")]
        public double? FrequencyPenalty { get; init; }
        [JsonPropertyName("presence_penalty")]
        public double? PresencePenalty { get; init; }
        [JsonPropertyName("stream")]
        public bool Stream { get; init; }
    }

    private sealed record OpenAIChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; init; } = string.Empty;
        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Content { get; init; }
    }

    private sealed record OpenAIChatResponse
    {
        [JsonPropertyName("choices")]
        public OpenAIChatChoice[]? Choices { get; init; }
    }

    private sealed record OpenAIChatChoice
    {
        [JsonPropertyName("message")]
        public OpenAIChatMessage? Message { get; init; }
    }
}
