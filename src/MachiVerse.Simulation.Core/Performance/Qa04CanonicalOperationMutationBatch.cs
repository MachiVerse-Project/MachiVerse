using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CanonicalOperationMutationStateV1(
    DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1> InfrastructureServiceQueue,
    DomainPartitionStateV1<ParticipationControlModePayloadV1> ParticipationControlMode,
    DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> ResidentBehaviorState,
    DomainPartitionStateV1<PhysicalPresencePayloadV1> PhysicalPresence,
    SocietyMarketTransactionPartitionStateV2 MarketTransaction,
    DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1> GovernanceSecurityIncident,
    DomainPartitionStateV1<EnvironmentHazardPayloadV1> EnvironmentHazard);

public sealed record Qa04CanonicalOperationMutationChangeV1(
    StableToken FamilyToken,
    string OperationKind,
    OpaqueId128 OperationId,
    byte[] ImmutablePayloadDigest,
    SameStepOrderKey OrderKey,
    StableToken PartitionId,
    OpaqueId128 ChangedRecordId,
    SchemaRefV1 RecordSchema,
    ulong ResultRevision,
    ulong ResultCreatedStep,
    DetailLevelV1 ResultDetailLevel,
    StableToken MutationMode);

public sealed record Qa04CanonicalOperationMutationBatchResultV1(
    ulong EffectiveStep,
    Qa04CanonicalOperationMutationStateV1 State,
    IReadOnlyList<OpaqueId128> AppliedOperationIds,
    IReadOnlyDictionary<string, ulong> AppliedCountByFamily,
    IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> Changes);

/// <summary>
/// Gate-2 integration stage that applies already-authoritative perf.reference.v1 Operations to the
/// six typed mutation targets in canonical scheduler order. This composes the six Gate-1 handlers;
/// it does not itself claim full authoritative-Step availability, build partition candidates, or
/// cross the SQLite COMMIT/publish boundary.
///
/// Production batches retain each large immutable basis partition and buffer only additions or
/// replacements while individual Gate-1 handlers validate one-operation material. Full canonical
/// partition states are rebuilt once after the ordered batch, rather than once per Operation.
/// </summary>
public static class Qa04CanonicalOperationMutationBatchV1
{
    private const string InfrastructureFamily = "infrastructure-service-delivery";
    private const string ResidentFamily = "participation-control-resident-action";
    private const string PhysicalFamily = "physical-item-movement-work";
    private const string MarketFamily = "society-market-payment-contract";
    private const string GovernanceFamily = "governance-security";
    private const string EnvironmentFamily = "environment-spatial-admin-synthetic";

    private static readonly StableToken Create = new("create");
    private static readonly StableToken Revise = new("revise");

