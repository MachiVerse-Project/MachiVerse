using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionCrossDomainTurnoverAuthorityV1(
    IReadOnlyList<Qa04ActiveTransactionSlotV1> ActiveSlots,
    IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> CanonicalRecordPools);

/// <summary>
/// Re-materializes only the canonical record-id pools needed by the QA-04 transaction turnover
/// authority and proves that its genesis transaction set is identical to the already assembled
/// production State(1) transaction authority before the mutable slot lifecycle is introduced.
/// </summary>
public static class Qa04ProductionCrossDomainTurnoverAuthorityBuilderV1
{
    public static Qa04ProductionCrossDomainTurnoverAuthorityV1 CreateCanonical(
        IReadOnlyCollection<CrossDomainTransactionStateV1> assembledActiveTransactions)
    {
        ArgumentNullException.ThrowIfNull(assembledActiveTransactions);
        Qa04CrossDomainTransactionTurnoverMaterializerV1.ValidateCanonicalContract();

        var scopes = Qa04SpatialTileScopeAuthorityV1.MaterializeCanonical();
        var environment = Qa04EnvironmentCanonicalD0FullEvidenceBuilderV1.Build();
        var physical = Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.MaterializeCanonical();
        var participation = Qa04ParticipationControlModeCanonicalAuthorityV1.MaterializeCanonical();
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeCanonicalResidentIdentityLifecycle();
        var society = Qa04SocietyGovernanceCanonicalMaterializerV1.MaterializeCanonical();
        var facility = Qa04FacilityServiceCanonicalAuthorityV1.MaterializeCanonical();

        var pools = new Dictionary<string, IReadOnlyList<OpaqueId128>>(StringComparer.Ordinal)
        {
            [SpatialScopeRegistryPayloadV1.PartitionId] = RecordIds(scopes),
            [EnvironmentHazardPayloadV1.PartitionId] = RecordIds(environment.Materialization.State.Hazard),
            [PhysicalPresencePayloadV1.PartitionId] = RecordIds(physical.Presences),
            [ParticipationControlModePayloadV1.PartitionId] = RecordIds(participation.Partition),
            [ResidentIdentityLifecyclePayloadV1.PartitionId] = RecordIds(resident.Partition),
            [SocietyContractClaimPayloadV1.PartitionId] = RecordIds(society.ContractClaims),
            [GovernancePermissionLicensePayloadV1.PartitionId] = RecordIds(society.PermissionLicenses),
            [InfrastructureServiceQueuePayloadV1.PartitionId] = RecordIds(facility.ServiceAuthority.ServiceQueue),
        };

        var genesis = Qa04CrossDomainTransactionGenesisMaterializerV1.Materialize(pools);
        var slots = Qa04CrossDomainTransactionTurnoverMaterializerV1.Initialize(genesis);
        RequireGenesisMatchesAssembly(assembledActiveTransactions, slots);

        return new Qa04ProductionCrossDomainTurnoverAuthorityV1(
            slots,
            new Dictionary<string, IReadOnlyList<OpaqueId128>>(pools, StringComparer.Ordinal));
    }

    private static void RequireGenesisMatchesAssembly(
        IReadOnlyCollection<CrossDomainTransactionStateV1> assembled,
        IReadOnlyCollection<Qa04ActiveTransactionSlotV1> slots)
    {
        if (assembled.Count != slots.Count)
            throw new InvalidDataException("qa04.production-turnover.genesis-count-drift");

        var expected = assembled.OrderBy(static state => state.TransactionId).ToArray();
        var actual = slots.Select(static slot => slot.State).OrderBy(static state => state.TransactionId).ToArray();
        for (var index = 0; index < expected.Length; index++)
        {
            if (expected[index].TransactionId != actual[index].TransactionId ||
                !CryptographicOperations.FixedTimeEquals(
                    expected[index].CanonicalDigest(),
                    actual[index].CanonicalDigest()))
                throw new InvalidDataException("qa04.production-turnover.genesis-authority-drift");
        }
    }

    private static IReadOnlyList<OpaqueId128> RecordIds<TPayload>(DomainPartitionStateV1<TPayload> partition)
        => Array.AsReadOnly(partition.RecordsCanonical.Select(static record => record.RecordId).ToArray());
}
