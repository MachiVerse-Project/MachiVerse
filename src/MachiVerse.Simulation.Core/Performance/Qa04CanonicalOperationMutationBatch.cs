using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Runtime;
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
    IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> Changes)
{
    public DeterministicCpuBatchObservationV1? CpuParallelism { get; init; }
}

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

        var emptyPhysical = new DomainPartitionStateV1<PhysicalPresencePayloadV1>(
            initialState.PhysicalPresence.Identity,
            Array.Empty<DomainRecordEnvelopeV1<PhysicalPresencePayloadV1>>());
        var emptyMarket = new SocietyMarketTransactionPartitionStateV2(
            Array.Empty<SocietyMarketTransactionRecordMaterialV2>());

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
            if (!binding.OrderKey.CanonicallyEquals(binding.ScheduledOperation.OrderKey))
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
                    var created = Qa04InfrastructureServiceReserveApplicationV1.CreateRecord(
                        worldId,
                        binding,
                        initialState.InfrastructureServiceQueue,
                        references);
                    infrastructure.Add(
                        created,
                        "qa04.infrastructure.service-reserve-duplicate");
                    change = Change(
                        binding,
                        InfrastructureServiceQueuePayloadV1.PartitionId,
                        created.RecordId,
                        created.RecordSchema,
                        created.Revision,
                        created.CreatedStep,
                        created.DetailLevel,
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
                    var created = Qa04GovernanceIncidentApplicationV1.CreateIncident(
                        binding,
                        initialState.GovernanceSecurityIncident,
                        references);
                    governance.Add(
                        created,
                        "qa04.governance.incident-record-id-collision");
                    change = Change(
                        binding,
                        GovernanceSecurityIncidentPayloadV1.PartitionId,
                        created.RecordId,
                        created.RecordSchema,
                        created.Revision,
                        created.CreatedStep,
                        created.DetailLevel,
                        Create);
                    break;
                }
                case EnvironmentFamily:
                {
                    var created = Qa04EnvironmentHazardApplicationV1.CreateHazard(
                        binding,
                        initialState.EnvironmentHazard,
                        references);
                    environment.Add(
                        created,
                        "qa04.environment.hazard-record-id-collision");
                    change = Change(
                        binding,
                        EnvironmentHazardPayloadV1.PartitionId,
                        created.RecordId,
                        created.RecordSchema,
                        created.Revision,
                        created.CreatedStep,
                        created.DetailLevel,
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


    public static async Task<Qa04CanonicalOperationMutationBatchResultV1> ApplyParallelAsync(
        OpaqueId128 worldId,
        ulong effectiveStep,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationStateV1 initialState,
        IDomainRecordSchemaResolverV1 references,
        int workerCount,
        CancellationToken cancellationToken = default)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        if (worldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.full-step.mutation-world-id-drift");
        if (effectiveStep == 0) throw new ArgumentOutOfRangeException(nameof(effectiveStep));
        ArgumentNullException.ThrowIfNull(orderedBindings);
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(references);
        if (workerCount < 1) throw new ArgumentOutOfRangeException(nameof(workerCount));
        if (orderedBindings.Count == 0)
            throw new InvalidDataException("qa04.full-step.mutation-batch-empty");

        ValidateStateIdentities(initialState);

        var familyOrder = new[]
        {
            InfrastructureFamily,
            ResidentFamily,
            PhysicalFamily,
            MarketFamily,
            GovernanceFamily,
            EnvironmentFamily,
        };
        var familyBindings = familyOrder.ToDictionary(
            static family => family,
            static _ => new List<Qa04CanonicalOperationBindingResultV1>(),
            StringComparer.Ordinal);
        var appliedIds = new OpaqueId128[orderedBindings.Count];
        var operationIndex = new Dictionary<OpaqueId128, int>(orderedBindings.Count);
        var counts = new Dictionary<string, ulong>(StringComparer.Ordinal);
        SameStepOrderKey? previousOrderKey = null;
        for (var index = 0; index < orderedBindings.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = orderedBindings[index]
                ?? throw new InvalidDataException("qa04.full-step.mutation-binding-null");
            if (binding.SourceDescriptor is null || binding.ScheduledOperation is null || binding.OrderKey is null)
                throw new InvalidDataException("qa04.full-step.mutation-binding-null");
            if (binding.ScheduledOperation.EffectiveStep != effectiveStep)
                throw new InvalidDataException("qa04.full-step.mutation-effective-step-drift");
            if (!binding.OrderKey.CanonicallyEquals(binding.ScheduledOperation.OrderKey))
                throw new InvalidDataException("qa04.full-step.mutation-order-key-drift");
            if (previousOrderKey is not null && previousOrderKey.CompareTo(binding.OrderKey) >= 0)
                throw new InvalidDataException("qa04.full-step.mutation-order-not-canonical");
            previousOrderKey = binding.OrderKey;

            var operationId = binding.SourceDescriptor.OperationId;
            if (operationId.IsZero || !operationIndex.TryAdd(operationId, index))
                throw new InvalidDataException("qa04.full-step.mutation-operation-id-duplicate");
            appliedIds[index] = operationId;

            var family = binding.SourceDescriptor.FamilyToken.Value;
            if (!familyBindings.TryGetValue(family, out var bucket))
                throw new InvalidDataException($"qa04.full-step.mutation-family-unregistered:{family}");
            bucket.Add(binding);
            counts[family] = checked(counts.GetValueOrDefault(family) + 1UL);
        }

        var work = familyOrder
            .Select(family => new FamilyMutationWorkV1(
                family,
                (IReadOnlyList<Qa04CanonicalOperationBindingResultV1>)Array.AsReadOnly(
                    familyBindings[family].ToArray())))
            .ToArray();
        if (work.Any(static item => item.Bindings.Count == 0))
            throw new InvalidDataException("qa04.full-step.mutation-family-coverage-drift");

        var batch = await DeterministicBatchExecutor.RunCpuBoundAsync(
            work,
            workerCount,
            (item, token) => ApplyFamily(
                worldId,
                initialState,
                references,
                item,
                token),
            cancellationToken).ConfigureAwait(false);

        var byFamily = batch.Outputs.ToDictionary(static result => result.Family, StringComparer.Ordinal);
        if (byFamily.Count != familyOrder.Length || familyOrder.Any(family => !byFamily.ContainsKey(family)))
            throw new InvalidDataException("qa04.full-step.mutation-family-result-drift");

        var state = new Qa04CanonicalOperationMutationStateV1(
            byFamily[InfrastructureFamily].InfrastructureServiceQueue
                ?? throw new InvalidDataException("qa04.full-step.mutation-infrastructure-result-missing"),
            initialState.ParticipationControlMode,
            byFamily[ResidentFamily].ResidentBehaviorState
                ?? throw new InvalidDataException("qa04.full-step.mutation-resident-result-missing"),
            byFamily[PhysicalFamily].PhysicalPresence
                ?? throw new InvalidDataException("qa04.full-step.mutation-physical-result-missing"),
            byFamily[MarketFamily].MarketTransaction
                ?? throw new InvalidDataException("qa04.full-step.mutation-market-result-missing"),
            byFamily[GovernanceFamily].GovernanceSecurityIncident
                ?? throw new InvalidDataException("qa04.full-step.mutation-governance-result-missing"),
            byFamily[EnvironmentFamily].EnvironmentHazard
                ?? throw new InvalidDataException("qa04.full-step.mutation-environment-result-missing"));
        ValidateStateIdentities(state);

        var changes = new Qa04CanonicalOperationMutationChangeV1[orderedBindings.Count];
        var assignedChanges = 0;
        foreach (var result in batch.Outputs)
        {
            foreach (var change in result.Changes)
            {
                if (!operationIndex.TryGetValue(change.OperationId, out var index) ||
                    changes[index] is not null)
                {
                    throw new InvalidDataException("qa04.full-step.mutation-change-coverage-drift");
                }

                changes[index] = change;
                assignedChanges++;
            }
        }
        if (assignedChanges != orderedBindings.Count || changes.Any(static change => change is null))
            throw new InvalidDataException("qa04.full-step.mutation-change-coverage-drift");

        return new Qa04CanonicalOperationMutationBatchResultV1(
            effectiveStep,
            state,
            Array.AsReadOnly(appliedIds),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, ulong>(counts),
            Array.AsReadOnly(changes))
        {
            CpuParallelism = batch.Observation,
        };
    }

    private static FamilyMutationResultV1 ApplyFamily(
        OpaqueId128 worldId,
        Qa04CanonicalOperationMutationStateV1 initialState,
        IDomainRecordSchemaResolverV1 references,
        FamilyMutationWorkV1 work,
        CancellationToken cancellationToken)
    {
        var changes = new List<Qa04CanonicalOperationMutationChangeV1>(work.Bindings.Count);

        switch (work.Family)
        {
            case InfrastructureFamily:
            {
                var overlay = new AdditionOverlayV1<InfrastructureServiceQueuePayloadV1>(
                    initialState.InfrastructureServiceQueue);
                foreach (var binding in work.Bindings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var created = Qa04InfrastructureServiceReserveApplicationV1.CreateRecord(
                        worldId,
                        binding,
                        initialState.InfrastructureServiceQueue,
                        references);
                    overlay.Add(created, "qa04.infrastructure.service-reserve-duplicate");
                    changes.Add(Change(
                        binding,
                        InfrastructureServiceQueuePayloadV1.PartitionId,
                        created.RecordId,
                        created.RecordSchema,
                        created.Revision,
                        created.CreatedStep,
                        created.DetailLevel,
                        Create));
                }
                return new FamilyMutationResultV1(
                    work.Family,
                    InfrastructureServiceQueue: overlay.Build(),
                    FamilyChanges: Array.AsReadOnly(changes.ToArray()));
            }
            case ResidentFamily:
            {
                var overlay = new ResidentBehaviorOverlayV1(initialState.ResidentBehaviorState);
                foreach (var binding in work.Bindings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var controlModeId = Qa04ParticipationControlModeCanonicalAuthorityV1.RecordId(
                        binding.SourceDescriptor.FamilyOrdinal);
                    if (!initialState.ParticipationControlMode.TryGet(controlModeId, out var controlMode) ||
                        controlMode is null)
                        throw new InvalidDataException("qa04.full-step.mutation-control-mode-missing");

                    _ = overlay.TryGet(binding.PrimaryTarget, out var existing);
                    var result = Qa04ResidentActionApplicationV1.ApplyRecord(
                        worldId,
                        binding,
                        existing,
                        initialState.ResidentBehaviorState,
                        controlMode,
                        references);
                    overlay.Apply(binding.PrimaryTarget, result);
                    changes.Add(Change(
                        binding,
                        ResidentBehaviorStatePayloadV1.PartitionId,
                        result.AppliedRecord.RecordId,
                        result.AppliedRecord.RecordSchema,
                        result.AppliedRecord.Revision,
                        result.AppliedRecord.CreatedStep,
                        result.AppliedRecord.DetailLevel,
                        result.Created ? Create : Revise));
                }
                return new FamilyMutationResultV1(
                    work.Family,
                    ResidentBehaviorState: overlay.Build(),
                    FamilyChanges: Array.AsReadOnly(changes.ToArray()));
            }
            case PhysicalFamily:
            {
                var overlay = new RevisionOverlayV1<PhysicalPresencePayloadV1>(initialState.PhysicalPresence);
                foreach (var binding in work.Bindings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!overlay.TryGet(binding.PrimaryTarget.RecordId, out var target) || target is null)
                        throw new InvalidDataException("qa04.physical.move-target-missing");
                    var applied = Qa04PhysicalMoveApplicationV1.ApplyRecord(binding, target, references);
                    overlay.Replace(applied, "qa04.physical.move-target-missing");
                    changes.Add(Change(
                        binding,
                        PhysicalPresencePayloadV1.PartitionId,
                        applied.RecordId,
                        applied.RecordSchema,
                        applied.Revision,
                        applied.CreatedStep,
                        applied.DetailLevel,
                        Revise));
                }
                return new FamilyMutationResultV1(
                    work.Family,
                    PhysicalPresence: overlay.Build(),
                    FamilyChanges: Array.AsReadOnly(changes.ToArray()));
            }
            case MarketFamily:
            {
                var overlay = new MarketAdditionOverlayV1(initialState.MarketTransaction);
                foreach (var binding in work.Bindings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!overlay.TryGet(binding.PrimaryTarget.RecordId, out var target) || target is null)
                        throw new InvalidDataException("qa04.market.order-target-missing");
                    var created = Qa04MarketOrderApplicationV1.CreateOrderRecord(binding, target, references);
                    overlay.Add(created, "qa04.market.order-record-id-collision");
                    changes.Add(Change(
                        binding,
                        SocietyMarketTransactionRecordSchemaV2.PartitionId,
                        created.RecordId,
                        created.RecordSchema,
                        created.Revision,
                        created.CreatedStep,
                        created.DetailLevel,
                        Create));
                }
                return new FamilyMutationResultV1(
                    work.Family,
                    MarketTransaction: overlay.Build(),
                    FamilyChanges: Array.AsReadOnly(changes.ToArray()));
            }
            case GovernanceFamily:
            {
                var overlay = new AdditionOverlayV1<GovernanceSecurityIncidentPayloadV1>(
                    initialState.GovernanceSecurityIncident);
                foreach (var binding in work.Bindings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var created = Qa04GovernanceIncidentApplicationV1.CreateIncident(
                        binding,
                        initialState.GovernanceSecurityIncident,
                        references);
                    overlay.Add(created, "qa04.governance.incident-record-id-collision");
                    changes.Add(Change(
                        binding,
                        GovernanceSecurityIncidentPayloadV1.PartitionId,
                        created.RecordId,
                        created.RecordSchema,
                        created.Revision,
                        created.CreatedStep,
                        created.DetailLevel,
                        Create));
                }
                return new FamilyMutationResultV1(
                    work.Family,
                    GovernanceSecurityIncident: overlay.Build(),
                    FamilyChanges: Array.AsReadOnly(changes.ToArray()));
            }
            case EnvironmentFamily:
            {
                var overlay = new AdditionOverlayV1<EnvironmentHazardPayloadV1>(initialState.EnvironmentHazard);
                foreach (var binding in work.Bindings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var created = Qa04EnvironmentHazardApplicationV1.CreateHazard(
                        binding,
                        initialState.EnvironmentHazard,
                        references);
                    overlay.Add(created, "qa04.environment.hazard-record-id-collision");
                    changes.Add(Change(
                        binding,
                        EnvironmentHazardPayloadV1.PartitionId,
                        created.RecordId,
                        created.RecordSchema,
                        created.Revision,
                        created.CreatedStep,
                        created.DetailLevel,
                        Create));
                }
                return new FamilyMutationResultV1(
                    work.Family,
                    EnvironmentHazard: overlay.Build(),
                    FamilyChanges: Array.AsReadOnly(changes.ToArray()));
            }
            default:
                throw new InvalidDataException($"qa04.full-step.mutation-family-unregistered:{work.Family}");
        }
    }

    private sealed record FamilyMutationWorkV1(
        string Family,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> Bindings);

    private sealed record FamilyMutationResultV1(
        string Family,
        DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1>? InfrastructureServiceQueue = null,
        DomainPartitionStateV1<ResidentBehaviorStatePayloadV1>? ResidentBehaviorState = null,
        DomainPartitionStateV1<PhysicalPresencePayloadV1>? PhysicalPresence = null,
        SocietyMarketTransactionPartitionStateV2? MarketTransaction = null,
        DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1>? GovernanceSecurityIncident = null,
        DomainPartitionStateV1<EnvironmentHazardPayloadV1>? EnvironmentHazard = null,
        IReadOnlyList<Qa04CanonicalOperationMutationChangeV1>? FamilyChanges = null)
    {
        public IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> Changes { get; } =
            FamilyChanges ?? Array.Empty<Qa04CanonicalOperationMutationChangeV1>();
    }

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
            => _initial.WithAdditions(
                _additions.Values.OrderBy(static record => record.RecordId),
                "qa04.mutation.addition-record-id-collision");
    }

    private sealed class ResidentBehaviorOverlayV1
    {
        private static readonly ConditionalWeakTable<
            DomainPartitionStateV1<ResidentBehaviorStatePayloadV1>,
            ResidentBehaviorIndexV1> IndexByState = new();

        private readonly DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> _initial;
        private ResidentBehaviorIndexV1 _residentIndex;
        private readonly Dictionary<OpaqueId128, DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>> _replacements = new();
        private readonly Dictionary<OpaqueId128, DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>> _additions = new();

        public ResidentBehaviorOverlayV1(
            DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> initial)
        {
            _initial = initial ?? throw new ArgumentNullException(nameof(initial));
            _residentIndex = IndexByState.GetValue(initial, static state => ResidentBehaviorIndexV1.Build(state));
        }

        public bool TryGet(
            PartitionRecordRefV1 residentRef,
            out DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>? record)
        {
            if (!_residentIndex.TryGet(residentRef, out var entry))
            {
                record = null;
                return false;
            }

            if (entry.Duplicate)
                throw new InvalidDataException("qa04.resident.action-duplicate-resident-behavior");

            record = entry.Record
                ?? throw new InvalidDataException("qa04.full-step.mutation-resident-index-null");
            return true;
        }

        public void Apply(
            PartitionRecordRefV1 residentRef,
            Qa04ResidentActionRecordResultV1 result)
        {
            ArgumentNullException.ThrowIfNull(result);
            var record = result.AppliedRecord
                ?? throw new InvalidDataException("qa04.full-step.mutation-resident-result-missing");
            if (record.Payload.ResidentRef != residentRef)
                throw new InvalidDataException("qa04.full-step.mutation-resident-result-drift");

            if (result.Created)
            {
                if (_residentIndex.TryGet(residentRef, out var existingByResident))
                {
                    if (existingByResident.Duplicate)
                        throw new InvalidDataException("qa04.resident.action-duplicate-resident-behavior");
                    throw new InvalidDataException("qa04.resident.action-record-id-collision");
                }

                if (_initial.TryGet(record.RecordId, out _) ||
                    _replacements.ContainsKey(record.RecordId) ||
                    !_additions.TryAdd(record.RecordId, record))
                {
                    throw new InvalidDataException("qa04.resident.action-record-id-collision");
                }

                _residentIndex = _residentIndex.Upsert(residentRef, record);
                return;
            }

            if (!_residentIndex.TryGet(residentRef, out var previousEntry) ||
                previousEntry.Duplicate ||
                previousEntry.Record is null ||
                previousEntry.Record.RecordId != record.RecordId ||
                _additions.ContainsKey(record.RecordId))
            {
                throw new InvalidDataException("qa04.full-step.mutation-resident-result-drift");
            }

            _replacements[record.RecordId] = record;
            _residentIndex = _residentIndex.Upsert(residentRef, record);
        }

        public DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> Build()
        {
            var replaced = _initial.WithReplacements(
                _replacements.Values.OrderBy(static record => record.RecordId),
                "qa04.mutation.resident-overlay-replacement-missing");
            var next = replaced.WithAdditions(
                _additions.Values.OrderBy(static record => record.RecordId),
                "qa04.resident.action-record-id-collision");

            IndexByState.Add(next, _residentIndex);
            return next;
        }

        private sealed record ResidentBehaviorIndexEntryV1(
            PartitionRecordRefV1 ResidentRef,
            DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>? Record,
            bool Duplicate);

        private sealed class ResidentBehaviorIndexV1
        {
            private readonly Node? _root;

            private ResidentBehaviorIndexV1(Node? root)
                => _root = root;

            public static ResidentBehaviorIndexV1 Build(
                DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> state)
            {
                ArgumentNullException.ThrowIfNull(state);
                var byResident = new SortedDictionary<PartitionRecordRefV1, ResidentBehaviorIndexEntryV1>(
                    ResidentRefComparer.Instance);

                foreach (var record in state.RecordsCanonical)
                {
                    var residentRef = record.Payload.ResidentRef;
                    if (byResident.TryGetValue(residentRef, out var previous))
                    {
                        byResident[residentRef] = previous with
                        {
                            Record = null,
                            Duplicate = true,
                        };
                    }
                    else
                    {
                        byResident.Add(
                            residentRef,
                            new ResidentBehaviorIndexEntryV1(residentRef, record, Duplicate: false));
                    }
                }

                var canonical = byResident.Values.ToArray();
                return new ResidentBehaviorIndexV1(
                    BuildBalanced(canonical, 0, canonical.Length));
            }

            public bool TryGet(
                PartitionRecordRefV1 residentRef,
                out ResidentBehaviorIndexEntryV1 entry)
            {
                var node = _root;
                while (node is not null)
                {
                    var comparison = ResidentRefComparer.Instance.Compare(residentRef, node.Entry.ResidentRef);
                    if (comparison == 0)
                    {
                        entry = node.Entry;
                        return true;
                    }

                    node = comparison < 0 ? node.Left : node.Right;
                }

                entry = default!;
                return false;
            }

            public ResidentBehaviorIndexV1 Upsert(
                PartitionRecordRefV1 residentRef,
                DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1> record)
            {
                ArgumentNullException.ThrowIfNull(record);
                if (record.Payload.ResidentRef != residentRef)
                    throw new InvalidDataException("qa04.full-step.mutation-resident-index-drift");

                return new ResidentBehaviorIndexV1(
                    Set(
                        _root,
                        new ResidentBehaviorIndexEntryV1(residentRef, record, Duplicate: false)));
            }

            private static Node? BuildBalanced(
                IReadOnlyList<ResidentBehaviorIndexEntryV1> entries,
                int start,
                int length)
            {
                if (length == 0)
                    return null;

                var leftLength = length >> 1;
                var middle = start + leftLength;
                return new Node(
                    entries[middle],
                    BuildBalanced(entries, start, leftLength),
                    BuildBalanced(entries, middle + 1, length - leftLength - 1));
            }

            private static Node Set(
                Node? node,
                ResidentBehaviorIndexEntryV1 entry)
            {
                if (node is null)
                    return new Node(entry, null, null);

                var comparison = ResidentRefComparer.Instance.Compare(
                    entry.ResidentRef,
                    node.Entry.ResidentRef);
                if (comparison == 0)
                {
                    if (node.Entry == entry)
                        return node;
                    return new Node(entry, node.Left, node.Right);
                }

                if (comparison < 0)
                {
                    var left = Set(node.Left, entry);
                    if (ReferenceEquals(left, node.Left))
                        return node;
                    return Balance(new Node(node.Entry, left, node.Right));
                }

                var right = Set(node.Right, entry);
                if (ReferenceEquals(right, node.Right))
                    return node;
                return Balance(new Node(node.Entry, node.Left, right));
            }

            private static Node Balance(Node node)
            {
                var balance = Height(node.Left) - Height(node.Right);
                if (balance > 1)
                {
                    if (Height(node.Left!.Left) < Height(node.Left.Right))
                        return RotateRight(new Node(node.Entry, RotateLeft(node.Left), node.Right));
                    return RotateRight(node);
                }

                if (balance < -1)
                {
                    if (Height(node.Right!.Right) < Height(node.Right.Left))
                        return RotateLeft(new Node(node.Entry, node.Left, RotateRight(node.Right)));
                    return RotateLeft(node);
                }

                return node;
            }

            private static Node RotateLeft(Node node)
            {
                var pivot = node.Right
                    ?? throw new InvalidOperationException("qa04.resident-index.rotate-left");
                var moved = new Node(node.Entry, node.Left, pivot.Left);
                return new Node(pivot.Entry, moved, pivot.Right);
            }

            private static Node RotateRight(Node node)
            {
                var pivot = node.Left
                    ?? throw new InvalidOperationException("qa04.resident-index.rotate-right");
                var moved = new Node(node.Entry, pivot.Right, node.Right);
                return new Node(pivot.Entry, pivot.Left, moved);
            }

            private static int Height(Node? node) => node?.Height ?? 0;

            private sealed class Node
            {
                public Node(
                    ResidentBehaviorIndexEntryV1 entry,
                    Node? left,
                    Node? right)
                {
                    Entry = entry;
                    Left = left;
                    Right = right;
                    Height = checked(1 + Math.Max(ResidentBehaviorIndexV1.Height(left), ResidentBehaviorIndexV1.Height(right)));
                }

                public ResidentBehaviorIndexEntryV1 Entry { get; }
                public Node? Left { get; }
                public Node? Right { get; }
                public int Height { get; }
            }

            private sealed class ResidentRefComparer : IComparer<PartitionRecordRefV1>
            {
                public static ResidentRefComparer Instance { get; } = new();

                public int Compare(PartitionRecordRefV1 left, PartitionRecordRefV1 right)
                {
                    var partition = string.CompareOrdinal(
                        left.PartitionId.Value,
                        right.PartitionId.Value);
                    return partition != 0
                        ? partition
                        : left.RecordId.CompareTo(right.RecordId);
                }
            }
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
            => _initial.WithReplacements(
                _replacements.Values.OrderBy(static record => record.RecordId),
                "qa04.mutation.revision-overlay-target-missing");
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
            => _initial.WithAdditions(
                _additions.Values.OrderBy(static record => record.RecordId).ToArray(),
                "qa04.market.order-record-id-collision");
    }
}