    public static Qa04CanonicalOperationMutationBatchResultV1 Apply(
        OpaqueId128 worldId,
        ulong effectiveStep,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationStateV1 initialState,
        IDomainRecordSchemaResolverV1 references)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        if (worldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.full-step.mutation-world-id-drift");
        if (effectiveStep == 0) throw new ArgumentOutOfRangeException(nameof(effectiveStep));
        ArgumentNullException.ThrowIfNull(orderedBindings);
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(references);
        if (orderedBindings.Count == 0)
            throw new InvalidDataException("qa04.full-step.mutation-batch-empty");

        ValidateStateIdentities(initialState);

        var infrastructure = new AdditionOverlayV1<InfrastructureServiceQueuePayloadV1>(
            initialState.InfrastructureServiceQueue);
        var physical = new RevisionOverlayV1<PhysicalPresencePayloadV1>(initialState.PhysicalPresence);
        var market = new MarketAdditionOverlayV1(initialState.MarketTransaction);
        var governance = new AdditionOverlayV1<GovernanceSecurityIncidentPayloadV1>(
            initialState.GovernanceSecurityIncident);
        var environment = new AdditionOverlayV1<EnvironmentHazardPayloadV1>(initialState.EnvironmentHazard);
        var residentBehaviorState = initialState.ResidentBehaviorState;

        var emptyInfrastructure = EmptyLike(initialState.InfrastructureServiceQueue);
        var emptyPhysical = EmptyLike(initialState.PhysicalPresence);
        var emptyMarket = new SocietyMarketTransactionPartitionStateV2(
            Array.Empty<SocietyMarketTransactionRecordMaterialV2>());
        var emptyGovernance = EmptyLike(initialState.GovernanceSecurityIncident);
        var emptyEnvironment = EmptyLike(initialState.EnvironmentHazard);

        var appliedIds = new List<OpaqueId128>(orderedBindings.Count);
        var appliedIdSet = new HashSet<OpaqueId128>();
        var counts = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var changes = new List<Qa04CanonicalOperationMutationChangeV1>(orderedBindings.Count);
        SameStepOrderKey? previousOrderKey = null;

        foreach (var binding in orderedBindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (binding.SourceDescriptor is null || binding.ScheduledOperation is null || binding.OrderKey is null)
                throw new InvalidDataException("qa04.full-step.mutation-binding-null");
            if (binding.ScheduledOperation.EffectiveStep != effectiveStep)
                throw new InvalidDataException("qa04.full-step.mutation-effective-step-drift");
            if (!binding.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(
                    binding.ScheduledOperation.OrderKey.ToDatabaseBytes()))
                throw new InvalidDataException("qa04.full-step.mutation-order-key-drift");
            if (previousOrderKey is not null && previousOrderKey.CompareTo(binding.OrderKey) >= 0)
                throw new InvalidDataException("qa04.full-step.mutation-order-not-canonical");
            previousOrderKey = binding.OrderKey;

            var operationId = binding.SourceDescriptor.OperationId;
            if (operationId.IsZero || !appliedIdSet.Add(operationId))
                throw new InvalidDataException("qa04.full-step.mutation-operation-id-duplicate");

            Qa04CanonicalOperationMutationChangeV1 change;
            switch (binding.SourceDescriptor.FamilyToken.Value)
            {
                case InfrastructureFamily:
                {
                    var result = Qa04InfrastructureServiceReserveApplicationV1.Apply(
                        worldId,
                        binding,
                        emptyInfrastructure,
                        references);
                    infrastructure.Add(
                        result.CreatedRecord,
                        "qa04.infrastructure.service-reserve-duplicate");
                    change = Change(
                        binding,
                        InfrastructureServiceQueuePayloadV1.PartitionId,
                        result.CreatedRecord.RecordId,
                        result.CreatedRecord.RecordSchema,
                        result.CreatedRecord.Revision,
                        result.CreatedRecord.CreatedStep,
                        result.CreatedRecord.DetailLevel,
                        Create);
                    break;
                }
                case ResidentFamily:
                {
                    var controlModeId = Qa04ParticipationControlModeCanonicalAuthorityV1.RecordId(
                        binding.SourceDescriptor.FamilyOrdinal);
                    if (!initialState.ParticipationControlMode.TryGet(controlModeId, out var controlMode) ||
                        controlMode is null)
                        throw new InvalidDataException("qa04.full-step.mutation-control-mode-missing");

                    var result = Qa04ResidentActionApplicationV1.Apply(
                        worldId,
                        binding,
                        residentBehaviorState,
                        controlMode,
                        references);
                    residentBehaviorState = result.BehaviorState;
                    change = Change(
                        binding,
                        ResidentBehaviorStatePayloadV1.PartitionId,
                        result.AppliedRecord.RecordId,
                        result.AppliedRecord.RecordSchema,
                        result.AppliedRecord.Revision,
                        result.AppliedRecord.CreatedStep,
                        result.AppliedRecord.DetailLevel,
                        result.Created ? Create : Revise);
                    break;
                }
                case PhysicalFamily:
                {
                    if (!physical.TryGet(binding.PrimaryTarget.RecordId, out var target) || target is null)
                    {
                        _ = Qa04PhysicalMoveApplicationV1.Apply(binding, emptyPhysical, references);
                        throw new InvalidDataException("qa04.physical.move-target-missing");
                    }

                    var singleTarget = new DomainPartitionStateV1<PhysicalPresencePayloadV1>(
                        initialState.PhysicalPresence.Identity,
                        new[] { target });
                    var result = Qa04PhysicalMoveApplicationV1.Apply(binding, singleTarget, references);
                    physical.Replace(
                        result.AppliedRecord,
                        "qa04.physical.move-target-missing");
                    change = Change(
                        binding,
                        PhysicalPresencePayloadV1.PartitionId,
                        result.AppliedRecord.RecordId,
                        result.AppliedRecord.RecordSchema,
                        result.AppliedRecord.Revision,
                        result.AppliedRecord.CreatedStep,
                        result.AppliedRecord.DetailLevel,
                        Revise);
                    break;
                }
                case MarketFamily:
                {
                    if (!market.TryGet(binding.PrimaryTarget.RecordId, out var target) || target is null)
                    {
                        _ = Qa04MarketOrderApplicationV1.Apply(binding, emptyMarket, references);
                        throw new InvalidDataException("qa04.market.order-target-missing");
                    }

                    var singleTarget = new SocietyMarketTransactionPartitionStateV2(new[] { target });
                    var result = Qa04MarketOrderApplicationV1.Apply(binding, singleTarget, references);
                    market.Add(
                        result.CreatedOrder,
                        "qa04.market.order-record-id-collision");
                    change = Change(
                        binding,
                        SocietyMarketTransactionRecordSchemaV2.PartitionId,
                        result.CreatedOrder.RecordId,
                        result.CreatedOrder.RecordSchema,
                        result.CreatedOrder.Revision,
                        result.CreatedOrder.CreatedStep,
                        result.CreatedOrder.DetailLevel,
                        Create);
                    break;
                }
                case GovernanceFamily:
                {
                    var result = Qa04GovernanceIncidentApplicationV1.Apply(
                        binding,
                        emptyGovernance,
                        references);
                    governance.Add(
                        result.CreatedIncident,
                        "qa04.governance.incident-record-id-collision");
                    change = Change(
                        binding,
                        GovernanceSecurityIncidentPayloadV1.PartitionId,
                        result.CreatedIncident.RecordId,
                        result.CreatedIncident.RecordSchema,
                        result.CreatedIncident.Revision,
                        result.CreatedIncident.CreatedStep,
                        result.CreatedIncident.DetailLevel,
                        Create);
                    break;
                }
                case EnvironmentFamily:
                {
                    var result = Qa04EnvironmentHazardApplicationV1.Apply(
                        binding,
                        emptyEnvironment,
                        references);
                    environment.Add(
                        result.CreatedHazard,
                        "qa04.environment.hazard-record-id-collision");
                    change = Change(
                        binding,
                        EnvironmentHazardPayloadV1.PartitionId,
                        result.CreatedHazard.RecordId,
                        result.CreatedHazard.RecordSchema,
                        result.CreatedHazard.Revision,
                        result.CreatedHazard.CreatedStep,
                        result.CreatedHazard.DetailLevel,
                        Create);
                    break;
                }
                default:
                    throw new InvalidDataException(
                        $"qa04.full-step.mutation-family-unregistered:{binding.SourceDescriptor.FamilyToken.Value}");
            }

            appliedIds.Add(operationId);
            changes.Add(change);
            counts[binding.SourceDescriptor.FamilyToken.Value] = checked(
                counts.GetValueOrDefault(binding.SourceDescriptor.FamilyToken.Value) + 1UL);
        }

        var state = new Qa04CanonicalOperationMutationStateV1(
            infrastructure.Build(),
            initialState.ParticipationControlMode,
            residentBehaviorState,
            physical.Build(),
            market.Build(),
            governance.Build(),
            environment.Build());
        ValidateStateIdentities(state);

        return new Qa04CanonicalOperationMutationBatchResultV1(
            effectiveStep,
            state,
            Array.AsReadOnly(appliedIds.ToArray()),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, ulong>(counts),
            Array.AsReadOnly(changes.ToArray()));
    }

