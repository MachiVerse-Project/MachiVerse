using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04ProductionStepCandidateIdentityInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var worldId = Qa04ReferenceLoadV1.WorldId;
        var first = Qa04ProductionStepCandidateIdentityDerivationV1.Derive(
            worldId,
            Qa04MeasurementPhaseContractV1.InitializationBasisStep);
        var retry = Qa04ProductionStepCandidateIdentityDerivationV1.Derive(
            worldId,
            Qa04MeasurementPhaseContractV1.InitializationBasisStep);
        Require(first.CandidateId == retry.CandidateId,
            "QA-04 retry/replay must derive the same CandidateId.");
        Require(first.BasisStep == 1 && first.TargetStep == 2 && !first.CandidateId.IsZero,
            "QA-04 first production CandidateId tuple drifted.");

        var second = Qa04ProductionStepCandidateIdentityDerivationV1.Derive(worldId, 2);
        Require(second.CandidateId != first.CandidateId,
            "QA-04 distinct production Steps must not share CandidateId.");

        RequireThrows<ArgumentException>(
            () => Qa04ProductionStepCandidateIdentityDerivationV1.Derive(OpaqueId128.Zero, 1),
            "QA-04 CandidateId derivation must reject ZERO WorldId.");
        RequireThrows<InvalidDataException>(
            () => Qa04ProductionStepCandidateIdentityDerivationV1.Derive(worldId, ulong.MaxValue),
            "QA-04 CandidateId derivation must reject Step overflow.");

        var registry = new Qa04ProductionStepCandidateIdentityRegistryV1();
        for (var basisStep = Qa04MeasurementPhaseContractV1.InitializationBasisStep;
             basisStep < Qa04MeasurementPhaseContractV1.MeasurementLastFinalizedStep;
             basisStep++)
        {
            var material = registry.DeriveAndRegister(worldId, basisStep);
            var sameTupleRetry = registry.DeriveAndRegister(worldId, basisStep);
            Require(material.CandidateId == sameTupleRetry.CandidateId,
                "QA-04 run-local registry must preserve retry identity.");
        }

        var sequence = registry.ValidateCompleteCanonicalRun();
        Require(sequence.Count == 27_000,
            "QA-04 canonical run must contain exactly 27,000 CandidateIds.");
        Require(sequence[0].BasisStep == 1 && sequence[0].TargetStep == 2,
            "QA-04 CandidateId sequence first tuple drifted.");
        Require(sequence[^1].BasisStep == 27_000 && sequence[^1].TargetStep == 27_001,
            "QA-04 CandidateId sequence final tuple drifted.");
        Require(sequence.Select(static material => material.CandidateId).Distinct().Count() == sequence.Count,
            "QA-04 CandidateId sequence contains a collision.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<T>(Action action, string message)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
