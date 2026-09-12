using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04TransactionKindV1(
    StableToken KindToken,
    ushort SharePermille);

public sealed record Qa04ActiveTransactionDescriptorV1(
    ulong Ordinal,
    StableToken KindToken,
    OpaqueId128 TransactionId,
    IReadOnlyList<OpaqueId128> SubjectIds);

public enum Qa04PhysicalCollisionClassV1 : byte
{
    ZeroContact = 0,
    OneToFourCandidates = 1,
    FiveToSixteenCandidates = 2,
    SeventeenToSixtyFourCandidates = 3,
}

public sealed record Qa04PhysicalCollisionDescriptorV1(
    ulong PhysicalOrdinal,
    OpaqueId128 SubjectId,
    Qa04PhysicalCollisionClassV1 LoadClass,
    int CandidateContactCount);

public sealed record Qa04DetailTransitionBatchV1(
    ulong Step,
    StableToken TransitionKind,
    int RegionCount,
    ulong CandidateRecordCount);

/// <summary>
/// Canonical deterministic scenario selectors for the non-population portions of QA-04
/// perf.reference.v1. These helpers define benchmark input only; they do not bypass domain
/// runtimes, persistence, scheduling, or publication semantics.
/// </summary>
public static class Qa04ReferenceScenariosV1
{
    public const ulong ActiveCrossDomainTransactionTarget = 10_000;
    public const ulong CrossDomainTransactionCreationEverySteps = 300;

    public const uint EnvironmentPrecipitationD0Ppm = 100_000;
    public const uint EnvironmentSurfaceFlowThresholdPpm = 10_000;
    public const uint EnvironmentHazardIntensityChangePpm = 1_000;
    public const ulong EnvironmentHazardCadenceSteps = 30;
    public const uint EnvironmentContaminantTransportD0Ppm = 20_000;

    public const ulong PhysicalD0PresenceCount = 500_000;

    public const int MarketScopeCount = 100;
    public const int ActiveOrdersPerMarketScope = 10_000;
    public const uint MarketOrderChangePpm = 50_000;

    public const int InfrastructureNetworkNodeCount = 20_000;
    public const int InfrastructureStableEdgeCount = 100_000;
    public const int InfrastructureQueuedServiceRequestCount = 250_000;
    public const ulong InfrastructureCascadingOutageEverySteps = 9_000;

    public const ulong DetailTransitionEverySteps = 300;
    public const int PromotionRegionCount = 6;
    public const ulong PromotionCandidateRecordCount = 30_000;
    public const int DemotionRegionCount = 10;
    public const ulong DemotionCandidateRecordCount = 80_000;

    private static readonly StableToken PerformanceDomain = new("performance");
    private static readonly StableToken TransactionRootKind = new("perf.transaction-root");
    private static readonly StableToken PrecipitationPurpose = new("perf.reference.precipitation.v1");
    private static readonly StableToken SurfaceFlowPurpose = new("perf.reference.surface-flow.v1");
    private static readonly StableToken HazardPurpose = new("perf.reference.hazard-change.v1");
    private static readonly StableToken ContaminantPurpose = new("perf.reference.contaminant.v1");
    private static readonly StableToken CollisionPurpose = new("perf.reference.collision.v1");

    public static readonly IReadOnlyList<Qa04TransactionKindV1> TransactionKinds = Array.AsReadOnly(new[]
    {
        new Qa04TransactionKindV1(new StableToken("market-sale-delivery"), 350),
        new Qa04TransactionKindV1(new StableToken("employment-work"), 200),
        new Qa04TransactionKindV1(new StableToken("food-consumption"), 100),
        new Qa04TransactionKindV1(new StableToken("information-transmission"), 100),
        new Qa04TransactionKindV1(new StableToken("medical-service"), 50),
        new Qa04TransactionKindV1(new StableToken("construction"), 50),
        new Qa04TransactionKindV1(new StableToken("mining-excavation"), 30),
        new Qa04TransactionKindV1(new StableToken("crime-justice"), 30),
        new Qa04TransactionKindV1(new StableToken("border-crossing"), 30),
        new Qa04TransactionKindV1(new StableToken("infrastructure-outage-cascade"), 20),
        new Qa04TransactionKindV1(new StableToken("natural-disaster-cascade"), 20),
        new Qa04TransactionKindV1(new StableToken("other-registered-transactions"), 20),
    });

