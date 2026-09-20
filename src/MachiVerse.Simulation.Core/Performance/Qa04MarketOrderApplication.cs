using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04MarketOrderApplicationResultV1(
    SocietyMarketTransactionRecordMaterialV2 CreatedOrder,
    SocietyMarketTransactionPartitionStateV2 MarketState);

/// <summary>
/// Applies the already-bound perf.reference.v1 society-market-payment-contract Operation by adding
/// one deterministic open order to authoritative society.market_transaction v2 state. This is a
/// benchmark-only creation boundary; matching, clearing, settlement, and payment consequences are
/// intentionally outside this handler.
/// </summary>
public static class Qa04MarketOrderApplicationV1
{
    private const string MarketFamily = "society-market-payment-contract";
    private const string MarketOrderOperationKind = "society.market.order-place";

    private static readonly StableToken SocietyDomain = new("society_economy");
    private static readonly StableToken ResidentClass = new("resident.persistent-identity");
    private static readonly StableToken RuntimeOrderKind = new("perf.market-order-operation");
    private static readonly StableToken Buy = new("buy");
    private static readonly StableToken Sell = new("sell");
    private static readonly StableToken Open = new("open");

    public static Qa04MarketOrderApplicationResultV1 Apply(
        Qa04CanonicalOperationBindingResultV1 binding,
        SocietyMarketTransactionPartitionStateV2 current,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(references);

        RequireCanonicalBinding(binding);
        SocietyMarketTransactionPartitionIdentityV2.ValidateCanonicalContract();
        if (current.State.Identity != SocietyMarketTransactionPartitionIdentityV2.Identity)
            throw new InvalidDataException("qa04.market.order-partition-identity");

        var target = RequireMarketTarget(binding, current.RecordSet);
        var created = CreateOrderRecordCore(binding, target, references);
        if (current.RecordSet.TryGet(created.RecordId, out _))
            throw new InvalidDataException("qa04.market.order-record-id-collision");

        var next = current.WithAdditions(
            new[] { created },
            "qa04.market.order-record-id-collision");
        if (next.State.ItemCount != checked(current.State.ItemCount + 1UL))
            throw new InvalidDataException("qa04.market.order-create-count-drift");

        return new Qa04MarketOrderApplicationResultV1(created, next);
    }

    internal static SocietyMarketTransactionRecordMaterialV2 CreateOrderRecord(
        Qa04CanonicalOperationBindingResultV1 binding,
        SocietyMarketTransactionRecordMaterialV2 target,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(references);

        RequireCanonicalBinding(binding);
        SocietyMarketTransactionPartitionIdentityV2.ValidateCanonicalContract();
        RequireExpectedMarketTarget(binding, target);
        return CreateOrderRecordCore(binding, target, references);
    }

    private static SocietyMarketTransactionRecordMaterialV2 RequireMarketTarget(
        Qa04CanonicalOperationBindingResultV1 binding,
        SocietyMarketTransactionRecordSetV2 records)
    {
        var descriptor = binding.SourceDescriptor;
        var scopeOrdinal = checked((uint)(descriptor.FamilyOrdinal % (ulong)Qa04ReferenceScenariosV1.MarketScopeCount));
        var expectedMarket = Qa04MarketMaterializerV1.CreateMarketState(
            scopeOrdinal,
            Qa04SpatialTileScopeAuthorityV1.ScopeRef,
            out _);
        var marketRef = new PartitionRecordRefV1(
            SocietyMarketTransactionRecordSchemaV2.PartitionId,
            expectedMarket.RecordId);
        var effectiveStep = checked(descriptor.InjectionStep + 1UL);
        if (binding.PrimaryTarget != marketRef || binding.ScheduledOperation.EffectiveStep != effectiveStep)
            throw new InvalidDataException("qa04.market.order-target-step-drift");
        if (!records.TryGet(marketRef.RecordId, out var target) || target is null)
            throw new InvalidDataException("qa04.market.order-target-missing");
        RequireCanonicalMarketState(target, expectedMarket);
        return target;
    }

