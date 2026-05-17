using DotNetAgents.Ui.Core.Ideation;

namespace DotNetAgents.Ui.Blazor.Components.Ideation;

public static class IdeationNodePalette
{
    public static string Resolve(IdeationNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Tags.Any(t => string.Equals(t, "agent-prompt", StringComparison.OrdinalIgnoreCase)))
            return "agent-prompt";
        if (node.Tags.Any(t => string.Equals(t, "group", StringComparison.OrdinalIgnoreCase)))
            return "group";
        if (node.Tags.Any(t => string.Equals(t, "freeform-shape", StringComparison.OrdinalIgnoreCase)))
            return "freeform-shape";

        return node.Kind switch
        {
            IdeationNodeKind.Requirement => "requirement-fragment",
            IdeationNodeKind.Reference => "link",
            IdeationNodeKind.Sketch or IdeationNodeKind.Custom => "freeform-shape",
            _ => "sticky-note",
        };
    }
}
