using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.SocietyEconomy;

/// <summary>
/// Alpha 1.1 exact society.market_transaction heterogeneous record schema v2. The standard partition
/// registry remains at v1; v2 is an explicit migration/materialization target.
/// </summary>
public static class SocietyMarketTransactionRecordSchemaV2
{
    public const string PartitionId = SocietyMarketTransactionPayloadV1.PartitionId;
    public const string MarketStateKind = "market_state";
    public const string OrderOrOfferKind = "order_or_offer";
    public const string TransactionOrPriceFactKind = "transaction_or_price_fact";

    public const string TradeFactKind = "market.trade";
    public const string ClearingPriceFactKind = "market.clearing-price";
    public const string LegacyV1FactKind = "market.legacy-v1";

    public static SchemaRefV1 RecordSchema { get; } =
        new("domain.society.market_transaction.record", 2, 0);

    public static IReadOnlyList<string> RecordKinds { get; } = Array.AsReadOnly(new[]
    {
        MarketStateKind,
        OrderOrOfferKind,
        TransactionOrPriceFactKind,
    }.OrderBy(static value => value, StringComparer.Ordinal).ToArray());

    public static IReadOnlyList<string> FactKinds { get; } = Array.AsReadOnly(new[]
    {
        ClearingPriceFactKind,
        LegacyV1FactKind,
        TradeFactKind,
    }.OrderBy(static value => value, StringComparer.Ordinal).ToArray());

