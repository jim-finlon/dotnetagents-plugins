namespace DotNetAgents.Ui.Core;

/// <summary>
/// Canonical token names used by DNA UI components.
/// </summary>
public static class DesignTokens
{
    public static class Surface
    {
        public const string Page = "var(--dna-surface-page)";
        public const string Raised = "var(--dna-surface-raised)";
        public const string Card = "var(--dna-surface-card)";
    }

    public static class Text
    {
        public const string Primary = "var(--dna-text-primary)";
        public const string Secondary = "var(--dna-text-secondary)";
        public const string Muted = "var(--dna-text-muted)";
    }

    public static class Accent
    {
        public const string Primary = "var(--dna-accent-primary)";
        public const string Info = "var(--dna-accent-info)";
        public const string Success = "var(--dna-accent-success)";
        public const string Warning = "var(--dna-accent-warning)";
        public const string Danger = "var(--dna-accent-danger)";
    }
}

public enum UiDensity
{
    Compact = 0,
    Comfortable = 1,
    Spacious = 2
}

public enum UiIntent
{
    Neutral = 0,
    Primary = 1,
    Info = 2,
    Success = 3,
    Warning = 4,
    Danger = 5
}