    private static DomainPartitionStateV1<TPayload> EmptyLike<TPayload>(
        DomainPartitionStateV1<TPayload> source)
        => new(
            source.Identity,
            Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static Qa04CanonicalOperationMutationChangeV1 Change(
        Qa04CanonicalOperationBindingResultV1 binding,
        string partitionId,
        OpaqueId128 changedRecordId,
        SchemaRefV1 recordSchema,
        ulong resultRevision,
        ulong resultCreatedStep,
        DetailLevelV1 resultDetailLevel,
        StableToken mutationMode)
    {
        var immutableDigest = binding.BoundDescriptor.PayloadDigest.ToArray();
        if (immutableDigest.Length != 32)
            throw new InvalidDataException("qa04.full-step.mutation-payload-digest-length");
        if (changedRecordId.IsZero)
            throw new InvalidDataException("qa04.full-step.mutation-changed-record-id-zero");
        if (mutationMode != Create && mutationMode != Revise)
            throw new InvalidDataException("qa04.full-step.mutation-mode-invalid");

        return new Qa04CanonicalOperationMutationChangeV1(
            binding.SourceDescriptor.FamilyToken,
            binding.Operation.OperationKind,
            binding.SourceDescriptor.OperationId,
            immutableDigest,
            binding.OrderKey,
            new StableToken(partitionId),
            changedRecordId,
            recordSchema,
            resultRevision,
            resultCreatedStep,
            resultDetailLevel,
            mutationMode);
    }

    private static void ValidateStateIdentities(Qa04CanonicalOperationMutationStateV1 state)
    {
        if (state.InfrastructureServiceQueue.Identity !=
            StandardDomainPartitionRegistry.Get(InfrastructureServiceQueuePayloadV1.PartitionId))
            throw new InvalidDataException("qa04.full-step.mutation-infrastructure-state-identity");
        if (state.ParticipationControlMode.Identity !=
            StandardDomainPartitionRegistry.Get(ParticipationControlModePayloadV1.PartitionId))
            throw new InvalidDataException("qa04.full-step.mutation-control-state-identity");
        if (state.ResidentBehaviorState.Identity !=
            StandardDomainPartitionRegistry.Get(ResidentBehaviorStatePayloadV1.PartitionId))
            throw new InvalidDataException("qa04.full-step.mutation-resident-state-identity");
        if (state.PhysicalPresence.Identity !=
            StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId))
            throw new InvalidDataException("qa04.full-step.mutation-physical-state-identity");
        if (state.MarketTransaction.State.Identity != SocietyMarketTransactionPartitionIdentityV2.Identity)
            throw new InvalidDataException("qa04.full-step.mutation-market-state-identity");
        if (state.GovernanceSecurityIncident.Identity !=
            StandardDomainPartitionRegistry.Get(GovernanceSecurityIncidentPayloadV1.PartitionId))
            throw new InvalidDataException("qa04.full-step.mutation-governance-state-identity");
        if (state.EnvironmentHazard.Identity !=
            StandardDomainPartitionRegistry.Get(EnvironmentHazardPayloadV1.PartitionId))
            throw new InvalidDataException("qa04.full-step.mutation-environment-state-identity");
    }