    public static void ValidateCanonicalContract()
    {
        var current = StandardDomainPartitionRegistry.Get(PartitionId);
        if (current.RecordSchema.SchemaId != RecordSchema.SchemaId ||
            current.RecordSchema.Version != new SchemaVersionV1(1, 0) ||
            RecordSchema.Version != new SchemaVersionV1(2, 0))
            throw new InvalidDataException("society.market-transaction-v2.schema-version-contract");
        if (RecordKinds.Count != 3 || RecordKinds.Distinct(StringComparer.Ordinal).Count() != 3 ||
            !RecordKinds.SequenceEqual(RecordKinds.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("society.market-transaction-v2.record-kind-contract");
        if (FactKinds.Count != 3 || FactKinds.Distinct(StringComparer.Ordinal).Count() != 3 ||
            !FactKinds.SequenceEqual(FactKinds.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("society.market-transaction-v2.fact-kind-contract");
    }
}

public abstract class SocietyMarketTransactionRecordPayloadV2
{
    public abstract string RecordKind { get; }
}

public sealed class SocietyMarketStatePayloadV2 : SocietyMarketTransactionRecordPayloadV2
{
    private static readonly IReadOnlySet<string> Statuses = new HashSet<string>(StringComparer.Ordinal)
    {
        "active", "halted", "retired",
    };

    public SocietyMarketStatePayloadV2(
        PartitionRecordRefV1 scopeRef,
        StableToken instrumentToken,
        StableToken currencyToken,
        StableToken status,
        ulong clearingCadenceSteps,
        ulong? lastClearingStep,
        long? lastClearingPriceMicrounit)
    {
        if (scopeRef.PartitionId.Value != "spatial.scope_registry" || scopeRef.RecordId.IsZero)
            throw new InvalidDataException("society.market-transaction-v2.market-state-scope-ref");
        if (!Statuses.Contains(status.Value))
            throw new InvalidDataException("society.market-transaction-v2.market-state-status");
        if (clearingCadenceSteps == 0)
            throw new InvalidDataException("society.market-transaction-v2.clearing-cadence");
        if (lastClearingPriceMicrounit is < 0)
            throw new InvalidDataException("society.market-transaction-v2.clearing-price-negative");
        if (lastClearingPriceMicrounit.HasValue && !lastClearingStep.HasValue)
            throw new InvalidDataException("society.market-transaction-v2.clearing-price-without-step");

        ScopeRef = scopeRef;
        InstrumentToken = instrumentToken;
        CurrencyToken = currencyToken;
        Status = status;
        ClearingCadenceSteps = clearingCadenceSteps;
        LastClearingStep = lastClearingStep;
        LastClearingPriceMicrounit = lastClearingPriceMicrounit;
    }

    public override string RecordKind => SocietyMarketTransactionRecordSchemaV2.MarketStateKind;
    public PartitionRecordRefV1 ScopeRef { get; }
    public StableToken InstrumentToken { get; }
    public StableToken CurrencyToken { get; }
    public StableToken Status { get; }
    public ulong ClearingCadenceSteps { get; }
    public ulong? LastClearingStep { get; }
    public long? LastClearingPriceMicrounit { get; }
}

public sealed class SocietyMarketOrderPayloadV2 : SocietyMarketTransactionRecordPayloadV2
{
    private static readonly IReadOnlySet<string> Sides = new HashSet<string>(StringComparer.Ordinal) { "buy", "sell" };
    private static readonly IReadOnlySet<string> Statuses = new HashSet<string>(StringComparer.Ordinal)
    {
        "open", "filled", "cancelled", "expired",
    };

    public SocietyMarketOrderPayloadV2(
        PartitionRecordRefV1 marketRef,
        PartitionRecordRefV1 ownerRef,
        StableToken instrumentToken,
        StableToken side,
        long limitPriceMicrounit,
        long quantity,
        long remainingQuantity,
        ulong eligibleStep,
        StableToken status)
    {
        ValidateMarketRef(marketRef);
        if (ownerRef.RecordId.IsZero)
            throw new InvalidDataException("society.market-transaction-v2.order-owner-ref-zero");
        if (!Sides.Contains(side.Value))
            throw new InvalidDataException("society.market-transaction-v2.order-side");
        if (limitPriceMicrounit < 0)
            throw new InvalidDataException("society.market-transaction-v2.order-price-negative");
        if (quantity <= 0 || remainingQuantity < 0 || remainingQuantity > quantity)
            throw new InvalidDataException("society.market-transaction-v2.order-quantity");
        if (!Statuses.Contains(status.Value))
            throw new InvalidDataException("society.market-transaction-v2.order-status");
        if (status.Value == "filled" && remainingQuantity != 0)
            throw new InvalidDataException("society.market-transaction-v2.filled-remaining");
        if (status.Value == "open" && remainingQuantity == 0)
            throw new InvalidDataException("society.market-transaction-v2.open-remaining");

        MarketRef = marketRef;
        OwnerRef = ownerRef;
        InstrumentToken = instrumentToken;
        Side = side;
        LimitPriceMicrounit = limitPriceMicrounit;
        Quantity = quantity;
        RemainingQuantity = remainingQuantity;
        EligibleStep = eligibleStep;
        Status = status;
    }

    public override string RecordKind => SocietyMarketTransactionRecordSchemaV2.OrderOrOfferKind;
    public PartitionRecordRefV1 MarketRef { get; }
    public PartitionRecordRefV1 OwnerRef { get; }
    public StableToken InstrumentToken { get; }
    public StableToken Side { get; }
    public long LimitPriceMicrounit { get; }
    public long Quantity { get; }
    public long RemainingQuantity { get; }
    public ulong EligibleStep { get; }
    public StableToken Status { get; }

    private static void ValidateMarketRef(PartitionRecordRefV1 marketRef)
    {
        if (marketRef.PartitionId.Value != SocietyMarketTransactionRecordSchemaV2.PartitionId || marketRef.RecordId.IsZero)
            throw new InvalidDataException("society.market-transaction-v2.order-market-ref");
    }
}

public sealed class SocietyMarketFactPayloadV2 : SocietyMarketTransactionRecordPayloadV2
{
    public SocietyMarketFactPayloadV2(
        StableToken factKind,
        PartitionRecordRefV1 marketRef,
        StableToken instrumentToken,
        StableToken? orderSide,
        long? limitPriceMicrounit,
        long quantity,
        long? clearingPriceMicrounit,
        PartitionRecordRefV1? buyerRef,
        PartitionRecordRefV1? sellerRef,
        ulong eligibleStep,
        StableToken status)
    {
        if (!SocietyMarketTransactionRecordSchemaV2.FactKinds.Contains(factKind.Value, StringComparer.Ordinal))
            throw new InvalidDataException("society.market-transaction-v2.fact-kind");
        if (marketRef.PartitionId.Value != SocietyMarketTransactionRecordSchemaV2.PartitionId || marketRef.RecordId.IsZero)
            throw new InvalidDataException("society.market-transaction-v2.fact-market-ref");
        if (orderSide is { } side && side.Value is not ("buy" or "sell"))
            throw new InvalidDataException("society.market-transaction-v2.fact-order-side");
        if (limitPriceMicrounit is < 0 || clearingPriceMicrounit is < 0)
            throw new InvalidDataException("society.market-transaction-v2.fact-price-negative");
        if (quantity < 0)
            throw new InvalidDataException("society.market-transaction-v2.fact-quantity-negative");
        if (buyerRef is { RecordId.IsZero: true } || sellerRef is { RecordId.IsZero: true })
            throw new InvalidDataException("society.market-transaction-v2.fact-party-zero");
        if (factKind.Value == SocietyMarketTransactionRecordSchemaV2.TradeFactKind &&
            (quantity <= 0 || buyerRef is null || sellerRef is null))
            throw new InvalidDataException("society.market-transaction-v2.trade-contract");
        if (factKind.Value == SocietyMarketTransactionRecordSchemaV2.ClearingPriceFactKind && quantity != 0)
            throw new InvalidDataException("society.market-transaction-v2.price-fact-quantity");

        FactKind = factKind;
        MarketRef = marketRef;
        InstrumentToken = instrumentToken;
        OrderSide = orderSide;
        LimitPriceMicrounit = limitPriceMicrounit;
        Quantity = quantity;
        ClearingPriceMicrounit = clearingPriceMicrounit;
        BuyerRef = buyerRef;
        SellerRef = sellerRef;
        EligibleStep = eligibleStep;
        Status = status;
    }

    public override string RecordKind => SocietyMarketTransactionRecordSchemaV2.TransactionOrPriceFactKind;
    public StableToken FactKind { get; }
    public PartitionRecordRefV1 MarketRef { get; }
    public StableToken InstrumentToken { get; }
    public StableToken? OrderSide { get; }
    public long? LimitPriceMicrounit { get; }
    public long Quantity { get; }
    public long? ClearingPriceMicrounit { get; }
    public PartitionRecordRefV1? BuyerRef { get; }
    public PartitionRecordRefV1? SellerRef { get; }
    public ulong EligibleStep { get; }
    public StableToken Status { get; }
}

public sealed class SocietyMarketTransactionRecordMaterialV2
{
    public SocietyMarketTransactionRecordMaterialV2(
        OpaqueId128 recordId,
        ulong revision,
        ulong createdStep,
        ulong? retiredStep,
        DetailLevelV1 detailLevel,
        OpaqueId128? lineageRef,
        SocietyMarketTransactionRecordPayloadV2 payload)
    {
        if (recordId.IsZero) throw new ArgumentException("Record id ZERO is invalid.", nameof(recordId));
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (retiredStep is { } retired && retired < createdStep)
            throw new ArgumentOutOfRangeException(nameof(retiredStep));
        if (lineageRef is { IsZero: true })
            throw new ArgumentException("Lineage id ZERO is invalid.", nameof(lineageRef));
        if (!Enum.IsDefined(detailLevel)) throw new ArgumentOutOfRangeException(nameof(detailLevel));
        ArgumentNullException.ThrowIfNull(payload);
        RecordId = recordId;
        Revision = revision;
        CreatedStep = createdStep;
        RetiredStep = retiredStep;
        DetailLevel = detailLevel;
        LineageRef = lineageRef;
        Payload = payload;
    }

    public OpaqueId128 RecordId { get; }
    public SchemaRefV1 RecordSchema => SocietyMarketTransactionRecordSchemaV2.RecordSchema;
    public ulong Revision { get; }
    public ulong CreatedStep { get; }
    public ulong? RetiredStep { get; }
    public DetailLevelV1 DetailLevel { get; }
    public OpaqueId128? LineageRef { get; }
    public SocietyMarketTransactionRecordPayloadV2 Payload { get; }

    public static SocietyMarketTransactionRecordMaterialV2 MigrateLegacyFact(
        DomainRecordEnvelopeV1<SocietyMarketTransactionPayloadV1> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var standard = StandardDomainPartitionRegistry.Get(SocietyMarketTransactionRecordSchemaV2.PartitionId);
        if (source.RecordSchema != standard.RecordSchema)
            throw new InvalidDataException("society.market-transaction-v2.migration-source-schema");
        var payload = source.Payload;
        return new SocietyMarketTransactionRecordMaterialV2(
            source.RecordId,
            source.Revision,
            source.CreatedStep,
            source.RetiredStep,
            source.DetailLevel,
            source.LineageRef,
            new SocietyMarketFactPayloadV2(
                new StableToken(SocietyMarketTransactionRecordSchemaV2.LegacyV1FactKind),
                payload.MarketRef,
                payload.InstrumentToken,
                payload.OrderSide,
                payload.LimitPrice,
                payload.Quantity,
                payload.ClearingPrice,
                payload.BuyerRef,
                payload.SellerRef,
                payload.EligibleStep,
                payload.Status));
    }
}
