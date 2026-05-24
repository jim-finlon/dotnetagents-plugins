using System.Diagnostics;
using System.Text.Json;
using DotNetAgents.MediaGeneration.Adapters;

namespace DotNetAgents.MediaGeneration.ComfyUI;

/// <summary>Image generation backend using ComfyUI (SDXL).</summary>
public sealed class ComfyUIImageBackend : IImageGenerationBackend
{
    private readonly ComfyUIClient _client;
    private readonly WorkflowTemplateLoader _loader;
    private readonly ComfyUIOptions _options;

    public ComfyUIImageBackend(ComfyUIClient client, WorkflowTemplateLoader loader, ComfyUIOptions options)
    {
        _client = client;
        _loader = loader;
        _options = options;
    }

    public async Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["prompt"] = JsonEscapeForTemplate(request.Prompt),
            ["negative_prompt"] = JsonEscapeForTemplate(request.NegativePrompt ?? ""),
            ["width"] = request.Width.ToString(),
            ["height"] = request.Height.ToString(),
            ["seed"] = request.Seed.ToString()
        };

        if (request.TemplateExtraVariables != null)
        {
            foreach (var kv in request.TemplateExtraVariables)
                variables[kv.Key] = JsonEscapeForTemplate(kv.Value);
        }

        var templateName = string.IsNullOrWhiteSpace(request.WorkflowTemplate) ? "sdxl-default" : request.WorkflowTemplate.Trim();
        var prompt = await _loader.LoadAsync(templateName, variables, cancellationToken).ConfigureAwait(false);
        var promptId = await _client.QueuePromptAsync(prompt, cancellationToken).ConfigureAwait(false);
        var entry = await _client.WaitForCompletionAsync(promptId, null, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        if (entry == null)
        {
            return new ImageGenerationResult
            {
                Success = false,
                ErrorMessage = "ComfyUI did not complete within timeout.",
                Width = request.Width,
                Height = request.Height,
                Seed = request.Seed,
                RenderTimeMs = sw.ElapsedMilliseconds
            };
        }

        var imgOut = entry.Outputs.FirstOrDefault(o => o.Type == "image");
        if (imgOut?.Filename == null)
        {
            return new ImageGenerationResult
            {
                Success = false,
                ErrorMessage = "No image output from ComfyUI.",
                Width = request.Width,
                Height = request.Height,
                Seed = request.Seed,
                RenderTimeMs = sw.ElapsedMilliseconds
            };
        }

        var bytes = await _client.DownloadOutputAsync(imgOut.Filename, imgOut.Subfolder, "output", cancellationToken).ConfigureAwait(false);
        var outDir = _options.OutputDirectory ?? Path.GetTempPath();
        Directory.CreateDirectory(outDir);
        var ext = Path.GetExtension(imgOut.Filename);
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        var outPath = Path.Combine(outDir, $"{promptId}{ext}");
        if (bytes != null && bytes.Length > 0)
            await File.WriteAllBytesAsync(outPath, bytes, cancellationToken).ConfigureAwait(false);

        return new ImageGenerationResult
        {
            Success = true,
            OutputPath = outPath,
            Width = request.Width,
            Height = request.Height,
            Seed = request.Seed,
            RenderTimeMs = sw.ElapsedMilliseconds
        };
    }

    public Task<GenerationProgress?> GetProgressAsync(string jobId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<GenerationProgress?>(null);
    }

    /// <summary>Escape a string for embedding inside a JSON string literal in workflow templates.</summary>
    private static string JsonEscapeForTemplate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var json = JsonSerializer.Serialize(s);
        return json.Length >= 2 ? json[1..^1] : json;
    }
}