    private static void RequireExpectedMarketTarget(
        Qa04CanonicalOperationBindingResultV1 binding,
        SocietyMarketTransactionRecordMaterialV2 target)
    {
        var descriptor = binding.SourceDescriptor;
        var scopeOrdinal = checked((uint)(descriptor.FamilyOrdinal % (ulong)Qa04ReferenceScenariosV1.MarketScopeCount));
        var expectedMarket = Qa04MarketMaterializerV1.CreateMarketState(
            scopeOrdinal,
            Qa04SpatialTileScopeAuthorityV1.ScopeRef,
            out _);
        var marketRef = new PartitionRecordRefV1(
            SocietyMarketTransactionRecordSchemaV2.PartitionId,
            expectedMarket.RecordId);
        var effectiveStep = checked(descriptor.InjectionStep + 1UL);
        if (binding.PrimaryTarget != marketRef || binding.ScheduledOperation.EffectiveStep != effectiveStep)
            throw new InvalidDataException("qa04.market.order-target-step-drift");
        RequireCanonicalMarketState(target, expectedMarket);
    }

    private static SocietyMarketTransactionRecordMaterialV2 CreateOrderRecordCore(
        Qa04CanonicalOperationBindingResultV1 binding,
        SocietyMarketTransactionRecordMaterialV2 target,
        IDomainRecordSchemaResolverV1 references)
    {
        var descriptor = binding.SourceDescriptor;
        var effectiveStep = checked(descriptor.InjectionStep + 1UL);
        var marketRef = binding.PrimaryTarget;
        var marketPayload = (SocietyMarketStatePayloadV2)target.Payload;

        var resident = Qa04ReferenceLoadV1.Record(ResidentClass, descriptor.FamilyOrdinal);
        var ownerRef = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            resident.RecordId);
        var residentSchema = StandardDomainPartitionRegistry.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).RecordSchema;
        if (!references.TryGetRecordSchema(ownerRef, out var resolvedOwnerSchema) || resolvedOwnerSchema != residentSchema)
            throw new InvalidDataException("qa04.market.order-owner-ref");

        var side = (descriptor.FamilyOrdinal & 1UL) == 0UL ? Buy : Sell;
        var price = side == Buy
            ? checked(100_000L + (long)(descriptor.FamilyOrdinal % 1_000UL))
            : checked(99_500L + (long)(descriptor.FamilyOrdinal % 1_000UL));
        var quantity = checked(1L + (long)(descriptor.FamilyOrdinal % 20UL));

        var recordId = DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            SocietyDomain,
            descriptor.OperationId,
            RuntimeOrderKind,
            localOrdinal: 0);
        if (recordId.IsZero)
            throw new InvalidDataException("qa04.market.order-record-id-zero");

        var payload = new SocietyMarketOrderPayloadV2(
            marketRef,
            ownerRef,
            marketPayload.InstrumentToken,
            side,
            price,
            quantity,
            quantity,
            effectiveStep,
            Open);
        var created = new SocietyMarketTransactionRecordMaterialV2(
            recordId,
            revision: 1,
            createdStep: effectiveStep,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            payload);

        if (created.RecordSchema != SocietyMarketTransactionRecordSchemaV2.RecordSchema ||
            created.Revision != 1 ||
            created.CreatedStep != effectiveStep ||
            created.RetiredStep is not null ||
            created.DetailLevel != DetailLevelV1.D2RegionalAggregate ||
            created.LineageRef is not null ||
            created.Payload is not SocietyMarketOrderPayloadV2 createdPayload ||
            createdPayload.MarketRef != marketRef ||
            createdPayload.OwnerRef != ownerRef ||
            createdPayload.InstrumentToken != marketPayload.InstrumentToken ||
            createdPayload.Side != side ||
            createdPayload.LimitPriceMicrounit != price ||
            createdPayload.Quantity != quantity ||
            createdPayload.RemainingQuantity != quantity ||
            createdPayload.EligibleStep != effectiveStep ||
            createdPayload.Status != Open)
        {
            throw new InvalidDataException("qa04.market.order-created-record-drift");
        }

        return created;
    }

    private static void RequireCanonicalMarketState(
        SocietyMarketTransactionRecordMaterialV2 actual,
        SocietyMarketTransactionRecordMaterialV2 expected)
    {
        if (actual.RecordId != expected.RecordId ||
            actual.RecordSchema != expected.RecordSchema ||
            actual.Revision != 1 ||
            actual.CreatedStep != 0 ||
            actual.RetiredStep is not null ||
            actual.DetailLevel != DetailLevelV1.D2RegionalAggregate ||
            actual.LineageRef is not null ||
            actual.Payload is not SocietyMarketStatePayloadV2 actualPayload ||
            expected.Payload is not SocietyMarketStatePayloadV2 expectedPayload ||
            actualPayload.ScopeRef != expectedPayload.ScopeRef ||
            actualPayload.InstrumentToken != expectedPayload.InstrumentToken ||
            actualPayload.CurrencyToken != expectedPayload.CurrencyToken ||
            actualPayload.Status != expectedPayload.Status ||
            actualPayload.ClearingCadenceSteps != expectedPayload.ClearingCadenceSteps ||
            actualPayload.LastClearingStep != expectedPayload.LastClearingStep ||
            actualPayload.LastClearingPriceMicrounit != expectedPayload.LastClearingPriceMicrounit)
        {
            throw new InvalidDataException("qa04.market.order-target-drift");
        }
    }

    private static void RequireCanonicalBinding(Qa04CanonicalOperationBindingResultV1 binding)
    {
        if (binding.SourceDescriptor is null || binding.BoundDescriptor is null ||
            binding.Operation is null || binding.OrderKey is null || binding.ScheduledOperation is null)
            throw new InvalidDataException("qa04.market.order-binding-null");
        if (binding.SourceDescriptor.FamilyToken.Value != MarketFamily)
            throw new InvalidDataException("qa04.market.order-family");
        if (binding.Operation.OperationKind != MarketOrderOperationKind)
            throw new InvalidDataException("qa04.market.order-operation-kind");
        if (binding.OwnerDomain != SocietyDomain)
            throw new InvalidDataException("qa04.market.order-owner-domain");
        if (binding.Operation.Admission is null || binding.Operation.Admission.SchedulingPolicyGeneration == 0)
            throw new InvalidDataException("qa04.market.order-admission");

        if (Qa04CanonicalOperationBindingV1.HasCanonicalAuthority(binding))
            return;

        var expected = Qa04CanonicalOperationBindingV1.Bind(
            binding.SourceDescriptor,
            binding.Operation.Admission.SchedulingPolicyGeneration);

        if (!expected.Operation.ToByteArray().AsSpan().SequenceEqual(binding.Operation.ToByteArray()) ||
            expected.OwnerDomain != binding.OwnerDomain ||
            expected.PrimaryTarget != binding.PrimaryTarget ||
            expected.ScheduledOperation.OperationId != binding.ScheduledOperation.OperationId ||
            expected.ScheduledOperation.EffectiveStep != binding.ScheduledOperation.EffectiveStep ||
            !expected.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(binding.OrderKey.ToDatabaseBytes()) ||
            !expected.ScheduledOperation.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(
                binding.ScheduledOperation.OrderKey.ToDatabaseBytes()) ||
            expected.BoundDescriptor.InjectionStep != binding.BoundDescriptor.InjectionStep ||
            expected.BoundDescriptor.FamilyToken != binding.BoundDescriptor.FamilyToken ||
            expected.BoundDescriptor.FamilyOrdinal != binding.BoundDescriptor.FamilyOrdinal ||
            expected.BoundDescriptor.OperationId != binding.BoundDescriptor.OperationId ||
            !expected.BoundDescriptor.PayloadDigest.AsSpan().SequenceEqual(binding.BoundDescriptor.PayloadDigest))
        {
            throw new InvalidDataException("qa04.market.order-binding-drift");
        }
    }
}
