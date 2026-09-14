using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionReferenceWorldStateValidationV1(
    ulong ResidentCount,
    ulong ParticipationControlModeCount,
    ulong PhysicalPresenceCount,
    ulong EnvironmentD0Count,
    ulong EnvironmentD1Count,
    ulong EnvironmentCount,
    ulong SocietyGovernanceCount,
    ulong InfrastructureInformationCount,
    ulong TerrainRecordCount,
    ulong ActiveTransactionCount,
    ulong CanonicalInitialRecordCount);

/// <summary>
/// Gate-2 Step-13 contract for an assembled perf.reference.v1 State(S). The 97-partition WorldState
/// carries the canonical base/D0 partition authority; Environment D1 is a detail authority over the
/// same thirteen Environment partition identities and is therefore supplied separately rather than
/// being fabricated as a mixed-detail aggregate header. The contract checks only already-decided
/// benchmark/materializer cardinalities and exact standard partition ownership.
/// </summary>
public static class Qa04ProductionReferenceWorldStateContractV1
{
    public static Qa04ProductionReferenceWorldStateValidationV1 Validate(
        WorldStateV1 state,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions,
        IReadOnlyCollection<PartitionStateHeaderV1> environmentD1Headers)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(activeTransactions);
        ArgumentNullException.ThrowIfNull(environmentD1Headers);

        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04TerrainCanonicalRecordSourceV1.ValidateCanonicalContract();
        Qa04CrossDomainTransactionGenesisMaterializerV1.ValidateCanonicalContract();
        Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04DetailRegionCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04FacilityServiceCanonicalAuthorityV1.ValidateCanonicalContract();

        if (state.Header.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.production-reference-world.world-id-drift");
        state.Partitions.ValidateStandardCompleteness();

        var residentCount = state.Partitions.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).Header.ItemCount;
        if (residentCount != Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount ||
            OwnerItemCount(state, "resident") != residentCount)
            throw new InvalidDataException("qa04.production-reference-world.resident-count-drift");

        var controlModeCount = state.Partitions.Get(ParticipationControlModePayloadV1.PartitionId).Header.ItemCount;
        if (controlModeCount != Qa04ParticipationControlModeCanonicalAuthorityV1.CanonicalCount ||
            OwnerItemCount(state, "participation") != controlModeCount)
            throw new InvalidDataException("qa04.production-reference-world.control-mode-count-drift");

        var physicalPresenceCount = state.Partitions.Get(PhysicalPresencePayloadV1.PartitionId).Header.ItemCount;
        var physicalOccupancyCount = state.Partitions.Get(PhysicalOccupancyRecordSchemaV2.PartitionId).Header.ItemCount;
        var builtStructureCount = state.Partitions.Get(BuiltStructurePayloadV1.PartitionId).Header.ItemCount;
        var expectedPhysicalOwnerCount = checked(
            Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalPhysicalCount +
            Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalPhysicalCount * 2UL +
            Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount);
        if (physicalPresenceCount != Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalPhysicalCount ||
            physicalOccupancyCount != checked(Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalPhysicalCount * 2UL) ||
            builtStructureCount != Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount ||
            OwnerItemCount(state, "physical_built") != expectedPhysicalOwnerCount)
            throw new InvalidDataException("qa04.production-reference-world.physical-count-drift");

        ValidateSpatialSupport(state);

        var environmentD0Count = ValidateEnvironmentBase(state);
        var environmentD1Count = ValidateEnvironmentD1(environmentD1Headers);
        var environmentCount = checked(environmentD0Count + environmentD1Count);
        if (environmentD0Count != Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count ||
            environmentD1Count != Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count)
            throw new InvalidDataException("qa04.production-reference-world.environment-count-drift");

        ValidateSocietyGovernancePartitionCounts(state);
        var societyGovernanceCount = checked(
            OwnerItemCount(state, "society_economy") +
            OwnerItemCount(state, "governance_security"));
        var expectedSocietyGovernanceCount = Qa04SocietyGovernanceReferenceDecompositionV1.CanonicalCount;
        if (societyGovernanceCount != expectedSocietyGovernanceCount)
            throw new InvalidDataException("qa04.production-reference-world.society-governance-count-drift");

        ValidateInfrastructurePartitionCounts(state);
        var infrastructureInformationCount = OwnerItemCount(state, "infrastructure_information");
        if (infrastructureInformationCount != Qa04InfrastructureReferenceDecompositionV1.CanonicalCount)
            throw new InvalidDataException("qa04.production-reference-world.infrastructure-count-drift");

