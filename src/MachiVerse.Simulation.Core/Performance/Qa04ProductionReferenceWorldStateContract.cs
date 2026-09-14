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
    ulong EnvironmentCount,
    ulong SocietyGovernanceCount,
    ulong InfrastructureInformationCount,
    ulong TerrainRecordCount,
    ulong ActiveTransactionCount,
    ulong CanonicalInitialRecordCount);

/// <summary>
/// Gate-2 Step-13 contract for an assembled perf.reference.v1 State(S). The older reference-world
/// dependency/material contracts prove that production materializers exist; this contract proves
/// that their canonical populations are simultaneously represented by the WorldState partition
/// authority supplied to the full Step loop. It introduces no new identity, payload, or mapping
/// semantics and only checks counts already fixed by the benchmark and production materializers.
/// </summary>
public static class Qa04ProductionReferenceWorldStateContractV1
{
    public static Qa04ProductionReferenceWorldStateValidationV1 Validate(
        WorldStateV1 state,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(activeTransactions);

        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();
        Qa04TerrainCanonicalRecordSourceV1.ValidateCanonicalContract();
        Qa04CrossDomainTransactionGenesisMaterializerV1.ValidateCanonicalContract();

        if (state.Header.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.production-reference-world.world-id-drift");
        state.Partitions.ValidateStandardCompleteness();

        var residentCount = state.Partitions.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).Header.ItemCount;
        if (residentCount != Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount)
            throw new InvalidDataException("qa04.production-reference-world.resident-count-drift");

        var controlModeCount = state.Partitions.Get(ParticipationControlModePayloadV1.PartitionId).Header.ItemCount;
        if (controlModeCount != Qa04ParticipationControlModeCanonicalAuthorityV1.CanonicalCount)
            throw new InvalidDataException("qa04.production-reference-world.control-mode-count-drift");

        var physicalPresenceCount = state.Partitions.Get(PhysicalPresencePayloadV1.PartitionId).Header.ItemCount;
        if (physicalPresenceCount != Qa04PhysicalD0MaterializerV1.CanonicalPhysicalCount)
            throw new InvalidDataException("qa04.production-reference-world.physical-presence-count-drift");

        var environmentCount = OwnerItemCount(state, "environment");
        var expectedEnvironmentCount = checked(
            Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count +
            Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count);
        if (environmentCount != expectedEnvironmentCount)
            throw new InvalidDataException("qa04.production-reference-world.environment-count-drift");

        var societyGovernanceCount = checked(
            OwnerItemCount(state, "society_economy") +
            OwnerItemCount(state, "governance_security"));
        var expectedSocietyGovernanceCount = Qa04ReferenceLoadV1.RecordClasses
            .Single(static item => item.ClassToken.Value == "society-governance.active-record").Count;
        if (societyGovernanceCount != expectedSocietyGovernanceCount)
            throw new InvalidDataException("qa04.production-reference-world.society-governance-count-drift");

        var infrastructureInformationCount = OwnerItemCount(state, "infrastructure_information");
        var expectedInfrastructureInformationCount = Qa04ReferenceLoadV1.RecordClasses
            .Single(static item => item.ClassToken.Value == "infrastructure.active-record").Count;
        if (infrastructureInformationCount != expectedInfrastructureInformationCount)
            throw new InvalidDataException("qa04.production-reference-world.infrastructure-count-drift");

        var terrainRecordCount = state.Partitions.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId).Header.ItemCount;
        if (terrainRecordCount != Qa04TerrainCanonicalRecordSourceV1.CanonicalRecordCount)
            throw new InvalidDataException("qa04.production-reference-world.terrain-count-drift");

        if ((ulong)activeTransactions.Count != Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount ||
            activeTransactions.Select(static state => state.TransactionId).Distinct().Count() != activeTransactions.Count ||
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
            environmentCount,
            societyGovernanceCount,
            infrastructureInformationCount,
            terrainRecordCount,
            checked((ulong)activeTransactions.Count),
            canonicalInitialRecordCount);
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
