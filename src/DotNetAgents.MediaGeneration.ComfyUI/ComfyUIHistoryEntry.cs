using System.Text.Json;

namespace DotNetAgents.MediaGeneration.ComfyUI;

/// <summary>Parsed ComfyUI history entry for a prompt_id.</summary>
public sealed class ComfyUIHistoryEntry
{
    public ComfyUIExecutionStatus? Status { get; init; }
    public IReadOnlyList<ComfyUIOutput> Outputs { get; init; } = Array.Empty<ComfyUIOutput>();

    public static ComfyUIHistoryEntry Parse(JsonElement element)
    {
        ComfyUIExecutionStatus? status = null;
        var outputs = new List<ComfyUIOutput>();

        if (element.TryGetProperty("status", out var statusEl))
        {
            var executed = statusEl.TryGetProperty("status_str", out var s) && s.GetString() == "success";
            status = new ComfyUIExecutionStatus
            {
                Completed = executed,
                Executed = statusEl.TryGetProperty("executed", out var ex) ? ex.GetInt32() : 0,
                Total = statusEl.TryGetProperty("total", out var t) ? t.GetInt32() : 0,
                CurrentNode = statusEl.TryGetProperty("current_node", out var cn) ? cn.GetString() : null
            };
        }

        if (element.TryGetProperty("outputs", out var outEl))
        {
            foreach (var node in outEl.EnumerateObject())
            {
                if (node.Value.TryGetProperty("gifs", out var gifs))
                {
                    foreach (var g in gifs.EnumerateArray())
                        outputs.Add(new ComfyUIOutput { NodeId = node.Name, Type = "gif", Filename = g.GetProperty("filename").GetString(), Subfolder = g.TryGetProperty("subfolder", out var sf) ? sf.GetString() : null });
                }
                if (node.Value.TryGetProperty("videos", out var videos))
                {
                    foreach (var v in videos.EnumerateArray())
                        outputs.Add(new ComfyUIOutput { NodeId = node.Name, Type = "video", Filename = v.GetProperty("filename").GetString(), Subfolder = v.TryGetProperty("subfolder", out var sf) ? sf.GetString() : null });
                }
                if (node.Value.TryGetProperty("images", out var images))
                {
                    foreach (var img in images.EnumerateArray())
                        outputs.Add(new ComfyUIOutput { NodeId = node.Name, Type = "image", Filename = img.GetProperty("filename").GetString(), Subfolder = img.TryGetProperty("subfolder", out var sf) ? sf.GetString() : null });
                }
            }
        }

        return new ComfyUIHistoryEntry { Status = status, Outputs = outputs };
    }
}

public sealed class ComfyUIExecutionStatus
{
    public bool Completed { get; init; }
    public int Executed { get; init; }
    public int Total { get; init; }
    public string? CurrentNode { get; init; }
}

public sealed class ComfyUIOutput
{
    public string? NodeId { get; init; }
    public string? Type { get; init; }
    public string? Filename { get; init; }
    public string? Subfolder { get; init; }
}