    private static T[] MergeCanonical<T>(
        IEnumerable<T> initialCanonical,
        int initialCount,
        IReadOnlyList<T> additionsCanonical,
        Func<T, OpaqueId128> idSelector)
    {
        ArgumentNullException.ThrowIfNull(initialCanonical);
        ArgumentNullException.ThrowIfNull(additionsCanonical);
        ArgumentNullException.ThrowIfNull(idSelector);
        if (initialCount < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCount));

        var merged = new T[checked(initialCount + additionsCanonical.Count)];
        using var initial = initialCanonical.GetEnumerator();
        var hasInitial = initial.MoveNext();
        var additionIndex = 0;
        var outputIndex = 0;

        while (hasInitial || additionIndex < additionsCanonical.Count)
        {
            if (!hasInitial)
            {
                merged[outputIndex++] = additionsCanonical[additionIndex++];
                continue;
            }

            if (additionIndex >= additionsCanonical.Count)
            {
                merged[outputIndex++] = initial.Current;
                hasInitial = initial.MoveNext();
                continue;
            }

            var initialId = idSelector(initial.Current);
            var additionId = idSelector(additionsCanonical[additionIndex]);
            var comparison = initialId.CompareTo(additionId);
            if (comparison < 0)
            {
                merged[outputIndex++] = initial.Current;
                hasInitial = initial.MoveNext();
            }
            else if (comparison > 0)
            {
                merged[outputIndex++] = additionsCanonical[additionIndex++];
            }
            else
            {
                throw new InvalidDataException("qa04.mutation.canonical-merge-duplicate");
            }
        }

