namespace DotNetAgents.Ui.Blazor.Components.Ideation;

public sealed record HandoffSubmissionReceipt(
    Guid EpicId,
    string GraphId,
    IReadOnlyList<HandoffSubmissionMapping> DraftMappings);

public sealed record HandoffSubmissionMapping(
    string DraftId,
    string Kind,
    Guid ResultingStoryId,
    string Title);
