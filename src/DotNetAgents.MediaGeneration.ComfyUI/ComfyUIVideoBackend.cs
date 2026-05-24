using System.Diagnostics;
using DotNetAgents.MediaGeneration.Adapters;

namespace DotNetAgents.MediaGeneration.ComfyUI;

/// <summary>Video generation backend using ComfyUI (LTX-2 T2V and I2V).</summary>
public sealed class ComfyUIVideoBackend : IVideoGenerationBackend
{
    private readonly ComfyUIClient _client;
    private readonly WorkflowTemplateLoader _loader;
    private readonly ComfyUIOptions _options;

    public ComfyUIVideoBackend(ComfyUIClient client, WorkflowTemplateLoader loader, ComfyUIOptions options)
    {
        _client = client;
        _loader = loader;
        _options = options;
    }

    public async Task<VideoGenerationResult> GenerateAsync(VideoGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var numFrames = Math.Max(1, (int)(request.DurationSeconds * 24)); // assume 24fps for frame count
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["prompt"] = request.Prompt,
            ["negative_prompt"] = request.NegativePrompt ?? "",
            ["width"] = request.Width.ToString(),
            ["height"] = request.Height.ToString(),
            ["num_frames"] = numFrames.ToString(),
            ["seed"] = request.Seed.ToString()
        };

        string templateName;
        if (request.IsImageToVideo && !string.IsNullOrEmpty(request.InputImagePath))
        {
            templateName = "ltx2-i2v";
            var uploadName = await _client.UploadImageAsync(request.InputImagePath, null, cancellationToken).ConfigureAwait(false);
            variables["input_image"] = uploadName ?? request.InputImagePath;
        }
        else
        {
            templateName = "ltx2-t2v";
        }

        var prompt = await _loader.LoadAsync(templateName, variables, cancellationToken).ConfigureAwait(false);
        var promptId = await _client.QueuePromptAsync(prompt, cancellationToken).ConfigureAwait(false);
        var entry = await _client.WaitForCompletionAsync(promptId, null, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        if (entry == null)
        {
            return new VideoGenerationResult
            {
                Success = false,
                ErrorMessage = "ComfyUI did not complete within timeout.",
                Width = request.Width,
                Height = request.Height,
                Seed = request.Seed,
                DurationSeconds = request.DurationSeconds,
                RenderTimeMs = sw.ElapsedMilliseconds
            };
        }

        var videoOut = entry.Outputs.FirstOrDefault(o => o.Type == "video" || o.Type == "gif");
        if (videoOut?.Filename == null)
        {
            return new VideoGenerationResult
            {
                Success = false,
                ErrorMessage = "No video output from ComfyUI.",
                Width = request.Width,
                Height = request.Height,
                Seed = request.Seed,
                DurationSeconds = request.DurationSeconds,
                RenderTimeMs = sw.ElapsedMilliseconds
            };
        }

        var bytes = await _client.DownloadOutputAsync(videoOut.Filename, videoOut.Subfolder, "output", cancellationToken).ConfigureAwait(false);
        var outDir = _options.OutputDirectory ?? Path.GetTempPath();
        Directory.CreateDirectory(outDir);
        var ext = Path.GetExtension(videoOut.Filename);
        if (string.IsNullOrEmpty(ext)) ext = ".mp4";
        var outPath = Path.Combine(outDir, $"{promptId}{ext}");
        if (bytes != null && bytes.Length > 0)
            await File.WriteAllBytesAsync(outPath, bytes, cancellationToken).ConfigureAwait(false);

        return new VideoGenerationResult
        {
            Success = true,
            OutputPath = outPath,
            Width = request.Width,
            Height = request.Height,
            Seed = request.Seed,
            DurationSeconds = request.DurationSeconds,
            RenderTimeMs = sw.ElapsedMilliseconds
        };
    }

    public Task<GenerationProgress?> GetProgressAsync(string jobId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<GenerationProgress?>(null);
    }
}
