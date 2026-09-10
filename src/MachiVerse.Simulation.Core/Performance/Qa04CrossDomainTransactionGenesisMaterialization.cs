using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CrossDomainTransactionGenesisBindingV1(
    ulong SlotOrdinal,
    OpaqueId128 DescriptorTransactionId,
    OpaqueId128 AuthoritativeTransactionId,
    StableToken TransactionKind,
    IReadOnlyList<PartitionRecordRefV1> ParticipantTargetRefs,
    CrossDomainTransactionStateV1 State);

/// <summary>
/// Materializes the exact perf.reference.v1 initial 10,000 ACTIVE transaction states. Every
/// participant target is selected from an externally supplied actual canonical partition pool;
/// missing/empty pools fail closed and are never replaced by synthetic placeholder records.
/// </summary>
public static class Qa04CrossDomainTransactionGenesisMaterializerV1
{
    public const ulong CanonicalActiveCount = 10_000;

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

    private static readonly string[] OtherKinds =
    [
        "transaction.demolition",
        "transaction.birth",
        "transaction.death",
        "transaction.disease-transmission",
        "transaction.public-record",
        "transaction.military-operation",
    ];

    private static readonly IReadOnlyDictionary<ulong, ulong> OtherOrdinalBySlot = BuildOtherOrdinals();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();
        if (CanonicalActiveCount != Qa04ReferenceScenariosV1.ActiveCrossDomainTransactionTarget)
            throw new InvalidDataException("qa04.transaction.genesis-active-count-drift");
        if (ParticipantPartitionByDomain.Count != DomainRanks.Count)
            throw new InvalidDataException("qa04.transaction.genesis-participant-domain-coverage");

        foreach (var entry in StandardDomainExecutionPlanV1.Create().Entries)
        {
            if (!ParticipantPartitionByDomain.TryGetValue(entry.DomainToken.Value, out var partitionId))
                throw new InvalidDataException($"qa04.transaction.genesis-participant-domain-missing:{entry.DomainToken.Value}");
            var partition = StandardDomainPartitionRegistry.Get(partitionId);
            if (partition.OwnerDomain != entry.DomainToken)
                throw new InvalidDataException($"qa04.transaction.genesis-participant-owner:{entry.DomainToken.Value}");
        }

