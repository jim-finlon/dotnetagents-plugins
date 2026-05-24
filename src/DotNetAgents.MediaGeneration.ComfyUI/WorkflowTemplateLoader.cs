using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.MediaGeneration.ComfyUI;

/// <summary>Loads workflow JSON templates and injects parameters (prompt, dimensions, seed, etc.).</summary>
public sealed class WorkflowTemplateLoader
{
    private readonly string _templatesPath;
    private readonly ILogger<WorkflowTemplateLoader>? _logger;

    public WorkflowTemplateLoader(string? templatesPath = null, ILogger<WorkflowTemplateLoader>? logger = null)
    {
        _templatesPath = templatesPath ?? Path.Combine(AppContext.BaseDirectory, "Templates");
        _logger = logger;
    }

    /// <summary>Load template by name (e.g. ltx2-t2v, ltx2-i2v, sdxl-default) and apply variables.</summary>
    public async Task<Dictionary<string, object>> LoadAsync(string templateName, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_templatesPath, templateName + ".json");
        if (!File.Exists(path))
        {
            _logger?.LogWarning("Template not found: {Path}", path);
            return await Task.FromResult(CreateFallbackWorkflow(templateName, variables)).ConfigureAwait(false);
        }
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        foreach (var (key, value) in variables)
            json = json.Replace("{{" + key + "}}", value ?? "", StringComparison.OrdinalIgnoreCase);
        var doc = JsonDocument.Parse(json);
        return JsonToPrompt(doc.RootElement);
    }

    /// <summary>Recursively convert JsonElement values to object for ComfyUI prompt format.</summary>
    public static Dictionary<string, object> JsonToPrompt(JsonElement element)
    {
        var d = new Dictionary<string, object>();
        foreach (var p in element.EnumerateObject())
            d[p.Name] = ConvertJsonValue(p.Value);
        return d;
    }

    private static object ConvertJsonValue(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Object => JsonToPrompt(el),
            JsonValueKind.Array => el.EnumerateArray().Select(ConvertJsonValue).ToArray(),
            JsonValueKind.String => el.GetString()!,
            JsonValueKind.Number => el.TryGetInt32(out var i) ? i : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => el.GetRawText()
        };
    }

    private static Dictionary<string, object> CreateFallbackWorkflow(string templateName, IReadOnlyDictionary<string, string> variables)
    {
        var prompt = variables.TryGetValue("prompt", out var p) ? p : "a beautiful scene";
        var width = variables.TryGetValue("width", out var w) && int.TryParse(w, out var wi) ? wi : 854;
        var height = variables.TryGetValue("height", out var h) && int.TryParse(h, out var hi) ? hi : 480;
        var seed = variables.TryGetValue("seed", out var s) && int.TryParse(s, out var si) ? si : 0;
        var nodeId = "1";
        var dict = new Dictionary<string, object>
        {
            [nodeId] = new Dictionary<string, object>
            {
                ["class_type"] = templateName.StartsWith("ltx2", StringComparison.OrdinalIgnoreCase) ? "LTX2VideoLoader" : "KSampler",
                ["inputs"] = new Dictionary<string, object>
                {
                    ["prompt"] = prompt,
                    ["width"] = width,
                    ["height"] = height,
                    ["seed"] = seed
                }
            }
        };
        return dict;
    }
}
