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

        return CreateFromPools(assembledActiveTransactions, pools);
    }

    internal static Qa04ProductionCrossDomainTurnoverAuthorityV1 CreateFromValidatedAssembly(
        Qa04ProductionReferenceWorldAssemblyV1 assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Qa04CrossDomainTransactionTurnoverMaterializerV1.ValidateCanonicalContract();
        return CreateFromPools(
            assembly.ActiveTransactions,
            assembly.CanonicalTransactionRecordPools);
    }

    private static Qa04ProductionCrossDomainTurnoverAuthorityV1 CreateFromPools(
        IReadOnlyCollection<CrossDomainTransactionStateV1> assembledActiveTransactions,
        IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> pools)
    {
        ArgumentNullException.ThrowIfNull(assembledActiveTransactions);
        ArgumentNullException.ThrowIfNull(pools);
        if (pools.Count != 8)
            throw new InvalidDataException("qa04.production-turnover.record-pool-count-drift");

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

/// <summary>
/// Fail-closed contract between the canonical turnover materializer and the Gate4 Step2 production
/// finalization path. Empty changes mean an ordinary Step and require an identical active set.
/// A turnover Step must replace exactly one 1,000-slot cohort while preserving 10,000 ACTIVE rows.
/// </summary>
public static class Qa04ProductionCrossDomainTurnoverContractV1
{
    internal static void ValidateProductionStep(
        ulong basisStep,
        ulong resultingStep,
        IReadOnlyCollection<CrossDomainTransactionStateV1> basisActive,
        IReadOnlyCollection<CrossDomainTransactionStateV1> resultingActive,
        IReadOnlyCollection<CrossDomainTransactionStateV1> stateChanges,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache)
    {
        ArgumentNullException.ThrowIfNull(basisActive);
        ArgumentNullException.ThrowIfNull(resultingActive);
        ArgumentNullException.ThrowIfNull(stateChanges);
        if (resultingStep != checked(basisStep + 1UL))
            throw new InvalidDataException("qa04.production-turnover.resulting-step-drift");

        if (digestCache is not null &&
            stateChanges.Count == 0 &&
            ReferenceEquals(basisActive, resultingActive) &&
            basisActive is System.Collections.ObjectModel.ReadOnlyCollection<CrossDomainTransactionStateV1>)
        {
            // The production run carries the same immutable active-transaction collection across
            // ordinary Steps. ActiveTransactionSetDigest performs the full count/lifecycle/order/
            // duplicate/canonical-digest validation on the first generation access and caches it
            // by exact collection identity. A turnover creates a new collection and falls back to
            // the full delta contract below.
            _ = digestCache.ActiveTransactionSetDigest(basisActive);
            return;
        }

        Validate(
            basisStep,
            resultingStep,
            basisActive,
            resultingActive,
            stateChanges);
    }

    public static void Validate(
        ulong basisStep,
        ulong resultingStep,
        IReadOnlyCollection<CrossDomainTransactionStateV1> basisActive,
        IReadOnlyCollection<CrossDomainTransactionStateV1> resultingActive,
        IReadOnlyCollection<CrossDomainTransactionStateV1> stateChanges)
    {
        ArgumentNullException.ThrowIfNull(basisActive);
        ArgumentNullException.ThrowIfNull(resultingActive);
        ArgumentNullException.ThrowIfNull(stateChanges);
        if (resultingStep != checked(basisStep + 1UL))
            throw new InvalidDataException("qa04.production-turnover.resulting-step-drift");

        var basis = CanonicalActive(basisActive, "basis");
        var resulting = CanonicalActive(resultingActive, "resulting");
        if (stateChanges.Count == 0)
        {
            RequireSameActiveAuthority(basis, resulting);
            return;
        }

        if (basisStep == 0 || basisStep % Qa04CrossDomainTransactionTurnoverMaterializerV1.TurnoverCadenceSteps != 0)
            throw new InvalidDataException("qa04.production-turnover.non-cadence-change");
        var changes = stateChanges.OrderBy(static state => state.TransactionId).ToArray();
        var expectedChangeCount = checked((int)(Qa04CrossDomainTransactionTurnoverMaterializerV1.CohortSize * 2UL));
        if (changes.Length != expectedChangeCount ||
            changes.Select(static state => state.TransactionId).Distinct().Count() != changes.Length ||
            changes.Any(state => state.UpdatedStep != resultingStep))
            throw new InvalidDataException("qa04.production-turnover.change-set-shape-drift");

        var committed = changes.Where(static state => state.Lifecycle == TransactionLifecycleV1.Committed).ToArray();
        var replacements = changes.Where(static state => state.Lifecycle == TransactionLifecycleV1.Active).ToArray();
        if (committed.Length != checked((int)Qa04CrossDomainTransactionTurnoverMaterializerV1.CohortSize) ||
            replacements.Length != checked((int)Qa04CrossDomainTransactionTurnoverMaterializerV1.CohortSize) ||
            changes.Any(static state => state.Lifecycle == TransactionLifecycleV1.Aborted))
            throw new InvalidDataException("qa04.production-turnover.lifecycle-cardinality-drift");
        if (committed.Any(state => state.TerminalStep != resultingStep) ||
            replacements.Any(state => state.TerminalStep is not null || state.CreatedStep != resultingStep))
            throw new InvalidDataException("qa04.production-turnover.lifecycle-step-drift");

        var basisById = basis.ToDictionary(static state => state.TransactionId);
        var resultingById = resulting.ToDictionary(static state => state.TransactionId);
        if (committed.Any(state => !basisById.ContainsKey(state.TransactionId)) ||
            replacements.Any(state => !resultingById.ContainsKey(state.TransactionId)))
            throw new InvalidDataException("qa04.production-turnover.change-set-membership-drift");

        var removed = basisById.Keys.Except(resultingById.Keys).Order().ToArray();
        var added = resultingById.Keys.Except(basisById.Keys).Order().ToArray();
        if (removed.Length != committed.Length || added.Length != replacements.Length ||
            !removed.SequenceEqual(committed.Select(static state => state.TransactionId).Order()) ||
            !added.SequenceEqual(replacements.Select(static state => state.TransactionId).Order()))
            throw new InvalidDataException("qa04.production-turnover.active-set-delta-drift");

        foreach (var id in basisById.Keys.Intersect(resultingById.Keys))
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    basisById[id].CanonicalDigest(),
                    resultingById[id].CanonicalDigest()))
                throw new InvalidDataException("qa04.production-turnover.unchanged-active-authority-drift");
        }
    }

    private static CrossDomainTransactionStateV1[] CanonicalActive(
        IReadOnlyCollection<CrossDomainTransactionStateV1> states,
        string scope)
    {
        var ordered = states.OrderBy(static state => state.TransactionId).ToArray();
        if (ordered.Length != checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount) ||
            ordered.Select(static state => state.TransactionId).Distinct().Count() != ordered.Length ||
            ordered.Any(static state => !state.IsActive || state.TerminalStep is not null))
            throw new InvalidDataException($"qa04.production-turnover.{scope}-active-set-invalid");
        return ordered;
    }

    private static void RequireSameActiveAuthority(
        IReadOnlyList<CrossDomainTransactionStateV1> basis,
        IReadOnlyList<CrossDomainTransactionStateV1> resulting)
    {
        for (var index = 0; index < basis.Count; index++)
        {
            if (basis[index].TransactionId != resulting[index].TransactionId ||
                !CryptographicOperations.FixedTimeEquals(
                    basis[index].CanonicalDigest(),
                    resulting[index].CanonicalDigest()))
                throw new InvalidDataException("qa04.production-turnover.non-turnover-active-set-drift");
        }
    }
}