        if (outputIndex != merged.Length)
            throw new InvalidDataException("qa04.mutation.canonical-merge-count-drift");
        return merged;
    }

    private sealed class AdditionOverlayV1<TPayload>
    {
        private readonly DomainPartitionStateV1<TPayload> _initial;
        private readonly Dictionary<OpaqueId128, DomainRecordEnvelopeV1<TPayload>> _additions = new();

        public AdditionOverlayV1(DomainPartitionStateV1<TPayload> initial)
        {
            _initial = initial ?? throw new ArgumentNullException(nameof(initial));
        }

        public void Add(DomainRecordEnvelopeV1<TPayload> record, string duplicateCode)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (_initial.TryGet(record.RecordId, out _) || !_additions.TryAdd(record.RecordId, record))
                throw new InvalidDataException(duplicateCode);
        }

        public DomainPartitionStateV1<TPayload> Build()
        {
            var additions = _additions.Values
                .OrderBy(static record => record.RecordId)
                .ToArray();
            var merged = MergeCanonical(
                _initial.RecordsCanonical,
                checked((int)_initial.ItemCount),
                additions,
                static record => record.RecordId);
            return DomainPartitionStateV1<TPayload>.FromCanonicalRecords(
                _initial.Identity,
                merged);
        }
    }

    private sealed class RevisionOverlayV1<TPayload>
    {
        private readonly DomainPartitionStateV1<TPayload> _initial;
        private readonly Dictionary<OpaqueId128, DomainRecordEnvelopeV1<TPayload>> _replacements = new();

        public RevisionOverlayV1(DomainPartitionStateV1<TPayload> initial)
        {
            _initial = initial ?? throw new ArgumentNullException(nameof(initial));
        }

        public bool TryGet(OpaqueId128 recordId, out DomainRecordEnvelopeV1<TPayload>? record)
        {
            if (_replacements.TryGetValue(recordId, out var replacement))
            {
                record = replacement;
                return true;
            }

            return _initial.TryGet(recordId, out record);
        }

        public void Replace(DomainRecordEnvelopeV1<TPayload> record, string missingCode)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!_replacements.ContainsKey(record.RecordId) &&
                !_initial.TryGet(record.RecordId, out _))
                throw new InvalidDataException(missingCode);
            _replacements[record.RecordId] = record;
        }

        public DomainPartitionStateV1<TPayload> Build()
        {
            var rebuilt = new DomainRecordEnvelopeV1<TPayload>[checked((int)_initial.ItemCount)];
            var index = 0;
            foreach (var record in _initial.RecordsCanonical)
            {
                rebuilt[index++] = _replacements.TryGetValue(record.RecordId, out var replacement)
                    ? replacement
                    : record;
            }
            if (index != rebuilt.Length)
                throw new InvalidDataException("qa04.mutation.revision-overlay-count-drift");
            return DomainPartitionStateV1<TPayload>.FromCanonicalRecords(
                _initial.Identity,
                rebuilt);
        }
    }

    private sealed class MarketAdditionOverlayV1
    {
        private readonly SocietyMarketTransactionPartitionStateV2 _initial;
        private readonly Dictionary<OpaqueId128, SocietyMarketTransactionRecordMaterialV2> _additions = new();

        public MarketAdditionOverlayV1(SocietyMarketTransactionPartitionStateV2 initial)
        {
            _initial = initial ?? throw new ArgumentNullException(nameof(initial));
        }

        public bool TryGet(OpaqueId128 recordId, out SocietyMarketTransactionRecordMaterialV2? record)
        {
            if (_additions.TryGetValue(recordId, out var added))
            {
                record = added;
                return true;
            }

            return _initial.RecordSet.TryGet(recordId, out record);
        }

        public void Add(SocietyMarketTransactionRecordMaterialV2 record, string duplicateCode)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (_initial.RecordSet.TryGet(record.RecordId, out _) ||
                !_additions.TryAdd(record.RecordId, record))
                throw new InvalidDataException(duplicateCode);
        }

        public SocietyMarketTransactionPartitionStateV2 Build()
        {
            var additions = _additions.Values
                .OrderBy(static record => record.RecordId)
                .ToArray();
            var initialRecords = _initial.RecordSet.RecordsCanonical;
            var mergedRecords = MergeCanonical(
                initialRecords,
                initialRecords.Count,
                additions,
                static record => record.RecordId);

            var additionEnvelopes = additions
                .Select(static record => new DomainRecordEnvelopeV1<SocietyMarketTransactionRecordPayloadV2>(
                    record.RecordId,
                    SocietyMarketTransactionRecordSchemaV2.RecordSchema,
                    record.Revision,
                    record.CreatedStep,
                    record.RetiredStep,
                    record.DetailLevel,
                    record.LineageRef,
                    record.Payload))
                .ToArray();
            var mergedEnvelopes = MergeCanonical(
                _initial.State.RecordsCanonical,
                checked((int)_initial.State.ItemCount),
                additionEnvelopes,
                static record => record.RecordId);

            return SocietyMarketTransactionPartitionStateV2.FromCanonicalMaterial(
                mergedRecords,
                mergedEnvelopes);
        }
    }
}
