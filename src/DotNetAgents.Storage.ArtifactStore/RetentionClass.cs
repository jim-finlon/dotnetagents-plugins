namespace DotNetAgents.Storage.ArtifactStore;

/// <summary>
/// Canonical catalog of retention-class identifiers used across the artifact store, sidecar
/// metadata, cleanup workers, and operator-facing MCP tools. Story b9df690a (R4.7).
///
/// <para>Constants here are the SAME strings that travel on the wire (zip sidecar JSON,
/// <c>ReleaseArtifactDescriptor.RetentionClass</c>, <c>IReleaseArtifactStore</c> argument
/// values). Other code SHOULD NOT define its own copies — consume <see cref="All"/> or the
/// individual constants instead so the catalog has a single source of truth.</para>
///
/// <para>Adding a new class is a deliberate addition: extend <see cref="All"/>, declare its
/// behavior in <see cref="RetentionClassPolicy"/>, and update
/// <c>docs/sdlc-governance/RETENTION-CLASS-TABLE.md</c> in the same PR so the policy doc and
/// code never drift.</para>
/// </summary>
public static class RetentionClass
{
    /// <summary>Built release manifests and their zipped artifact packages. Protected — auto-cleanup
    /// refuses; explicit operator action only.</summary>
    public const string ReleasePackage = "release_package";

    /// <summary>Per-deployment bundles (compose / helm / k3s-prime). Auto-deletes after
    /// <see cref="ReleaseArtifactDescriptor.RetainUntilUtc"/>; operator can extend.</summary>
    public const string DeploymentBundle = "deployment_bundle";

    /// <summary>Test-result summaries captured at build time. Auto-deletes after
    /// <see cref="ReleaseArtifactDescriptor.RetainUntilUtc"/>.</summary>
    public const string TestSummary = "test_summary";

    /// <summary>The artifact slated as a rollback target for an active release. Protected — same
    /// rules as <see cref="ReleasePackage"/> so a live rollback path is never auto-cleaned.</summary>
    public const string RollbackTarget = "rollback_target";

    /// <summary>Provenance and lineage metadata (signed manifests, dependency-graph snapshots).
    /// Never auto-deleted; the audit trail is forever.</summary>
    public const string LineageMetadata = "lineage_metadata";

    /// <summary>Promotion receipts emitted by the DeploymentAgent. Never auto-deleted.</summary>
    public const string PromotionReceipt = "promotion_receipt";

    /// <summary>Receipts written when an artifact is deleted. Auto-deletes itself after 30 days
    /// so receipts don't accumulate forever (the audit trail is durable for at least one calendar
    /// month, which exceeds the longest <see cref="ReleasePackage"/> retention window).</summary>
    public const string DeletionReceipt = "deletion_receipt";

    /// <summary>Quarantined artifacts pulled out of normal flow during event response. Never
    /// auto-deleted; operator review releases or destroys them explicitly.</summary>
    public const string QuarantineRecord = "quarantine_record";

    /// <summary>The full v1 catalog. Iterate this when validating wire values or building
    /// dispatch tables; do not hard-code subsets elsewhere.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        ReleasePackage,
        DeploymentBundle,
        TestSummary,
        RollbackTarget,
        LineageMetadata,
        PromotionReceipt,
        DeletionReceipt,
        QuarantineRecord,
    };
}