        if (OtherKinds.Length != 6 || OtherKinds.Distinct(StringComparer.Ordinal).Count() != 6 ||
            OtherKinds.Any(kind => !CrossDomainTransactionKindRegistryV1.Contains(new StableToken(kind))))
            throw new InvalidDataException("qa04.transaction.genesis-other-kind-contract");
        if (OtherOrdinalBySlot.Count != 200 || OtherOrdinalBySlot.Values.Distinct().Count() != 200 ||
            OtherOrdinalBySlot.Values.Min() != 0 || OtherOrdinalBySlot.Values.Max() != 199)
            throw new InvalidDataException("qa04.transaction.genesis-other-ordinal-contract");
    }

    public static IReadOnlyList<Qa04CrossDomainTransactionGenesisBindingV1> Materialize(
        IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> canonicalRecordPools)
    {
        ArgumentNullException.ThrowIfNull(canonicalRecordPools);
        ValidateCanonicalContract();
        ValidatePools(canonicalRecordPools);

        var result = new Qa04CrossDomainTransactionGenesisBindingV1[checked((int)CanonicalActiveCount)];
        for (ulong slot = 0; slot < CanonicalActiveCount; slot++)
            result[checked((int)slot)] = MaterializeSlotValidated(slot, canonicalRecordPools);

        if (result.Select(static binding => binding.AuthoritativeTransactionId).Distinct().Count() != result.Length)
            throw new InvalidDataException("qa04.transaction.genesis-authoritative-id-duplicate");
        if (result.Any(static binding => !binding.State.IsActive || binding.State.CreatedStep != 0 || binding.State.UpdatedStep != 0))
            throw new InvalidDataException("qa04.transaction.genesis-active-state-drift");
        return Array.AsReadOnly(result);
    }

    public static Qa04CrossDomainTransactionGenesisBindingV1 MaterializeSlot(
        ulong slotOrdinal,
        IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> canonicalRecordPools)
    {
        ArgumentNullException.ThrowIfNull(canonicalRecordPools);
        ValidateCanonicalContract();
        if (slotOrdinal >= CanonicalActiveCount) throw new ArgumentOutOfRangeException(nameof(slotOrdinal));
        ValidatePools(canonicalRecordPools);
        return MaterializeSlotValidated(slotOrdinal, canonicalRecordPools);
    }

    private static Qa04CrossDomainTransactionGenesisBindingV1 MaterializeSlotValidated(
        ulong slotOrdinal,
        IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> canonicalRecordPools)
    {
        var descriptor = Qa04ReferenceScenariosV1.ActiveTransaction(slotOrdinal);
        var mappedKind = MapKind(descriptor.KindToken, slotOrdinal);
        var registration = CrossDomainTransactionKindRegistryV1.GetRegistration(mappedKind);
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

        var root = new CausalityRefV1(
            CausalityRefKindV1.Entity,
            descriptor.SubjectIds[0].ToBytes(),
            basisStep: 0);
        var authoritativeId = TransactionIdentityV1.Derive(
            Qa04ReferenceLoadV1.WorldId,
            mappedKind,
            basisStep: 0,
            root,
            descriptor.SubjectIds,
            stableLocalOrdinal: slotOrdinal);

        var participantMaterial = participantDomains.Select(pair =>
        {
            var partitionId = ParticipantPartitionByDomain[pair.Domain.Value];
            var pool = canonicalRecordPools[partitionId];
            var target = pool[checked((int)(slotOrdinal % checked((ulong)pool.Count)))];
            var targetRef = new PartitionRecordRefV1(partitionId, target);
            var intentId = HashSuite.Trunc128(HashSuite.DomainHash(
                "mv.perf-reference-transaction-intent.v1",
                writer =>
                {
                    writer.WriteArrayStart(3);
                    writer.WriteBytes(authoritativeId.ToBytes());
                    writer.WriteAsciiText(pair.Domain.Value);
                    writer.WriteAsciiText(partitionId);
                }));
            var effectDigest = HashSuite.DomainHash(
                "mv.perf-reference-transaction-effect.v1",
                writer =>
                {
                    writer.WriteArrayStart(5);
                    writer.WriteBytes(authoritativeId.ToBytes());
                    writer.WriteUnsigned(0);
                    writer.WriteAsciiText(pair.Domain.Value);
                    writer.WriteAsciiText(partitionId);
                    writer.WriteBytes(intentId.ToBytes());
                });
            var participant = new TransactionParticipantCandidateV1(
                pair.Domain,
                new StableToken(partitionId),
                [intentId],
                pair.Required,
                TransactionParticipantOutcomeV1.Ready,
                effectDigest);
            return (Participant: participant, TargetRef: targetRef);
        }).ToArray();

        var invariants = CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(mappedKind)
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
            mappedKind,
            basisStep: 0,
            root,
            descriptor.SubjectIds,
            stableLocalOrdinal: slotOrdinal,
            participantMaterial.Select(static value => value.Participant),
            invariants);
        if (!candidate.CanFinalize || candidate.Status != TransactionCandidateStatusV1.Valid ||
            candidate.TransactionId != authoritativeId || candidate.IsAuthoritative)
            throw new InvalidDataException("qa04.transaction.genesis-candidate-materialization");

        var state = new CrossDomainTransactionStateV1(
            candidate.TransactionId,
            candidate.TransactionKind,
            TransactionLifecycleV1.Active,
            createdStep: 0,
            updatedStep: 0,
            terminalStep: null,
            candidate.RootCausalityRef,
            candidate.SubjectRefs,
            candidate.Participants.Select(PersistentTransactionParticipantV1.FromCandidate),
            candidate.InvariantResults);
        return new Qa04CrossDomainTransactionGenesisBindingV1(
            slotOrdinal,
            descriptor.TransactionId,
            authoritativeId,
            mappedKind,
            Array.AsReadOnly(participantMaterial.Select(static value => value.TargetRef).ToArray()),
            state);
    }

    private static StableToken MapKind(StableToken descriptorKind, ulong slotOrdinal)
    {
        if (descriptorKind.Value == "other-registered-transactions")
        {
            if (!OtherOrdinalBySlot.TryGetValue(slotOrdinal, out var otherOrdinal))
                throw new InvalidDataException("qa04.transaction.genesis-other-slot-missing");
            return CrossDomainTransactionKindRegistryV1.Get(OtherKinds[checked((int)(otherOrdinal % 6))]);
        }
        return CrossDomainTransactionKindRegistryV1.Get("transaction." + descriptorKind.Value);
    }

    private static IReadOnlyDictionary<ulong, ulong> BuildOtherOrdinals()
    {
        var orderedSlots = Enumerable.Range(0, checked((int)CanonicalActiveCount))
            .Select(static value => (ulong)value)
            .Where(slot => Qa04ReferenceScenariosV1.ActiveTransaction(slot).KindToken.Value == "other-registered-transactions")
            .OrderBy(slot => Qa04ReferenceScenariosV1.ActiveTransaction(slot).TransactionId)
            .ToArray();
        return orderedSlots
            .Select((slot, index) => (slot, Index: checked((ulong)index)))
            .ToDictionary(static pair => pair.slot, static pair => pair.Index);
    }

    private static ushort DomainRank(StableToken domain)
        => DomainRanks.TryGetValue(domain, out var rank)
            ? rank
            : throw new InvalidDataException("qa04.transaction.genesis-domain-rank-missing");

    private static void ValidatePools(IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> pools)
    {
        foreach (var partitionId in ParticipantPartitionByDomain.Values.Distinct(StringComparer.Ordinal))
        {
            if (!pools.TryGetValue(partitionId, out var pool) || pool is null || pool.Count == 0)
                throw new InvalidDataException($"qa04.transaction.genesis-participant-pool-missing:{partitionId}");
            OpaqueId128? previous = null;
            foreach (var id in pool)
            {
                if (id.IsZero) throw new InvalidDataException($"qa04.transaction.genesis-participant-pool-zero:{partitionId}");
                if (previous is { } prior && prior.CompareTo(id) >= 0)
                    throw new InvalidDataException($"qa04.transaction.genesis-participant-pool-order:{partitionId}");
                previous = id;
            }
        }
    }
}
