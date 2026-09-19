using System.Security.Cryptography;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Streaming-section counterpart of SnapshotPhysicalManifestStagingValidationV1.RequireExpectedAuthority.
/// It validates only exact-103 logical metadata and frozen/running-cut authority, so no fragment
/// payload collection is required merely to bind a staged physical manifest to the frozen state.
/// </summary>
public static class SnapshotPhysicalManifestStreamingAuthorityValidationV1
{
    public static void RequireExpectedAuthority(
        PhysicalSnapshotManifestMaterialV1 manifest,
        IEnumerable<CanonicalSnapshotStreamingSectionV1> expectedSections,
        WorldStateV1? frozenState = null,
        RunningSnapshotCutV1? expectedCut = null,
        ReadOnlyMemory<byte> expectedSnapshotDigest = default,
        ReadOnlyMemory<byte> expectedPhysicalManifestDigest = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(expectedSections);

        if (!expectedSnapshotDigest.IsEmpty)
        {
            if (expectedSnapshotDigest.Length != 32)
                throw new ArgumentException("Expected SnapshotDigest must be 32 bytes.", nameof(expectedSnapshotDigest));
            if (!CryptographicOperations.FixedTimeEquals(expectedSnapshotDigest.Span, manifest.Logical.SnapshotDigest))
                throw new InvalidDataException("persistence.snapshot.manifest-logical-digest-authority-mismatch");
        }
        if (!expectedPhysicalManifestDigest.IsEmpty)
        {
            if (expectedPhysicalManifestDigest.Length != 32)
                throw new ArgumentException("Expected physical manifest digest must be 32 bytes.", nameof(expectedPhysicalManifestDigest));
            if (!CryptographicOperations.FixedTimeEquals(expectedPhysicalManifestDigest.Span, manifest.PhysicalManifestDigest))
                throw new InvalidDataException("persistence.snapshot.manifest-physical-digest-authority-mismatch");
        }

        var expectedLogicalSections = CanonicalSnapshotStreamingSectionValidationV1.ToLogicalSections(
            expectedSections,
            frozenState);
        if (manifest.Logical.Sections.Count != expectedLogicalSections.Count)
            throw new InvalidDataException("persistence.snapshot.manifest-section-count-mismatch");
        for (var i = 0; i < expectedLogicalSections.Count; i++)
            RequireLogicalSectionMatch(expectedLogicalSections[i], manifest.Logical.Sections[i]);

        var expectedDomains = StandardDomainPartitionRegistry.Entries
            .Select(static entry => entry.OwnerDomain.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (!manifest.Logical.RequiredDomains.SequenceEqual(expectedDomains, StringComparer.Ordinal))
            throw new InvalidDataException("persistence.snapshot.manifest-required-domain-set-mismatch");

        if (frozenState is not null)
            RequireFrozenStateMatch(manifest.Logical, frozenState);
        if (expectedCut is not null)
            RequireRunningCutMatch(manifest.Logical, expectedCut);
    }

    private static void RequireLogicalSectionMatch(LogicalSnapshotSection expected, LogicalSnapshotSection actual)
    {
        if (!string.Equals(expected.SectionId, actual.SectionId, StringComparison.Ordinal) ||
            !string.Equals(expected.SchemaId, actual.SchemaId, StringComparison.Ordinal) ||
            expected.SchemaMajor != actual.SchemaMajor ||
            expected.SchemaMinor != actual.SchemaMinor ||
            expected.LogicalItemCount != actual.LogicalItemCount ||
            expected.Required != actual.Required ||
            !CryptographicOperations.FixedTimeEquals(expected.LogicalContentDigest, actual.LogicalContentDigest))
            throw new InvalidDataException($"persistence.snapshot.manifest-section-authority-mismatch:{expected.SectionId}");
    }

    private static void RequireFrozenStateMatch(LogicalSnapshotManifest logical, WorldStateV1 frozenState)
    {
        if (logical.WorldId != frozenState.Header.WorldId ||
            logical.SnapshotStep != frozenState.Header.Step ||
            logical.SimulationConfigGeneration != frozenState.Header.ConfigGeneration ||
            logical.MasterGeneration != frozenState.Header.MasterGeneration ||
            !CryptographicOperations.FixedTimeEquals(logical.SimulationConfigDigest, frozenState.Diagnostic.ConfigDigest))
            throw new InvalidDataException("persistence.snapshot.manifest-frozen-state-mismatch");
    }

    private static void RequireRunningCutMatch(LogicalSnapshotManifest logical, RunningSnapshotCutV1 cut)
    {
        if (logical.WorldId != cut.FrozenState.Header.WorldId ||
            logical.SnapshotId != cut.SnapshotId ||
            logical.SnapshotStep != cut.SnapshotStep ||
            logical.HistoryAnchorSequence != cut.HistoryAnchor.Sequence ||
            !CryptographicOperations.FixedTimeEquals(logical.HistoryAnchorDigest, cut.HistoryAnchor.Digest) ||
            !CryptographicOperations.FixedTimeEquals(logical.StateContinuityToken, cut.StateContinuityToken))
            throw new InvalidDataException("persistence.snapshot.manifest-running-cut-mismatch");
    }
}
