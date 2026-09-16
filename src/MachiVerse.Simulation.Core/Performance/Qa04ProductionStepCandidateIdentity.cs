using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionStepCandidateIdentityV1(
    OpaqueId128 WorldId,
    ulong BasisStep,
    ulong TargetStep,
    OpaqueId128 CandidateId);

/// <summary>
/// Canonical perf.reference.v1 StepCandidate identity. This is deliberately QA-04 scoped and does
/// not define a general CandidateId policy for arbitrary Simulation Core callers.
/// </summary>
public static class Qa04ProductionStepCandidateIdentityDerivationV1
{
    private const string DomainLabel = "mv.qa04-step-candidate.v1";

    public static Qa04ProductionStepCandidateIdentityV1 Derive(
        OpaqueId128 worldId,
        ulong basisStep)
    {
        if (worldId.IsZero)
            throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        if (basisStep == ulong.MaxValue)
            throw new InvalidDataException("qa04.production-candidate.step-overflow");

        var targetStep = checked(basisStep + 1UL);
        for (ulong nonce = 0; ; nonce++)
        {
            var candidateId = HashSuite.Trunc128(HashSuite.DomainHash(DomainLabel, writer =>
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteUnsigned(basisStep);
                writer.WriteUnsigned(2); writer.WriteUnsigned(targetStep);
                writer.WriteUnsigned(3); writer.WriteUnsigned(nonce);
            }));

            if (!candidateId.IsZero)
                return new Qa04ProductionStepCandidateIdentityV1(
                    worldId,
                    basisStep,
                    targetStep,
                    candidateId);

            if (nonce == ulong.MaxValue)
                throw new InvalidDataException("qa04.production-candidate.zero-derivation-exhausted");
        }
    }
}

/// <summary>
/// Run-local fail-closed collision guard for the canonical QA-04 CandidateId sequence. Re-observing
/// the exact same tuple is permitted for deterministic retry/replay; binding one CandidateId to a
/// different tuple is never repaired with run-local entropy.
/// </summary>
public sealed class Qa04ProductionStepCandidateIdentityRegistryV1
{
    private readonly Dictionary<OpaqueId128, Qa04ProductionStepCandidateIdentityV1> _byCandidateId = new();
    private readonly Dictionary<ulong, Qa04ProductionStepCandidateIdentityV1> _byBasisStep = new();

    public int Count => _byBasisStep.Count;

    public Qa04ProductionStepCandidateIdentityV1 DeriveAndRegister(
        OpaqueId128 worldId,
        ulong basisStep)
    {
        var material = Qa04ProductionStepCandidateIdentityDerivationV1.Derive(worldId, basisStep);

        if (_byCandidateId.TryGetValue(material.CandidateId, out var existingById))
        {
            if (existingById.WorldId != material.WorldId ||
                existingById.BasisStep != material.BasisStep ||
                existingById.TargetStep != material.TargetStep)
                throw new InvalidDataException("qa04.production-candidate.collision");
        }
        else
        {
            _byCandidateId.Add(material.CandidateId, material);
        }

        if (_byBasisStep.TryGetValue(material.BasisStep, out var existingByStep))
        {
            if (existingByStep.WorldId != material.WorldId ||
                existingByStep.TargetStep != material.TargetStep ||
                existingByStep.CandidateId != material.CandidateId)
                throw new InvalidDataException("qa04.production-candidate.basis-step-rebound");
            return existingByStep;
        }

        _byBasisStep.Add(material.BasisStep, material);
        return material;
    }

    public IReadOnlyList<Qa04ProductionStepCandidateIdentityV1> SnapshotCanonical()
        => Array.AsReadOnly(_byBasisStep.Values
            .OrderBy(static material => material.BasisStep)
            .ToArray());

    public IReadOnlyList<Qa04ProductionStepCandidateIdentityV1> ValidateCompleteCanonicalRun()
    {
        var ordered = SnapshotCanonical();
        var expectedCount = checked((int)(
            Qa04MeasurementPhaseContractV1.WarmUpStepCount +
            Qa04MeasurementPhaseContractV1.MeasurementStepCount));
        if (ordered.Count != expectedCount)
            throw new InvalidDataException("qa04.production-candidate.sequence-count-drift");

        var expectedBasis = Qa04MeasurementPhaseContractV1.InitializationBasisStep;
        foreach (var material in ordered)
        {
            if (material.WorldId != Qa04ReferenceLoadV1.WorldId ||
                material.BasisStep != expectedBasis ||
                material.TargetStep != checked(expectedBasis + 1UL))
                throw new InvalidDataException("qa04.production-candidate.sequence-step-drift");

            var derived = Qa04ProductionStepCandidateIdentityDerivationV1.Derive(
                material.WorldId,
                material.BasisStep);
            if (derived.CandidateId != material.CandidateId)
                throw new InvalidDataException("qa04.production-candidate.sequence-identity-drift");

            expectedBasis = checked(expectedBasis + 1UL);
        }

        if (expectedBasis != Qa04MeasurementPhaseContractV1.MeasurementLastFinalizedStep)
            throw new InvalidDataException("qa04.production-candidate.sequence-final-state-drift");

        return ordered;
    }
}