    public static void ValidateCanonicalContract()
    {
        if (TransactionKinds.Sum(static item => (int)item.SharePermille) != 1_000)
            throw new InvalidDataException("qa04.reference.transaction-kind-total");
        if (TransactionKinds.Select(static item => item.KindToken).Distinct().Count() != TransactionKinds.Count)
            throw new InvalidDataException("qa04.reference.transaction-kind-duplicate");
        if (ActiveCrossDomainTransactionTarget != 10_000 || CrossDomainTransactionCreationEverySteps != 300)
            throw new InvalidDataException("qa04.reference.transaction-load-drift");
        if (EnvironmentPrecipitationD0Ppm != 100_000 ||
            EnvironmentSurfaceFlowThresholdPpm != 10_000 ||
            EnvironmentHazardIntensityChangePpm != 1_000 ||
            EnvironmentHazardCadenceSteps != 30 ||
            EnvironmentContaminantTransportD0Ppm != 20_000)
            throw new InvalidDataException("qa04.reference.environment-load-drift");
        if (MarketScopeCount != 100 || ActiveOrdersPerMarketScope != 10_000 || MarketOrderChangePpm != 50_000)
            throw new InvalidDataException("qa04.reference.market-load-drift");
        if (InfrastructureNetworkNodeCount != 20_000 ||
            InfrastructureStableEdgeCount != 100_000 ||
            InfrastructureQueuedServiceRequestCount != 250_000 ||
            InfrastructureCascadingOutageEverySteps != 9_000)
            throw new InvalidDataException("qa04.reference.infrastructure-load-drift");
        if (DetailTransitionEverySteps != 300 ||
            PromotionRegionCount != 6 || PromotionCandidateRecordCount != 30_000 ||
            DemotionRegionCount != 10 || DemotionCandidateRecordCount != 80_000)
            throw new InvalidDataException("qa04.reference.detail-transition-load-drift");
    }

    public static bool IsCrossDomainTransactionCreationStep(ulong step)
        => step != 0 && step % CrossDomainTransactionCreationEverySteps == 0;

