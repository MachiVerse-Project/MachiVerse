using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04ReferenceLoadSmoke
{
    internal static void Run()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();

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

        var transactionBuckets = Enumerable.Range(0, 1_000)
            .Select(index => Qa04ReferenceScenariosV1.SelectTransactionKind((ulong)index).Value)
            .GroupBy(static value => value, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        foreach (var kind in Qa04ReferenceScenariosV1.TransactionKinds)
            Require(transactionBuckets[kind.KindToken.Value] == kind.SharePermille,
                $"QA-04 transaction mix drifted for {kind.KindToken.Value}.");

        var transactionA = Qa04ReferenceScenariosV1.ActiveTransaction(42);
        var transactionB = Qa04ReferenceScenariosV1.ActiveTransaction(42);
        Require(transactionA.TransactionId == transactionB.TransactionId &&
                !transactionA.TransactionId.IsZero &&
                transactionA.SubjectIds.SequenceEqual(transactionB.SubjectIds),
            "QA-04 active transaction identity must be deterministic.");
        Require(!Qa04ReferenceScenariosV1.IsCrossDomainTransactionCreationStep(299) &&
                Qa04ReferenceScenariosV1.IsCrossDomainTransactionCreationStep(300),
            "QA-04 transaction creation cadence drifted.");

        var environmentCell = Qa04ReferenceLoadV1.Record(new StableToken("environment.d0-cell-cohort"), 123).RecordId;
        Require(Qa04ReferenceScenariosV1.EnvironmentReceivesPrecipitation(environmentCell, 60) ==
                Qa04ReferenceScenariosV1.EnvironmentReceivesPrecipitation(environmentCell, 60),
            "QA-04 precipitation selector is not deterministic.");
        Require(!Qa04ReferenceScenariosV1.EnvironmentHazardIntensityChanges(environmentCell, 29),
            "QA-04 hazard change must respect the 30-Step cadence.");
        Require(Qa04ReferenceScenariosV1.EnvironmentContaminantTransportActive(environmentCell, 60) ==
                Qa04ReferenceScenariosV1.EnvironmentContaminantTransportActive(environmentCell, 60),
            "QA-04 contaminant selector is not deterministic.");

        var collisions = Enumerable.Range(0, 100)
            .Select(index => Qa04ReferenceScenariosV1.PhysicalCollision((ulong)index, 42))
            .ToArray();
        Require(collisions.Count(static item => item.LoadClass == Qa04PhysicalCollisionClassV1.ZeroContact) == 80 &&
                collisions.Count(static item => item.LoadClass == Qa04PhysicalCollisionClassV1.OneToFourCandidates) == 15 &&
                collisions.Count(static item => item.LoadClass == Qa04PhysicalCollisionClassV1.FiveToSixteenCandidates) == 4 &&
                collisions.Count(static item => item.LoadClass == Qa04PhysicalCollisionClassV1.SeventeenToSixtyFourCandidates) == 1,
            "QA-04 physical collision class distribution drifted.");
        Require(collisions.All(static item => item.LoadClass switch
        {
            Qa04PhysicalCollisionClassV1.ZeroContact => item.CandidateContactCount == 0,
            Qa04PhysicalCollisionClassV1.OneToFourCandidates => item.CandidateContactCount is >= 1 and <= 4,
            Qa04PhysicalCollisionClassV1.FiveToSixteenCandidates => item.CandidateContactCount is >= 5 and <= 16,
            Qa04PhysicalCollisionClassV1.SeventeenToSixtyFourCandidates => item.CandidateContactCount is >= 17 and <= 64,
            _ => false,
        }), "QA-04 physical collision candidate range drifted.");

        var marketScope = Qa04ReferenceScenariosV1.MarketScopeId(0);
        var marketOrder = Qa04ReferenceScenariosV1.MarketOrderId(0, 0);
        Require(!marketScope.IsZero && !marketOrder.IsZero && marketScope != marketOrder,
            "QA-04 market identities must be distinct and non-zero.");
        Require(Enumerable.Range(0, Qa04ReferenceScenariosV1.ActiveOrdersPerMarketScope)
                    .Count(order => Qa04ReferenceScenariosV1.MarketOrderChanges(0, order)) == 500,
            "QA-04 market cadence must select exactly 5% of canonical orders.");

        Require(Qa04ReferenceScenariosV1.InfrastructureNodeId(0) ==
                Qa04ReferenceScenariosV1.InfrastructureCascadingOutageSourceNodeId(),
            "QA-04 cascading outage source must be the fixed canonical node.");
        Require(!Qa04ReferenceScenariosV1.IsInfrastructureCascadingOutageStep(8_999) &&
                Qa04ReferenceScenariosV1.IsInfrastructureCascadingOutageStep(9_000),
            "QA-04 infrastructure outage cadence drifted.");
        Require(Qa04ReferenceScenariosV1.InfrastructureNodeId(0) != Qa04ReferenceScenariosV1.InfrastructureNodeId(1) &&
                Qa04ReferenceScenariosV1.InfrastructureEdgeId(0) != Qa04ReferenceScenariosV1.InfrastructureEdgeId(1) &&
                Qa04ReferenceScenariosV1.InfrastructureServiceRequestId(0) != Qa04ReferenceScenariosV1.InfrastructureServiceRequestId(1),
            "QA-04 infrastructure identities must be unique by ordinal.");

        Require(Qa04ReferenceScenariosV1.DetailTransitionBatches(299).Count == 0,
            "QA-04 detail transition must not run before cadence boundary.");
        var detailBatches = Qa04ReferenceScenariosV1.DetailTransitionBatches(300);
        Require(detailBatches.Count == 2 &&
                detailBatches.Single(item => item.TransitionKind.Value == "promotion").RegionCount == 6 &&
                detailBatches.Single(item => item.TransitionKind.Value == "promotion").CandidateRecordCount == 30_000 &&
                detailBatches.Single(item => item.TransitionKind.Value == "demotion").RegionCount == 10 &&
                detailBatches.Single(item => item.TransitionKind.Value == "demotion").CandidateRecordCount == 80_000,
            "QA-04 detail transition batch drifted.");
        var promotion = new StableToken("promotion");
        Require(Qa04ReferenceScenariosV1.DetailTransitionRegionId(promotion, 0) !=
                Qa04ReferenceScenariosV1.DetailTransitionRegionId(promotion, 1),
            "QA-04 detail region identities must be unique.");
        Require(Qa04ReferenceScenariosV1.DetailTransitionCandidateId(promotion, 300, 0) ==
                Qa04ReferenceScenariosV1.DetailTransitionCandidateId(promotion, 300, 0),
            "QA-04 detail transition candidate identity must be deterministic.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
