namespace DotNetAgents.Storage.ArtifactStore;

/// <summary>
/// Single source of truth for retention-class behavior: which classes auto-delete, which are
/// protected, which never auto-clean, and what the maximum allowed retention TTL is for each.
/// Story b9df690a (R4.7).
///
/// <para>Consumers (see <c>docs/sdlc-governance/RETENTION-CLASS-TABLE.md</c>):</para>
/// <list type="bullet">
///   <item><b>R4.4 zip composer</b> validates the sidecar's <c>RetentionClass</c> via
///   <see cref="IsValid"/> at compose time.</item>
///   <item><b>R4.5 TTL cleanup worker</b> reads <see cref="IsAutoDelete"/> /
///   <see cref="IsProtected"/> / <see cref="IsNeverDelete"/> to decide whether to delete,
///   escalate via review decision record, or no-op.</item>
///   <item><b>R4.6 <c>extend_artifact_retention</c> MCP tool</b> consults
///   <see cref="MaxRetentionHours"/> for the per-class upper bound.</item>
/// </list>
///
/// <para>This class is purely declarative: O(1) static dispatch on a known string, no I/O.</para>
/// </summary>
public static class RetentionClassPolicy
{
    /// <summary>Maximum allowed retention window for the <see cref="RetentionClass.ReleasePackage"/>
    /// class (7 days, per the spec §11 D4 default cap).</summary>
    public const int MaxReleasePackageHours = 168;

    /// <summary>The single hard-coded TTL we apply to <see cref="RetentionClass.DeletionReceipt"/>:
    /// receipts auto-delete themselves after 30 days (720 hours) so the audit trail doesn't
    /// outgrow the retention windows for the classes it documents.</summary>
    public const int DeletionReceiptTtlHours = 30 * 24;

    /// <summary>True when <paramref name="retentionClass"/> appears in <see cref="RetentionClass.All"/>.</summary>
    public static bool IsValid(string? retentionClass)
    {
        if (string.IsNullOrEmpty(retentionClass))
        {
            return false;
        }
        return retentionClass switch
        {
            RetentionClass.ReleasePackage => true,
            RetentionClass.DeploymentBundle => true,
            RetentionClass.TestSummary => true,
            RetentionClass.RollbackTarget => true,
            RetentionClass.LineageMetadata => true,
            RetentionClass.PromotionReceipt => true,
            RetentionClass.DeletionReceipt => true,
            RetentionClass.QuarantineRecord => true,
            _ => false,
        };
    }

    /// <summary>True for classes whose artifacts auto-delete after their
    /// <c>retainUntilUtc</c> moment (the R4.5 worker is allowed to delete without operator
    /// confirmation).</summary>
    public static bool IsAutoDelete(string? retentionClass) => retentionClass switch
    {
        RetentionClass.DeploymentBundle => true,
        RetentionClass.TestSummary => true,
        RetentionClass.DeletionReceipt => true,
        _ => false,
    };

    /// <summary>True for classes whose artifacts the R4.5 worker MUST NOT auto-delete even when
    /// expired. Operator escalation (review decision record) is required for actual deletion.</summary>
    public static bool IsProtected(string? retentionClass) => retentionClass switch
    {
        RetentionClass.ReleasePackage => true,
        RetentionClass.RollbackTarget => true,
        _ => false,
    };

    /// <summary>True for classes whose artifacts are never auto-deleted under any circumstances
    /// (the cleanup worker treats them as a no-op).</summary>
    public static bool IsNeverDelete(string? retentionClass) => retentionClass switch
    {
        RetentionClass.LineageMetadata => true,
        RetentionClass.PromotionReceipt => true,
        RetentionClass.QuarantineRecord => true,
        _ => false,
    };

    /// <summary>Maximum allowed retention window in hours for the class. <c>null</c> means
    /// unlimited (no upper bound — typical for the never-delete classes).
    /// <see cref="MaxReleasePackageHours"/> is the spec D4 cap (7 days).</summary>
    public static int? MaxRetentionHours(string? retentionClass) => retentionClass switch
    {
        RetentionClass.ReleasePackage => MaxReleasePackageHours,
        RetentionClass.RollbackTarget => MaxReleasePackageHours,
        RetentionClass.DeploymentBundle => MaxReleasePackageHours,
        RetentionClass.TestSummary => MaxReleasePackageHours,
        RetentionClass.DeletionReceipt => DeletionReceiptTtlHours,
        // null = unlimited
        RetentionClass.LineageMetadata => null,
        RetentionClass.PromotionReceipt => null,
        RetentionClass.QuarantineRecord => null,
        _ => null,
    };
}
