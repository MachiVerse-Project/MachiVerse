using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ActiveTransactionSlotV1(
    ulong SlotOrdinal,
    ulong Generation,
    IReadOnlyList<PartitionRecordRefV1> ParticipantTargetRefs,
    CrossDomainTransactionStateV1 State);

public sealed record Qa04CrossDomainTransactionTurnoverV1(
    ulong BasisStep,
    ulong ResultingStep,
    IReadOnlyList<CrossDomainTransactionStateV1> CommittedStates,
    IReadOnlyList<Qa04ActiveTransactionSlotV1> ActiveSlots);

/// <summary>
/// Applies the perf.reference.v1 300-Step transaction turnover rule. Exactly one 1,000-slot cohort
/// is terminalized at each due basis Step and replaced in the same resulting State, preserving the
/// steady authoritative ACTIVE count of 10,000.
/// </summary>
public static class Qa04CrossDomainTransactionTurnoverMaterializerV1
{
    public const ulong TurnoverCadenceSteps = 300;
    public const ulong CohortCount = 10;
    public const ulong CohortSize = 1_000;
    public const ulong LifetimeSteps = 3_000;

    private static readonly IReadOnlyDictionary<string, string> ParticipantPartitionByDomain =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["spatial"] = "spatial.scope_registry",
            ["environment"] = "environment.hazard",
            ["physical_built"] = "physical.presence",
            ["participation"] = "participation.control_mode",
            ["resident"] = "resident.identity_lifecycle",
            ["society_economy"] = "society.contract_claim",
            ["governance_security"] = "governance.permission_license",
            ["infrastructure_information"] = "infrastructure.service_queue",
        };

    private static readonly IReadOnlyDictionary<StableToken, ushort> DomainRanks =
        StandardDomainExecutionPlanV1.Create().Entries.ToDictionary(static entry => entry.DomainToken, static entry => entry.DomainRank);

    public static void ValidateCanonicalContract()
    {
        Qa04CrossDomainTransactionGenesisMaterializerV1.ValidateCanonicalContract();
        if (TurnoverCadenceSteps != Qa04ReferenceScenariosV1.CrossDomainTransactionCreationEverySteps ||
            CohortCount != 10 || CohortSize * CohortCount != Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount ||
            LifetimeSteps != 3_000 || LifetimeSteps != TurnoverCadenceSteps * CohortCount)
            throw new InvalidDataException("qa04.transaction.turnover-contract-drift");
    }

    public static IReadOnlyList<Qa04ActiveTransactionSlotV1> Initialize(
        IReadOnlyList<Qa04CrossDomainTransactionGenesisBindingV1> genesis)
    {
        ArgumentNullException.ThrowIfNull(genesis);
        ValidateCanonicalContract();
        if (genesis.Count != checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount))
            throw new InvalidDataException("qa04.transaction.turnover-genesis-count");

        var ordered = genesis.OrderBy(static binding => binding.SlotOrdinal).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var binding = ordered[index];
            if (binding.SlotOrdinal != checked((ulong)index) || !binding.State.IsActive ||
                binding.State.CreatedStep != 0 || binding.State.UpdatedStep != 0)
                throw new InvalidDataException("qa04.transaction.turnover-genesis-slot");
        }

        return Array.AsReadOnly(ordered.Select(static binding => new Qa04ActiveTransactionSlotV1(
            binding.SlotOrdinal,
            Generation: 0,
            binding.ParticipantTargetRefs,
            binding.State)).ToArray());
    }

    public static Qa04CrossDomainTransactionTurnoverV1 Apply(
        ulong basisStep,
        IReadOnlyList<Qa04ActiveTransactionSlotV1> activeSlots,
        IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> canonicalRecordPools)
    {
        ArgumentNullException.ThrowIfNull(activeSlots);
        ArgumentNullException.ThrowIfNull(canonicalRecordPools);
        ValidateCanonicalContract();
        ValidateActiveSlots(activeSlots);
        ValidatePools(canonicalRecordPools);
        if (basisStep == 0 || basisStep % TurnoverCadenceSteps != 0)
            throw new ArgumentOutOfRangeException(nameof(basisStep), "Turnover basis Step must be a nonzero 300-Step cadence boundary.");

        var resultingStep = checked(basisStep + 1);
        var committed = new List<CrossDomainTransactionStateV1>(checked((int)CohortSize));
        var next = new Qa04ActiveTransactionSlotV1[activeSlots.Count];
        ulong replaced = 0;

        foreach (var slot in activeSlots.OrderBy(static value => value.SlotOrdinal))
        {
            if (!IsDue(slot, basisStep))
            {
                next[checked((int)slot.SlotOrdinal)] = slot;
                continue;
            }

            var terminal = slot.State.Commit(resultingStep);
            committed.Add(terminal);
            var replacementGeneration = checked(slot.Generation + 1);
            next[checked((int)slot.SlotOrdinal)] = CreateReplacement(
                slot,
                replacementGeneration,
                basisStep,
                resultingStep,
                canonicalRecordPools);
            replaced++;
        }

        if (replaced != CohortSize || committed.Count != checked((int)CohortSize))
            throw new InvalidDataException($"qa04.transaction.turnover-cohort-count:{replaced}");
        if (next.Any(static value => value is null) || next.Any(static value => !value.State.IsActive))
            throw new InvalidDataException("qa04.transaction.turnover-active-set-incomplete");
        if (next.Select(static value => value.State.TransactionId).Distinct().Count() != next.Length)
            throw new InvalidDataException("qa04.transaction.turnover-active-id-duplicate");

        return new Qa04CrossDomainTransactionTurnoverV1(
            basisStep,
            resultingStep,
            Array.AsReadOnly(committed.OrderBy(static state => state.TransactionId).ToArray()),
            Array.AsReadOnly(next));
    }

    public static bool IsDue(Qa04ActiveTransactionSlotV1 slot, ulong basisStep)
    {
        ArgumentNullException.ThrowIfNull(slot);
        var initialDue = checked(TurnoverCadenceSteps * (slot.SlotOrdinal % CohortCount + 1));
        var expected = checked(initialDue + slot.Generation * LifetimeSteps);
        return basisStep == expected;
    }

    private static Qa04ActiveTransactionSlotV1 CreateReplacement(
        Qa04ActiveTransactionSlotV1 prior,
        ulong generation,
        ulong basisStep,
        ulong resultingStep,
        IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> pools)
    {
        var kind = prior.State.TransactionKind;
        var registration = CrossDomainTransactionKindRegistryV1.GetRegistration(kind);
        var root = new CausalityRefV1(
            CausalityRefKindV1.Transaction,
            prior.State.TransactionId.ToBytes(),
            basisStep);
        var stableLocalOrdinal = checked(prior.SlotOrdinal + generation * Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount);
        var transactionId = TransactionIdentityV1.Derive(
            Qa04ReferenceLoadV1.WorldId,
            kind,
            basisStep,
            root,
            prior.State.SubjectIds,
            stableLocalOrdinal);

        var participantDomains = registration.RequiredDomains
            .Select(static domain => (Domain: domain, Required: true))
            .Concat(registration.RequiredAnyDomainGroups.Select(group =>
            {
                var selected = group
                    .OrderBy(DomainRank)
                    .ThenBy(static domain => domain.Value, StringComparer.Ordinal)
                    .First();
                return (Domain: selected, Required: true);
            }))
            .Concat(registration.OptionalDomains.Select(static domain => (Domain: domain, Required: false)))
            .OrderBy(pair => DomainRank(pair.Domain))
            .ThenBy(static pair => pair.Domain.Value, StringComparer.Ordinal)
            .ToArray();

        var participantMaterial = participantDomains.Select(pair =>
        {
            var partitionId = ParticipantPartitionByDomain[pair.Domain.Value];
            var pool = pools[partitionId];
            var targetId = pool[checked((int)(prior.SlotOrdinal % checked((ulong)pool.Count)))];
            var targetRef = new PartitionRecordRefV1(partitionId, targetId);
            var intentId = HashSuite.Trunc128(HashSuite.DomainHash(
                "mv.perf-reference-transaction-intent.v1",
                writer =>
                {
                    writer.WriteArrayStart(3);
                    writer.WriteBytes(transactionId.ToBytes());
                    writer.WriteAsciiText(pair.Domain.Value);
                    writer.WriteAsciiText(partitionId);
                }));
            var effectDigest = HashSuite.DomainHash(
                "mv.perf-reference-transaction-effect.v1",
                writer =>
                {
                    writer.WriteArrayStart(5);
                    writer.WriteBytes(transactionId.ToBytes());
                    writer.WriteUnsigned(basisStep);
                    writer.WriteAsciiText(pair.Domain.Value);
                    writer.WriteAsciiText(partitionId);
                    writer.WriteBytes(intentId.ToBytes());
                });
            return (
                Participant: new TransactionParticipantCandidateV1(
                    pair.Domain,
                    new StableToken(partitionId),
                    [intentId],
                    pair.Required,
                    TransactionParticipantOutcomeV1.Ready,
                    effectDigest),
                TargetRef: targetRef);
        }).ToArray();

        var invariants = CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind)
            .OrderBy(static id => id.Value, StringComparer.Ordinal)
            .Select(static invariantId => new InvariantResultV1(
                invariantId,
                InvariantSeverityV1.CommitBlocking,
                InvariantOutcomeV1.Pass,
                Array.Empty<CausalityRefV1>(),
                null))
            .ToArray();
        var candidate = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
            Qa04ReferenceLoadV1.WorldId,
            kind,
            basisStep,
            root,
            prior.State.SubjectIds,
            stableLocalOrdinal,
            participantMaterial.Select(static value => value.Participant),
            invariants);
        if (!candidate.CanFinalize || candidate.TransactionId != transactionId || candidate.IsAuthoritative)
            throw new InvalidDataException("qa04.transaction.turnover-replacement-candidate");

        var state = CrossDomainTransactionStateV1.FromValidCandidate(candidate, resultingStep);
        if (!state.SubjectIds.SequenceEqual(prior.State.SubjectIds))
            throw new InvalidDataException("qa04.transaction.turnover-subject-drift");
        return new Qa04ActiveTransactionSlotV1(
            prior.SlotOrdinal,
            generation,
            Array.AsReadOnly(participantMaterial.Select(static value => value.TargetRef).ToArray()),
            state);
    }

    private static void ValidateActiveSlots(IReadOnlyList<Qa04ActiveTransactionSlotV1> activeSlots)
    {
        if (activeSlots.Count != checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount))
            throw new InvalidDataException("qa04.transaction.turnover-active-count");
        var ordered = activeSlots.OrderBy(static value => value.SlotOrdinal).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var slot = ordered[index] ?? throw new InvalidDataException("qa04.transaction.turnover-active-null");
            if (slot.SlotOrdinal != checked((ulong)index) || !slot.State.IsActive || slot.State.TerminalStep is not null)
                throw new InvalidDataException("qa04.transaction.turnover-active-slot");
        }
        if (ordered.Select(static value => value.State.TransactionId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("qa04.transaction.turnover-active-id-duplicate");
    }

    private static void ValidatePools(IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> pools)
    {
        foreach (var partitionId in ParticipantPartitionByDomain.Values.Distinct(StringComparer.Ordinal))
        {
            if (!pools.TryGetValue(partitionId, out var pool) || pool is null || pool.Count == 0)
                throw new InvalidDataException($"qa04.transaction.turnover-participant-pool-missing:{partitionId}");
            OpaqueId128? previous = null;
            foreach (var id in pool)
            {
                if (id.IsZero) throw new InvalidDataException($"qa04.transaction.turnover-participant-pool-zero:{partitionId}");
                if (previous is { } prior && prior.CompareTo(id) >= 0)
                    throw new InvalidDataException($"qa04.transaction.turnover-participant-pool-order:{partitionId}");
                previous = id;
            }
        }
    }

    private static ushort DomainRank(StableToken domain)
        => DomainRanks.TryGetValue(domain, out var rank)
            ? rank
            : throw new InvalidDataException("qa04.transaction.turnover-domain-rank-missing");
}
