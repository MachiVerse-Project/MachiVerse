using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionDomainSnapshotSectionProofV1(
    ulong BasisStep,
    int DomainSectionCount,
    int ChangedPartitionCount,
    ulong LogicalRecordCount);

/// <summary>
/// Gate3 Step 2 proof seam. Reuses the exact State(S) production material retained by the Gate2
/// assembler for unchanged partitions and replaces only the six typed mutation targets with their
/// actual post-mutation State(S+1) roots. It then composes the canonical exact-97 production Domain
/// sections. Terrain remains on the bounded-memory streaming authority path.
/// </summary>
public static class Qa04ProductionDomainSnapshotSectionProofRunnerV1
{
    public static Qa04ProductionDomainSnapshotSectionProofV1 Verify(
        AuthoritativeStepWorldStateV1 authoritative,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> basisAuthorities,
        Qa04CanonicalOperationMutationStateV1 mutationState)
    {
        ArgumentNullException.ThrowIfNull(authoritative);
        ArgumentNullException.ThrowIfNull(basisAuthorities);
        ArgumentNullException.ThrowIfNull(mutationState);
        if (!authoritative.IsPublishable)
            throw new InvalidDataException("qa04.gate3.domain-sections.authoritative-state-not-publishable");
        if (basisAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.gate3.domain-sections.basis-authority-count-not-97");

        var resultingState = authoritative.State;
        var changedPartitionCount = CountChangedPartitions(resultingState, basisAuthorities);
        if (changedPartitionCount != 6)
            throw new InvalidDataException("qa04.gate3.domain-sections.changed-partition-count-not-6");

        var authorities = Qa04ProductionDomainSnapshotAuthorityBuilderV1.CreateResultingState(
            resultingState,
            basisAuthorities,
            mutationState);
        if (authorities.CanonicalAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.gate3.domain-sections.authority-count-not-97");

        foreach (var authority in authorities.CanonicalAuthorities)
            authority.VerifyBoundAuthority();

        var sections = DomainPartitionSnapshotStreamingProductionProviderV1.CreateAll97WithTerrainV2(
            authorities,
            StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders());
        if (sections.Count != StandardDomainPartitionRegistry.StandardPartitionCount || sections.Count != 97)
            throw new InvalidDataException("qa04.gate3.domain-sections.count-not-97");

        ulong logicalRecordCount = 0;
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var identity = StandardDomainPartitionRegistry.Entries[i];
            var authority = authorities.Get(identity.PartitionId.Value);
            if (!string.Equals(section.SectionId, identity.PartitionId.Value, StringComparison.Ordinal) ||
                section.SectionSchema != identity.PartitionSchema ||
                section.LogicalItemCount != authority.ActualItemCount ||
                !CryptographicOperations.FixedTimeEquals(
                    section.LogicalContentDigest,
                    authority.Header.CanonicalDigest))
                throw new InvalidDataException($"qa04.gate3.domain-sections.material-mismatch:{identity.PartitionId.Value}");
            logicalRecordCount = checked(logicalRecordCount + section.LogicalItemCount);
        }

        return new Qa04ProductionDomainSnapshotSectionProofV1(
            resultingState.Header.Step,
            sections.Count,
            changedPartitionCount,
            logicalRecordCount);
    }

    private static int CountChangedPartitions(
        WorldStateV1 resultingState,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> basisAuthorities)
    {
        var basisById = basisAuthorities.ToDictionary(
            static authority => authority.PartitionId.Value,
            static authority => authority.Header,
            StringComparer.Ordinal);
        var changed = 0;
        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            if (!basisById.TryGetValue(identity.PartitionId.Value, out var basis))
                throw new InvalidDataException($"qa04.gate3.domain-sections.basis-authority-missing:{identity.PartitionId.Value}");
            var current = resultingState.Partitions.Get(identity.PartitionId.Value).Header;
            if (!SameHeader(basis, current)) changed++;
        }
        return changed;
    }

    private static bool SameHeader(PartitionStateHeaderV1 left, PartitionStateHeaderV1 right)
        => left.PartitionId == right.PartitionId &&
           left.OwnerDomain == right.OwnerDomain &&
           left.Schema == right.Schema &&
           left.Revision == right.Revision &&
           left.BasisStep == right.BasisStep &&
           left.DetailLevel == right.DetailLevel &&
           left.ItemCount == right.ItemCount &&
           CryptographicOperations.FixedTimeEquals(left.CanonicalDigest, right.CanonicalDigest);
}
