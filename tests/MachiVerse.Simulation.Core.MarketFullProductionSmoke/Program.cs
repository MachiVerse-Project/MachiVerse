using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static PartitionRecordRefV1 Scope(ushort tile)
{
    var id = HashSuite.Trunc128(HashSuite.DomainHash("mv.qa04-market-smoke-tile-scope.v1", writer =>
    {
        writer.WriteMapStart(1);
        writer.WriteUnsigned(0); writer.WriteUnsigned(tile);
    }));
    if (id.IsZero) throw new InvalidOperationException("Market full materialization scope id unexpectedly became ZERO.");
    return new PartitionRecordRefV1("spatial.scope_registry", id);
}

Qa04MarketMaterializerV1.ValidateCanonicalContract();

var marketIds = new OpaqueId128[checked((int)Qa04MarketMaterializerV1.CanonicalMarketStateCount)];
var seenIds = new HashSet<OpaqueId128>();
ulong stateCount = 0;
ulong orderCount = 0;
ulong totalCount = 0;

Console.WriteLine($"Validating full canonical Market materialization ({Qa04MarketMaterializerV1.CanonicalRecordCount:N0} records)...");

foreach (var record in Qa04MarketMaterializerV1.MaterializeCanonical(Scope))
{
    Require(record.RecordSchema == SocietyMarketTransactionRecordSchemaV2.RecordSchema &&
            record.Revision == 1 && record.CreatedStep == 0 && record.RetiredStep is null &&
            record.DetailLevel == DetailLevelV1.D2RegionalAggregate && record.LineageRef is null,
        $"QA-04 Market canonical stream envelope drifted at ordinal {totalCount}.");
    Require(seenIds.Add(record.RecordId),
        $"QA-04 Market canonical stream emitted duplicate RecordId at ordinal {totalCount}.");

    if (record.Payload is SocietyMarketStatePayloadV2 state)
    {
        Require(totalCount < Qa04MarketMaterializerV1.CanonicalMarketStateCount,
            "QA-04 Market canonical stream emitted market_state after the state prefix.");
        var scope = checked((int)stateCount);
        marketIds[scope] = record.RecordId;
        Require(state.ScopeRef.PartitionId.Value == "spatial.scope_registry" &&
                !state.ScopeRef.RecordId.IsZero && state.Status.Value == "active" &&
                state.ClearingCadenceSteps == 30,
            $"QA-04 Market canonical market_state payload drifted at scope {scope}.");
        stateCount++;
    }
    else if (record.Payload is SocietyMarketOrderPayloadV2 order)
    {
        Require(totalCount >= Qa04MarketMaterializerV1.CanonicalMarketStateCount,
            "QA-04 Market canonical stream emitted order before all market_state records.");
        var scope = checked((int)(orderCount / 10_000UL));
        Require(scope < marketIds.Length && marketIds[scope] != default,
            $"QA-04 Market order targets an unmaterialized market_state at order {orderCount}.");
        Require(order.MarketRef.PartitionId.Value == SocietyMarketTransactionRecordSchemaV2.PartitionId &&
                order.MarketRef.RecordId == marketIds[scope],
            $"QA-04 Market order market_ref target-kind closure drifted at order {orderCount}.");
        Require(order.OwnerRef.PartitionId.Value == "resident.identity_lifecycle" &&
                !order.OwnerRef.RecordId.IsZero && order.Status.Value == "open" &&
                order.Quantity > 0 && order.RemainingQuantity == order.Quantity,
            $"QA-04 Market canonical order payload drifted at order {orderCount}.");
        orderCount++;
    }
    else
    {
        throw new InvalidOperationException(
            $"QA-04 Market canonical stream emitted unsupported payload at ordinal {totalCount}.");
    }

    totalCount++;
}

Require(stateCount == Qa04MarketMaterializerV1.CanonicalMarketStateCount,
    $"QA-04 Market canonical stream market_state count drifted: {stateCount}.");
Require(orderCount == Qa04MarketMaterializerV1.CanonicalOrderCount,
    $"QA-04 Market canonical stream order count drifted: {orderCount}.");
Require(totalCount == Qa04MarketMaterializerV1.CanonicalRecordCount &&
        (ulong)seenIds.Count == Qa04MarketMaterializerV1.CanonicalRecordCount,
    $"QA-04 Market canonical stream must contain exactly {Qa04MarketMaterializerV1.CanonicalRecordCount} unique records; actual={totalCount}, unique={seenIds.Count}.");

Console.WriteLine("Market full canonical materialization: PASS");