        var terrainRecordCount = state.Partitions.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId).Header.ItemCount;
        if (terrainRecordCount != Qa04TerrainCanonicalRecordSourceV1.CanonicalRecordCount)
            throw new InvalidDataException("qa04.production-reference-world.terrain-count-drift");

        if ((ulong)activeTransactions.Count != Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount ||
            activeTransactions.Select(static transaction => transaction.TransactionId).Distinct().Count() != activeTransactions.Count ||
            activeTransactions.Any(transaction =>
                !transaction.IsActive ||
                transaction.CreatedStep != 0 ||
                transaction.UpdatedStep > state.Header.Step))
        {
            throw new InvalidDataException("qa04.production-reference-world.transaction-count-or-state-drift");
        }

        var canonicalInitialRecordCount = checked(
            residentCount +
            controlModeCount +
            physicalPresenceCount +
            environmentCount +
            societyGovernanceCount +
            infrastructureInformationCount +
            Qa04TerrainCanonicalRecordSourceV1.CanonicalHotBrickCount +
            (ulong)activeTransactions.Count);
        if (canonicalInitialRecordCount != Qa04ReferenceLoadV1.CanonicalInitialRecordCount)
            throw new InvalidDataException("qa04.production-reference-world.initial-record-total-drift");

        return new Qa04ProductionReferenceWorldStateValidationV1(
            residentCount,
            controlModeCount,
            physicalPresenceCount,
            environmentD0Count,
            environmentD1Count,
            environmentCount,
            societyGovernanceCount,
            infrastructureInformationCount,
            terrainRecordCount,
            checked((ulong)activeTransactions.Count),
            canonicalInitialRecordCount);
    }

    private static void ValidateSpatialSupport(WorldStateV1 state)
    {
        var worldFrame = state.Partitions.Get(SpatialWorldFramePayloadV1.PartitionId).Header.ItemCount;
        var scopes = state.Partitions.Get(SpatialScopeRegistryPayloadV1.PartitionId).Header.ItemCount;
        var terrain = state.Partitions.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId).Header.ItemCount;
        var detailRegions = state.Partitions.Get(SpatialDetailRegionsPayloadV1.PartitionId).Header.ItemCount;
        var expectedOwnerCount = checked(
            (ulong)Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalTileFrameCount +
            (ulong)Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount +
            Qa04TerrainCanonicalRecordSourceV1.CanonicalRecordCount +
            (ulong)Qa04DetailRegionCanonicalAuthorityV1.CanonicalRegionCount);
        if (worldFrame != (ulong)Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalTileFrameCount ||
            scopes != (ulong)Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount ||
            terrain != Qa04TerrainCanonicalRecordSourceV1.CanonicalRecordCount ||
            detailRegions != (ulong)Qa04DetailRegionCanonicalAuthorityV1.CanonicalRegionCount ||
            OwnerItemCount(state, "spatial") != expectedOwnerCount)
            throw new InvalidDataException("qa04.production-reference-world.spatial-support-count-drift");
    }

    private static ulong ValidateEnvironmentBase(WorldStateV1 state)
    {
        ulong total = 0;
        foreach (var decomposition in Qa04EnvironmentReferenceDecompositionV1.Partitions)
        {
            var header = state.Partitions.Get(decomposition.PartitionId.Value).Header;
            if (header.OwnerDomain.Value != "environment" ||
                header.ItemCount != decomposition.D0Count ||
                header.DetailLevel != DetailLevelV1.D0Entity)
                throw new InvalidDataException($"qa04.production-reference-world.environment-d0-partition:{decomposition.PartitionId.Value}");
            total = checked(total + header.ItemCount);
        }
        if (OwnerItemCount(state, "environment") != total)
            throw new InvalidDataException("qa04.production-reference-world.environment-d0-owner-total");
        return total;
    }

    private static ulong ValidateEnvironmentD1(IReadOnlyCollection<PartitionStateHeaderV1> headers)
    {
        if (headers.Count != Qa04EnvironmentReferenceDecompositionV1.Partitions.Count)
            throw new InvalidDataException("qa04.production-reference-world.environment-d1-header-count");

        var byId = headers.ToDictionary(static header => header.PartitionId.Value, StringComparer.Ordinal);
        if (byId.Count != headers.Count)
            throw new InvalidDataException("qa04.production-reference-world.environment-d1-header-duplicate");

        ulong total = 0;
        foreach (var decomposition in Qa04EnvironmentReferenceDecompositionV1.Partitions)
        {
            if (!byId.TryGetValue(decomposition.PartitionId.Value, out var header) ||
                header.OwnerDomain.Value != "environment" ||
                header.ItemCount != decomposition.D1Count ||
                header.Revision != 1 ||
                header.BasisStep != 0 ||
                header.DetailLevel != DetailLevelV1.D1LocalAggregate ||
                header.CanonicalDigest.Length != 32 ||
                header.CanonicalDigest.All(static value => value == 0))
                throw new InvalidDataException($"qa04.production-reference-world.environment-d1-partition:{decomposition.PartitionId.Value}");
            total = checked(total + header.ItemCount);
        }
        return total;
    }

    private static void ValidateSocietyGovernancePartitionCounts(WorldStateV1 state)
    {
        foreach (var slice in Qa04SocietyGovernanceReferenceDecompositionV1.Partitions)
        {
            var header = state.Partitions.Get(slice.PartitionId.Value).Header;
            if (header.ItemCount != slice.Count ||
                header.OwnerDomain.Value is not ("society_economy" or "governance_security"))
                throw new InvalidDataException($"qa04.production-reference-world.society-governance-partition:{slice.PartitionId.Value}");
        }
    }

    private static void ValidateInfrastructurePartitionCounts(WorldStateV1 state)
    {
        foreach (var group in Qa04InfrastructureReferenceDecompositionV1.Slices.GroupBy(static slice => slice.PartitionId.Value, StringComparer.Ordinal))
        {
            var expected = group.Aggregate(0UL, static (sum, slice) => checked(sum + slice.Count));
            var header = state.Partitions.Get(group.Key).Header;
            if (header.ItemCount != expected || header.OwnerDomain.Value != "infrastructure_information")
                throw new InvalidDataException($"qa04.production-reference-world.infrastructure-partition:{group.Key}");
        }
    }

    private static ulong OwnerItemCount(WorldStateV1 state, string ownerDomain)
    {
        ulong total = 0;
        foreach (var partition in state.Partitions.CanonicalEntries)
        {
            if (!string.Equals(partition.Header.OwnerDomain.Value, ownerDomain, StringComparison.Ordinal))
                continue;
            total = checked(total + partition.Header.ItemCount);
        }
        return total;
    }
}
