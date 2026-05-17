using System.Text.RegularExpressions;
using DotNetAgents.MultiModal;
using DotNetAgents.MultiModal.Models;

namespace DotNetAgents.ComputerUse.Vision;

/// <summary>Detects UI elements using an <see cref="IMultiModalModel"/> vision API. CU-3.3.</summary>
public sealed class VisionScreenElementDetector : DotNetAgents.ComputerUse.IVisionElementDetector
{
    private readonly IMultiModalModel _visionModel;

    /// <summary>Creates a detector that uses the given vision model for screen analysis.</summary>
    public VisionScreenElementDetector(IMultiModalModel visionModel)
    {
        _visionModel = visionModel ?? throw new ArgumentNullException(nameof(visionModel));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DotNetAgents.ComputerUse.ElementInfo>> DetectElementsAsync(
        DotNetAgents.ComputerUse.ScreenState screen,
        string? descriptionHint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (screen.ImageBytes.Length == 0)
            return [];

        var imageInput = new ImageInput
        {
            ImageDataBase64 = screen.ImageBytes, // API may expect base64; some clients accept raw bytes - check IMultiModalModel impl
            MimeType = screen.ContentType ?? "image/png"
        };
        int w = screen.Width;
        int h = screen.Height;
        string prompt = $"This image is a screenshot of size {w}x{h} pixels. "
            + "List every clickable or focusable UI element (buttons, links, inputs, icons). "
            + "For each element output exactly one line in this format: description | x | y | width | height "
            + "where x,y are the top-left pixel coordinates and width,height are in pixels. "
            + (string.IsNullOrEmpty(descriptionHint) ? "" : $"Focus on elements matching: {descriptionHint}. ");
        string response = await _visionModel.DescribeImageAsync(imageInput, prompt, cancellationToken).ConfigureAwait(false);
        return ParseResponse(response, w, h);
    }

    private static IReadOnlyList<DotNetAgents.ComputerUse.ElementInfo> ParseResponse(string response, int screenW, int screenH)
    {
        var list = new List<DotNetAgents.ComputerUse.ElementInfo>();
        // Match "description | x | y | width | height" or "description (x, y, w, h)" or similar
        var linePattern = new Regex(@"([^|]+)\s*\|\s*(\d+)\s*\|\s*(\d+)\s*\|\s*(\d+)\s*\|\s*(\d+)", RegexOptions.Compiled);
        var altPattern = new Regex(@"([^(\n]+)\((\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\)", RegexOptions.Compiled);
        foreach (var line in response.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var m = linePattern.Match(line.Trim());
            if (!m.Success)
                m = altPattern.Match(line.Trim());
            if (!m.Success)
                continue;
            string desc = m.Groups[1].Value.Trim();
            int x = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            int y = int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            int w = int.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
            int height = int.Parse(m.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (w <= 0 || height <= 0)
                continue;
            list.Add(new DotNetAgents.ComputerUse.ElementInfo
            {
                Id = Guid.NewGuid().ToString("N"),
                Description = desc,
                Bounds = new DotNetAgents.ComputerUse.ScreenRegion(x, y, w, height)
            });
        }
        return list;
    }
}
