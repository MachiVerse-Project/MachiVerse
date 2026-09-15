using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionExact103SnapshotRecoveryProofV1(
    ulong SnapshotStep,
    int SectionCount,
    int CoreSectionCount,
    int DomainSectionCount,
    int ChunkCount,
    ulong FragmentCount,
    ulong DomainLogicalRecordCount,
    byte[] SnapshotDigest,
    byte[] PhysicalManifestDigest);

/// <summary>
/// Gate3 Step 4 production proof. Recovery starts only from the committed SQLite snapshot catalog
/// and the final manifest/chunk files created by Step 3. The original State(S+1), typed mutation
/// material and in-memory source sections are deliberately not accepted as inputs. This stage proves
/// exact-103 physical/logical recovery; schema-owner semantic rehash remains Gate3 Step 5.
/// </summary>
public static class Qa04ProductionExact103SnapshotRecoveryProofRunnerV1
{
    public static async Task<Qa04ProductionExact103SnapshotRecoveryProofV1> VerifyAsync(
        Qa04ProductionExact103SnapshotPersistenceProofV1 persisted,
        SqlitePersistenceStore store,
        WorldPersistencePaths world,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(world);

        var recovered = await CanonicalSnapshotDurableRecoveryV1.RecoverNewestAsync(
            store,
            world,
            CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (recovered.Catalog.SnapshotStep != persisted.SnapshotStep ||
            !CryptographicOperations.FixedTimeEquals(recovered.Catalog.SnapshotDigest, persisted.SnapshotDigest) ||
            !CryptographicOperations.FixedTimeEquals(recovered.Catalog.PhysicalManifestDigest, persisted.PhysicalManifestDigest) ||
            !CryptographicOperations.FixedTimeEquals(recovered.Manifest.Logical.SnapshotDigest, persisted.SnapshotDigest) ||
            !CryptographicOperations.FixedTimeEquals(recovered.Manifest.PhysicalManifestDigest, persisted.PhysicalManifestDigest))
            throw new InvalidDataException("qa04.gate3.recovery.persisted-identity-mismatch");

        if (recovered.Manifest.Logical.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.gate3.recovery.world-id-mismatch");
        if (!CryptographicOperations.FixedTimeEquals(
                recovered.Manifest.Logical.WorldSeed,
                Qa04ReferenceLoadV1.WorldSeed.ToBytes()))
            throw new InvalidDataException("qa04.gate3.recovery.world-seed-mismatch");

        var sections = recovered.Sections;
        if (sections.Count != SnapshotManifestValidation.StandardRequiredSectionCount || sections.Count != 103)
            throw new InvalidDataException("qa04.gate3.recovery.section-count-not-103");
        if (!sections.Select(static section => section.SectionId)
                .SequenceEqual(StandardSnapshotSectionSetV1.SectionIds, StringComparer.Ordinal))
            throw new InvalidDataException("qa04.gate3.recovery.section-set-mismatch");

        var coreSectionCount = sections.Count(static section => StandardSnapshotSectionSetV1.IsCoreSection(section.SectionId));
        var domainSectionCount = sections.Count(static section => StandardDomainPartitionRegistry.TryGet(section.SectionId, out _));
        if (coreSectionCount != 6 || domainSectionCount != StandardDomainPartitionRegistry.StandardPartitionCount || domainSectionCount != 97)
            throw new InvalidDataException("qa04.gate3.recovery.section-owner-count-mismatch");

        ulong domainLogicalRecordCount = 0;
        foreach (var section in sections)
        {
            if (StandardDomainPartitionRegistry.TryGet(section.SectionId, out _))
                domainLogicalRecordCount = checked(domainLogicalRecordCount + section.LogicalItemCount);
        }

        if (recovered.ChunkCount != persisted.ChunkCount || recovered.ChunkCount == 0 ||
            domainLogicalRecordCount != persisted.DomainLogicalRecordCount ||
            recovered.FragmentCount < checked((ulong)sections.Count))
            throw new InvalidDataException("qa04.gate3.recovery.material-count-mismatch");

        return new Qa04ProductionExact103SnapshotRecoveryProofV1(
            recovered.Catalog.SnapshotStep,
            sections.Count,
            coreSectionCount,
            domainSectionCount,
            recovered.ChunkCount,
            recovered.FragmentCount,
            domainLogicalRecordCount,
            recovered.Catalog.SnapshotDigest.ToArray(),
            recovered.Catalog.PhysicalManifestDigest.ToArray());
    }
}
