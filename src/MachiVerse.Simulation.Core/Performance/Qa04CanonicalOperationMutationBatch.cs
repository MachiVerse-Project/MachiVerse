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

public sealed record Qa04CanonicalOperationMutationBatchResultV1(
    ulong EffectiveStep,
    Qa04CanonicalOperationMutationStateV1 State,
    IReadOnlyList<OpaqueId128> AppliedOperationIds,
    IReadOnlyDictionary<string, ulong> AppliedCountByFamily);

/// <summary>
/// Gate-2 integration stage that applies already-authoritative perf.reference.v1 Operations to the
/// six typed mutation targets in canonical scheduler order. This composes the six Gate-1 handlers;
/// it does not itself claim full authoritative-Step availability, build partition candidates, or
/// cross the SQLite COMMIT/publish boundary.
/// </summary>
public static class Qa04CanonicalOperationMutationBatchV1
{
    private const string InfrastructureFamily = "infrastructure-service-delivery";
    private const string ResidentFamily = "participation-control-resident-action";
    private const string PhysicalFamily = "physical-item-movement-work";
    private const string MarketFamily = "society-market-payment-contract";
    private const string GovernanceFamily = "governance-security";
    private const string EnvironmentFamily = "environment-spatial-admin-synthetic";

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

        var state = initialState;
        var appliedIds = new List<OpaqueId128>(orderedBindings.Count);
        var counts = new Dictionary<string, ulong>(StringComparer.Ordinal);
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
            if (operationId.IsZero || appliedIds.Contains(operationId))
                throw new InvalidDataException("qa04.full-step.mutation-operation-id-duplicate");

            state = binding.SourceDescriptor.FamilyToken.Value switch
            {
                InfrastructureFamily => ApplyInfrastructure(worldId, binding, state, references),
                ResidentFamily => ApplyResident(worldId, binding, state, references),
                PhysicalFamily => ApplyPhysical(binding, state, references),
                MarketFamily => ApplyMarket(binding, state, references),
                GovernanceFamily => ApplyGovernance(binding, state, references),
                EnvironmentFamily => ApplyEnvironment(binding, state, references),
                _ => throw new InvalidDataException(
                    $"qa04.full-step.mutation-family-unregistered:{binding.SourceDescriptor.FamilyToken.Value}"),
            };

            appliedIds.Add(operationId);
            counts[binding.SourceDescriptor.FamilyToken.Value] = checked(
                counts.GetValueOrDefault(binding.SourceDescriptor.FamilyToken.Value) + 1UL);
        }

        return new Qa04CanonicalOperationMutationBatchResultV1(
            effectiveStep,
            state,
            Array.AsReadOnly(appliedIds.ToArray()),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, ulong>(counts));
    }

    private static Qa04CanonicalOperationMutationStateV1 ApplyInfrastructure(
        OpaqueId128 worldId,
        Qa04CanonicalOperationBindingResultV1 binding,
        Qa04CanonicalOperationMutationStateV1 state,
        IDomainRecordSchemaResolverV1 references)
    {
        var result = Qa04InfrastructureServiceReserveApplicationV1.Apply(
            worldId,
            binding,
            state.InfrastructureServiceQueue,
            references);
        return state with { InfrastructureServiceQueue = result.ServiceQueue };
    }

    private static Qa04CanonicalOperationMutationStateV1 ApplyResident(
        OpaqueId128 worldId,
        Qa04CanonicalOperationBindingResultV1 binding,
        Qa04CanonicalOperationMutationStateV1 state,
        IDomainRecordSchemaResolverV1 references)
    {
        var controlModeId = Qa04ParticipationControlModeCanonicalAuthorityV1.RecordId(
            binding.SourceDescriptor.FamilyOrdinal);
        if (!state.ParticipationControlMode.TryGet(controlModeId, out var controlMode) || controlMode is null)
            throw new InvalidDataException("qa04.full-step.mutation-control-mode-missing");

        var result = Qa04ResidentActionApplicationV1.Apply(
            worldId,
            binding,
            state.ResidentBehaviorState,
            controlMode,
            references);
        return state with { ResidentBehaviorState = result.BehaviorState };
    }

    private static Qa04CanonicalOperationMutationStateV1 ApplyPhysical(
        Qa04CanonicalOperationBindingResultV1 binding,
        Qa04CanonicalOperationMutationStateV1 state,
        IDomainRecordSchemaResolverV1 references)
    {
        var result = Qa04PhysicalMoveApplicationV1.Apply(binding, state.PhysicalPresence, references);
        return state with { PhysicalPresence = result.PresenceState };
    }

    private static Qa04CanonicalOperationMutationStateV1 ApplyMarket(
        Qa04CanonicalOperationBindingResultV1 binding,
        Qa04CanonicalOperationMutationStateV1 state,
        IDomainRecordSchemaResolverV1 references)
    {
        var result = Qa04MarketOrderApplicationV1.Apply(binding, state.MarketTransaction, references);
        return state with { MarketTransaction = result.MarketState };
    }

    private static Qa04CanonicalOperationMutationStateV1 ApplyGovernance(
        Qa04CanonicalOperationBindingResultV1 binding,
        Qa04CanonicalOperationMutationStateV1 state,
        IDomainRecordSchemaResolverV1 references)
    {
        var result = Qa04GovernanceIncidentApplicationV1.Apply(
            binding,
            state.GovernanceSecurityIncident,
            references);
        return state with { GovernanceSecurityIncident = result.IncidentState };
    }

    private static Qa04CanonicalOperationMutationStateV1 ApplyEnvironment(
        Qa04CanonicalOperationBindingResultV1 binding,
        Qa04CanonicalOperationMutationStateV1 state,
        IDomainRecordSchemaResolverV1 references)
    {
        var result = Qa04EnvironmentHazardApplicationV1.Apply(binding, state.EnvironmentHazard, references);
        return state with { EnvironmentHazard = result.HazardState };
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
}
