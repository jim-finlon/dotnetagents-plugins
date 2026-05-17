using System.Text.RegularExpressions;

namespace DotNetAgents.CodeAction;

/// <summary>
/// Extracts code-action blocks from an LLM response. Recognizes three forms (in order of precedence):
/// <list type="bullet">
///   <item><description>XML-style: <c>&lt;code&gt;...&lt;/code&gt;</c> — the canonical DNA marker.</description></item>
///   <item><description>Fenced markdown: <c>```python\n...\n```</c> — common in chat completions.</description></item>
///   <item><description>Fenced markdown without language tag: <c>```\n...\n```</c> — fallback when the model omits the tag.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// Returns ALL blocks found in document order. The orchestrator decides whether to execute
/// only the first block, all blocks concatenated, or to refuse multi-block responses.
/// </remarks>
public static partial class CodeBlockExtractor
{
    [GeneratedRegex(@"<code(?:\s+[^>]*)?>(?<body>[\s\S]*?)</code>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex XmlCodePattern();

    [GeneratedRegex(@"```(?:[a-zA-Z0-9_-]+)?\n(?<body>[\s\S]*?)\n```", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex FencedCodePattern();

    /// <summary>
    /// Return every code block in <paramref name="response"/>, preferring XML markers when
    /// present (model was prompted for them) and falling back to fenced markdown otherwise.
    /// Empty / whitespace-only blocks are filtered out.
    /// </summary>
    public static IReadOnlyList<string> Extract(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return Array.Empty<string>();

        var xmlMatches = XmlCodePattern().Matches(response);
        if (xmlMatches.Count > 0)
        {
            return xmlMatches
                .Select(m => m.Groups["body"].Value)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim('\r', '\n'))
                .ToArray();
        }

        var fencedMatches = FencedCodePattern().Matches(response);
        if (fencedMatches.Count > 0)
        {
            return fencedMatches
                .Select(m => m.Groups["body"].Value)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim('\r', '\n'))
                .ToArray();
        }

        return Array.Empty<string>();
    }
}
