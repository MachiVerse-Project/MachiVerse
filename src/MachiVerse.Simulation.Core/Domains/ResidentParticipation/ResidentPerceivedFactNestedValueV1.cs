using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.ResidentParticipation;

/// <summary>
/// Exact nested persistence value for resident.perception/perceived_facts.
/// The field set is a lossless projection of ResidentPerceptionObservationV1; the parent
/// ResidentPerceptionPayloadV1 still owns the collection itself.
/// </summary>
public sealed record ResidentPerceivedFactNestedValueV1(
    OpaqueId128 FactId,
    OpaqueId128 ResidentId,
    OpaqueId128 SubjectId,
    StableToken Proposition,
    OpaqueId128 SourceDeliveryId,
    uint ConfidencePpm,
    ulong PerceivedStep) : ICanonicalDomainNestedValueV1
{
    public void ValidateCanonical()
    {
        ToObservation().Validate();
    }

    public ResidentPerceptionObservationV1 ToObservation()
        => new(
            FactId,
            ResidentId,
            SubjectId,
            Proposition,
            SourceDeliveryId,
            ConfidencePpm,
            PerceivedStep);

    public static ResidentPerceivedFactNestedValueV1 FromObservation(
        ResidentPerceptionObservationV1 observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        observation.Validate();
        return new ResidentPerceivedFactNestedValueV1(
            observation.ObservationId,
            observation.ResidentId,
            observation.SubjectRef,
            observation.Proposition,
            observation.SourceDeliveryId,
            observation.ConfidencePpm,
            observation.PerceivedStep);
    }
}
