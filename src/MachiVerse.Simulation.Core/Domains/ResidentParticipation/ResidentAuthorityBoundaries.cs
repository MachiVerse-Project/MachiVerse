using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Domains.ResidentParticipation;

public sealed record ResidentInformationDeliveryV1(
    OpaqueId128 DeliveryId,
    OpaqueId128 ResidentId,
    OpaqueId128 SubjectRef,
    StableToken Proposition,
    ulong DeliveredStep)
{
    public void Validate()
    {
        if (DeliveryId.IsZero || ResidentId.IsZero || SubjectRef.IsZero)
            throw new InvalidDataException("resident.delivery-id-zero");
    }
}

public sealed record ResidentPerceptionObservationV1(
    OpaqueId128 ObservationId,
    OpaqueId128 ResidentId,
    OpaqueId128 SubjectRef,
    StableToken Proposition,
    OpaqueId128 SourceDeliveryId,
    uint ConfidencePpm,
    ulong PerceivedStep)
{
    public void Validate()
    {
        if (ObservationId.IsZero || ResidentId.IsZero || SubjectRef.IsZero || SourceDeliveryId.IsZero)
            throw new InvalidDataException("resident.perception-id-zero");
        if (ConfidencePpm > ResidentPpmV1.Scale)
            throw new InvalidDataException("resident.perception-confidence-range");
    }
}

public sealed record ResidentBeliefEntryV1(
    OpaqueId128 ResidentId,
    OpaqueId128 SubjectRef,
    StableToken Proposition,
    uint ConfidencePpm,
    OpaqueId128 EvidenceObservationId,
    ulong LastUpdatedStep);

public sealed class ResidentCognitionProjectionV1
{
    private readonly SortedDictionary<string, ResidentBeliefEntryV1> _beliefs = new(StringComparer.Ordinal);
    private readonly SortedDictionary<OpaqueId128, ResidentInformationDeliveryV1> _deliveries = new();

    public IReadOnlyList<ResidentBeliefEntryV1> Beliefs => Array.AsReadOnly(_beliefs.Values.ToArray());

    public void ReceiveDelivery(ResidentInformationDeliveryV1 delivery)
    {
        delivery.Validate();
        if (!_deliveries.TryAdd(delivery.DeliveryId, delivery))
            throw new InvalidDataException("resident.delivery-duplicate");
    }

    public ResidentPerceptionObservationV1 PerceiveDelivery(
        OpaqueId128 deliveryId,
        OpaqueId128 observationId,
        uint confidencePpm,
        ulong perceivedStep)
    {
        if (!_deliveries.TryGetValue(deliveryId, out var delivery))
            throw new InvalidDataException("resident.perception-delivery-missing");
        if (perceivedStep < delivery.DeliveredStep)
            throw new InvalidDataException("resident.perception-before-delivery");
        var observation = new ResidentPerceptionObservationV1(
            observationId,
            delivery.ResidentId,
            delivery.SubjectRef,
            delivery.Proposition,
            delivery.DeliveryId,
            confidencePpm,
            perceivedStep);
        observation.Validate();
        return observation;
    }

    public void ApplyPerception(ResidentPerceptionObservationV1 observation)
    {
        observation.Validate();
        var key = observation.ResidentId + "/" + observation.SubjectRef + "/" + observation.Proposition.Value;
        _beliefs[key] = new ResidentBeliefEntryV1(
            observation.ResidentId,
            observation.SubjectRef,
            observation.Proposition,
            observation.ConfidencePpm,
            observation.ObservationId,
            observation.PerceivedStep);
    }
}

public sealed record ResidentPhysicalActionDecisionV1(
    OpaqueId128 DecisionId,
    OpaqueId128 ResidentId,
    ulong BasisStep,
    StableToken ActionKind,
    byte[] SemanticPayloadDigest)
{
    public void Validate()
    {
        if (DecisionId.IsZero || ResidentId.IsZero)
            throw new InvalidDataException("resident.action-decision-id-zero");
        ArgumentNullException.ThrowIfNull(SemanticPayloadDigest);
        if (SemanticPayloadDigest.Length != 32)
            throw new InvalidDataException("resident.action-decision-digest-width");
    }
}

public static class ResidentPhysicalIntentFactoryV1
{
    private static readonly StableToken ResidentDomain = new("resident");
    private static readonly StableToken PhysicalDomain = new("physical_built");
    private static readonly StableToken PresencePartition = new("physical.presence");
    private static readonly StableToken MoveIntentKind = new("physical.intent.move");

    public static MutationIntentCandidateV1 CreateMoveIntent(
        ResidentPhysicalActionDecisionV1 decision,
        byte phase,
        ConflictScopeV1 physicalScope,
        int semanticPriority)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(physicalScope);
        decision.Validate();
        if (decision.ActionKind != MoveIntentKind)
            throw new InvalidDataException("resident.physical-action-kind-invalid");
        if (physicalScope.Domain != PhysicalDomain || physicalScope.TargetKind != PresencePartition)
            throw new InvalidDataException("resident.physical-scope-mismatch");

        return new MutationIntentCandidateV1(
            decision.DecisionId,
            phase,
            ResidentDomain,
            PhysicalDomain,
            PresencePartition,
            decision.BasisStep,
            MoveIntentKind,
            physicalScope,
            semanticPriority,
            ConflictResolutionModeV1.CustomDeterministic,
            decision.SemanticPayloadDigest);
    }
}