    public static Qa04ActiveTransactionDescriptorV1 ActiveTransaction(ulong ordinal)
    {
        if (ordinal >= ActiveCrossDomainTransactionTarget)
            throw new ArgumentOutOfRangeException(nameof(ordinal));

        var kind = SelectTransactionKind(ordinal);
        var firstSubject = DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            0,
            PerformanceDomain,
            OpaqueId128.Zero,
            TransactionRootKind,
            checked(ordinal * 2));
        var secondSubject = DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            0,
            PerformanceDomain,
            OpaqueId128.Zero,
            TransactionRootKind,
            checked(ordinal * 2 + 1));
        var subjects = new[] { firstSubject, secondSubject };
        var root = new CausalityRefV1(CausalityRefKindV1.Entity, firstSubject.ToBytes(), 0);
        var transactionId = TransactionIdentityV1.Derive(
            Qa04ReferenceLoadV1.WorldId,
            kind,
            0,
            root,
            subjects,
            ordinal);
        return new Qa04ActiveTransactionDescriptorV1(
            ordinal,
            kind,
            transactionId,
            Array.AsReadOnly(subjects));
    }

    public static StableToken SelectTransactionKind(ulong ordinal)
    {
        var bucket = checked((ushort)(ordinal % 1_000));
        var cumulative = 0;
        foreach (var item in TransactionKinds)
        {
            cumulative += item.SharePermille;
            if (bucket < cumulative) return item.KindToken;
        }
        throw new InvalidDataException("qa04.reference.transaction-kind-selection-fell-through");
    }

    public static bool EnvironmentReceivesPrecipitation(OpaqueId128 cellId, ulong step)
        => Occurs(cellId, step, PrecipitationPurpose, EnvironmentPrecipitationD0Ppm);

    public static bool EnvironmentCrossesSurfaceFlowThreshold(OpaqueId128 cellId, ulong step)
        => Occurs(cellId, step, SurfaceFlowPurpose, EnvironmentSurfaceFlowThresholdPpm);

    public static bool EnvironmentHazardIntensityChanges(OpaqueId128 cellId, ulong step)
        => step != 0 && step % EnvironmentHazardCadenceSteps == 0 &&
           Occurs(cellId, step, HazardPurpose, EnvironmentHazardIntensityChangePpm);

    public static bool EnvironmentContaminantTransportActive(OpaqueId128 cellId, ulong step)
        => Occurs(cellId, step, ContaminantPurpose, EnvironmentContaminantTransportD0Ppm);

    public static Qa04PhysicalCollisionDescriptorV1 PhysicalCollision(ulong physicalOrdinal, ulong step)
    {
        if (physicalOrdinal >= PhysicalD0PresenceCount)
            throw new ArgumentOutOfRangeException(nameof(physicalOrdinal));

        var record = Qa04ReferenceLoadV1.Record(new StableToken("physical.d0-presence"), physicalOrdinal);
        var bucket = physicalOrdinal % 100;
        var (loadClass, min, max) = bucket switch
        {
            < 80 => (Qa04PhysicalCollisionClassV1.ZeroContact, 0, 0),
            < 95 => (Qa04PhysicalCollisionClassV1.OneToFourCandidates, 1, 4),
            < 99 => (Qa04PhysicalCollisionClassV1.FiveToSixteenCandidates, 5, 16),
            _ => (Qa04PhysicalCollisionClassV1.SeventeenToSixtyFourCandidates, 17, 64),
        };
        var count = min == max ? min : min + checked((int)DeterministicRandom.BoundedUInt64(
            Qa04ReferenceLoadV1.WorldSeed,
            new RandomContextV1(
                Qa04ReferenceLoadV1.WorldId,
                step,
                PerformanceDomain,
                CollisionPurpose,
                record.RecordId,
                OpaqueId128.Zero,
                OpaqueId128.Zero,
                0),
            0,
            checked((ulong)(max - min + 1))));
        return new Qa04PhysicalCollisionDescriptorV1(
            physicalOrdinal,
            record.RecordId,
            loadClass,
            count);
    }

    public static OpaqueId128 MarketScopeId(int scopeOrdinal)
    {
        if (scopeOrdinal < 0 || scopeOrdinal >= MarketScopeCount)
            throw new ArgumentOutOfRangeException(nameof(scopeOrdinal));
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            0,
            PerformanceDomain,
            OpaqueId128.Zero,
            new StableToken("perf.market-scope"),
            checked((ulong)scopeOrdinal));
    }

    public static OpaqueId128 MarketOrderId(int scopeOrdinal, int orderOrdinal)
    {
        if (orderOrdinal < 0 || orderOrdinal >= ActiveOrdersPerMarketScope)
            throw new ArgumentOutOfRangeException(nameof(orderOrdinal));
        var scopeId = MarketScopeId(scopeOrdinal);
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            0,
            PerformanceDomain,
            scopeId,
            new StableToken("perf.market-order"),
            checked((ulong)orderOrdinal));
    }

    public static bool MarketOrderChanges(int scopeOrdinal, int orderOrdinal)
    {
        _ = MarketScopeId(scopeOrdinal);
        if (orderOrdinal < 0 || orderOrdinal >= ActiveOrdersPerMarketScope)
            throw new ArgumentOutOfRangeException(nameof(orderOrdinal));
        return orderOrdinal % 20 == 0;
    }

    public static OpaqueId128 InfrastructureNodeId(int nodeOrdinal)
        => InfrastructureIdentity("perf.infrastructure-node", nodeOrdinal, InfrastructureNetworkNodeCount);

    public static OpaqueId128 InfrastructureEdgeId(int edgeOrdinal)
        => InfrastructureIdentity("perf.infrastructure-edge", edgeOrdinal, InfrastructureStableEdgeCount);

    public static OpaqueId128 InfrastructureServiceRequestId(int requestOrdinal)
        => InfrastructureIdentity("perf.infrastructure-service-request", requestOrdinal, InfrastructureQueuedServiceRequestCount);

    public static bool IsInfrastructureCascadingOutageStep(ulong step)
        => step != 0 && step % InfrastructureCascadingOutageEverySteps == 0;

    public static OpaqueId128 InfrastructureCascadingOutageSourceNodeId()
        => InfrastructureNodeId(0);

    public static IReadOnlyList<Qa04DetailTransitionBatchV1> DetailTransitionBatches(ulong step)
    {
        if (step == 0 || step % DetailTransitionEverySteps != 0)
            return Array.Empty<Qa04DetailTransitionBatchV1>();
        return Array.AsReadOnly(new[]
        {
            new Qa04DetailTransitionBatchV1(
                step,
                new StableToken("promotion"),
                PromotionRegionCount,
                PromotionCandidateRecordCount),
            new Qa04DetailTransitionBatchV1(
                step,
                new StableToken("demotion"),
                DemotionRegionCount,
                DemotionCandidateRecordCount),
        });
    }

    public static OpaqueId128 DetailTransitionRegionId(StableToken transitionKind, int regionOrdinal)
    {
        var regionCount = transitionKind.Value switch
        {
            "promotion" => PromotionRegionCount,
            "demotion" => DemotionRegionCount,
            _ => throw new ArgumentOutOfRangeException(nameof(transitionKind)),
        };
        if (regionOrdinal < 0 || regionOrdinal >= regionCount)
            throw new ArgumentOutOfRangeException(nameof(regionOrdinal));
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            0,
            PerformanceDomain,
            OpaqueId128.Zero,
            new StableToken($"perf.detail-{transitionKind.Value}-region"),
            checked((ulong)regionOrdinal));
    }

    public static OpaqueId128 DetailTransitionCandidateId(
        StableToken transitionKind,
        ulong step,
        ulong candidateOrdinal)
    {
        var candidateCount = transitionKind.Value switch
        {
            "promotion" => PromotionCandidateRecordCount,
            "demotion" => DemotionCandidateRecordCount,
            _ => throw new ArgumentOutOfRangeException(nameof(transitionKind)),
        };
        if (step == 0 || step % DetailTransitionEverySteps != 0)
            throw new ArgumentOutOfRangeException(nameof(step));
        if (candidateOrdinal >= candidateCount)
            throw new ArgumentOutOfRangeException(nameof(candidateOrdinal));
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            step,
            PerformanceDomain,
            OpaqueId128.Zero,
            new StableToken($"perf.detail-{transitionKind.Value}-candidate"),
            candidateOrdinal);
    }

    private static bool Occurs(OpaqueId128 subjectId, ulong step, StableToken purpose, uint probabilityPpm)
    {
        if (subjectId.IsZero) throw new ArgumentException("subjectId ZERO is invalid.", nameof(subjectId));
        if (probabilityPpm > 1_000_000) throw new ArgumentOutOfRangeException(nameof(probabilityPpm));
        if (probabilityPpm == 0) return false;
        if (probabilityPpm == 1_000_000) return true;
        var context = new RandomContextV1(
            Qa04ReferenceLoadV1.WorldId,
            step,
            PerformanceDomain,
            purpose,
            subjectId,
            OpaqueId128.Zero,
            OpaqueId128.Zero,
            0);
        return DeterministicRandom.BoundedUInt64(Qa04ReferenceLoadV1.WorldSeed, context, 0, 1_000_000) < probabilityPpm;
    }

    private static OpaqueId128 InfrastructureIdentity(string kind, int ordinal, int count)
    {
        if (ordinal < 0 || ordinal >= count)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            0,
            PerformanceDomain,
            OpaqueId128.Zero,
            new StableToken(kind),
            checked((ulong)ordinal));
    }
}
