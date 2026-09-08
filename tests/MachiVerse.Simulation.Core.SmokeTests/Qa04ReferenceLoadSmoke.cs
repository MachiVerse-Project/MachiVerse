using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04ReferenceLoadSmoke
{
    internal static void Run()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();

        Require(Qa04ReferenceLoadV1.BenchmarkProfileId == "perf.reference.v1",
            "QA-04 benchmark profile id drifted.");
        Require(Qa04ReferenceLoadV1.WorldSeedHex ==
                "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f",
            "QA-04 reference seed drifted.");
        Require(!Qa04ReferenceLoadV1.WorldId.IsZero,
            "QA-04 benchmark WorldId must be deterministically non-zero.");
        Require(Qa04ReferenceLoadV1.WarmupSteps == 9_000 &&
                Qa04ReferenceLoadV1.MeasurementSteps == 18_000,
            "QA-04 measurement window drifted.");

        Require(Qa04ReferenceLoadV1.ResidentDetailCount(DetailLevelV1.D0Entity) == 100_000,
            "QA-04 Resident D0 count drifted.");
        Require(Qa04ReferenceLoadV1.ResidentDetailCount(DetailLevelV1.D1LocalAggregate) == 300_000,
            "QA-04 Resident D1 count drifted.");
        Require(Qa04ReferenceLoadV1.ResidentDetailCount(DetailLevelV1.D2RegionalAggregate) == 400_000,
            "QA-04 Resident D2 count drifted.");
        Require(Qa04ReferenceLoadV1.ResidentDetailCount(DetailLevelV1.D3BoundarySummary) == 200_000,
            "QA-04 Resident D3 count drifted.");

        Require(Qa04ReferenceLoadV1.ResidentDetailLevel(99_999) == DetailLevelV1.D0Entity,
            "QA-04 D0 boundary drifted.");
        Require(Qa04ReferenceLoadV1.ResidentDetailLevel(100_000) == DetailLevelV1.D1LocalAggregate,
            "QA-04 D1 lower boundary drifted.");
        Require(Qa04ReferenceLoadV1.ResidentDetailLevel(399_999) == DetailLevelV1.D1LocalAggregate,
            "QA-04 D1 upper boundary drifted.");
        Require(Qa04ReferenceLoadV1.ResidentDetailLevel(400_000) == DetailLevelV1.D2RegionalAggregate,
            "QA-04 D2 lower boundary drifted.");
        Require(Qa04ReferenceLoadV1.ResidentDetailLevel(799_999) == DetailLevelV1.D2RegionalAggregate,
            "QA-04 D2 upper boundary drifted.");
        Require(Qa04ReferenceLoadV1.ResidentDetailLevel(800_000) == DetailLevelV1.D3BoundarySummary,
            "QA-04 D3 lower boundary drifted.");

        var residentClass = new StableToken("resident.persistent-identity");
        var residentA = Qa04ReferenceLoadV1.Record(residentClass, 0);
        var residentB = Qa04ReferenceLoadV1.Record(residentClass, 0);
        Require(residentA == residentB && !residentA.RecordId.IsZero,
            "QA-04 reference record derivation must be stable and non-zero.");
        Require(residentA.RegionalTileIndex < Qa04ReferenceLoadV1.RegionalTileCount,
            "QA-04 reference record tile is out of range.");

        var denseResident = Qa04ReferenceLoadV1.Record(residentClass, 4);
        var sparseResident = Qa04ReferenceLoadV1.Record(residentClass, 5);
        Require(denseResident.DetailLevel == DetailLevelV1.D0Entity && denseResident.DenseRegionIndex is not null,
            "QA-04 must concentrate one quarter of D0 ordinals into dense regions.");
        Require(sparseResident.DetailLevel == DetailLevelV1.D0Entity && sparseResident.DenseRegionIndex is null,
            "QA-04 non-dense D0 ordinal unexpectedly entered a dense region.");

        var positionA = Qa04ReferenceLoadV1.PositionWithinTile(residentA.RecordId, 42);
        var positionB = Qa04ReferenceLoadV1.PositionWithinTile(residentA.RecordId, 42);
        Require(positionA == positionB &&
                positionA.X >= 0 && positionA.X < 1 &&
                positionA.Y >= 0 && positionA.Y < 1,
            "QA-04 addressable position must be deterministic and normalized.");

        var activityA = Qa04ReferenceLoadV1.ResidentActivity(residentA.RecordId, 42);
        var activityB = Qa04ReferenceLoadV1.ResidentActivity(residentA.RecordId, 42);
        Require(activityA == activityB &&
                Qa04ReferenceLoadV1.ResidentActivityMix.Any(entry => entry.ActivityToken == activityA),
            "QA-04 Resident activity selection must be deterministic and canonical.");

        Require(Qa04ReferenceLoadV1.OperationCountForStep(1) == 5_000,
            "QA-04 steady Operation count drifted.");
        Require(Qa04ReferenceLoadV1.OperationCountForStep(900) == 55_000,
            "QA-04 burst Operation count drifted.");

        var steady = Qa04ReferenceLoadV1.OperationsForStep(1).ToArray();
        Require(steady.Length == 5_000,
            "QA-04 steady Operation materialization count mismatch.");
        Require(steady.Select(static operation => operation.OperationId).Distinct().Count() == steady.Length,
            "QA-04 steady OperationId collision detected.");
        Require(steady.All(static operation => operation.PayloadDigest.Length == 32),
            "QA-04 Operation payload digest length mismatch.");

        var steadyRepeat = Qa04ReferenceLoadV1.OperationsForStep(1).ToArray();
        Require(steady.Select(static operation => operation.OperationId)
                .SequenceEqual(steadyRepeat.Select(static operation => operation.OperationId)),
            "QA-04 steady Operation sequence is not deterministic.");
        Require(steady.Select(static operation => Convert.ToHexString(operation.PayloadDigest))
                .SequenceEqual(steadyRepeat.Select(static operation => Convert.ToHexString(operation.PayloadDigest))),
            "QA-04 steady Operation payload sequence is not deterministic.");

        var burst = Qa04ReferenceLoadV1.OperationsForStep(900).ToArray();
        Require(burst.Length == 55_000,
            "QA-04 burst Operation materialization count mismatch.");
        Require(burst.Select(static operation => operation.OperationId).Distinct().Count() == burst.Length,
            "QA-04 burst OperationId collision detected.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
