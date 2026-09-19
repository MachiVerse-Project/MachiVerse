using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Regression guard for the resolved society.market_transaction.market_ref target-kind dependency.
/// The world blocker remains removed only while canonical Market material, ownership, v2 migration,
/// and market_state target identity continue to agree.
/// </summary>
public static class Qa04MarketRefTargetDependencyContractV1
{
    public const string ParentWorldDependencyId = "society.market-transaction.market-ref-target";
    public const string ParentWorldFailureCode = "qa04.material.market-ref-authority-undefined";

    public static void ValidateCanonicalContract()
    {
        Qa04MarketMaterializerV1.ValidateCanonicalContract();
        SocietyMarketTransactionRecordSchemaV2.ValidateCanonicalContract();

        var migration = StandardDomainRecordSchemaMigrationRegistryV1.Get(SocietyMarketTransactionRecordSchemaV2.PartitionId);
        if (migration.SourceRecordSchema.Version != new SchemaVersionV1(1, 0) ||
            migration.TargetRecordSchema != SocietyMarketTransactionRecordSchemaV2.RecordSchema)
            throw new InvalidDataException("qa04.market-ref.migration-drift");

        var closure = Qa04ReferenceWorldRefOwnershipContractV1.RefClosures.SingleOrDefault(
            static value => value.DependencyId.Value == ParentWorldDependencyId)
            ?? throw new InvalidDataException("qa04.market-ref.ownership-closure-missing");
        if (closure.SourcePartitionId.Value != SocietyMarketTransactionRecordSchemaV2.PartitionId ||
            !string.Equals(closure.SourceFieldName, "market_ref", StringComparison.Ordinal) ||
            closure.TargetPartitionId.Value != SocietyMarketTransactionRecordSchemaV2.PartitionId ||
            closure.TargetRecordKind.Value != SocietyMarketTransactionRecordSchemaV2.MarketStateKind)
            throw new InvalidDataException("qa04.market-ref.ownership-closure-drift");

        if (Qa04MarketMaterializerV1.CanonicalMarketStateCount != 100 ||
            Qa04MarketMaterializerV1.CanonicalOrderCount != 1_000_000 ||
            Qa04MarketMaterializerV1.CanonicalRecordCount != 1_000_100)
            throw new InvalidDataException("qa04.market-ref.canonical-count-drift");

        var scopeRef = new PartitionRecordRefV1(
            "spatial.scope_registry",
            Qa04ReferenceScenariosV1.MarketScopeId(0));
        var state = Qa04MarketMaterializerV1.CreateMarketState(0, _ => scopeRef, out _);
        var order = Qa04MarketMaterializerV1.CreateOrder(0, out _);
        if (state.Payload.RecordKind != SocietyMarketTransactionRecordSchemaV2.MarketStateKind ||
            order.Payload is not SocietyMarketOrderPayloadV2 orderPayload ||
            orderPayload.MarketRef.PartitionId.Value != SocietyMarketTransactionRecordSchemaV2.PartitionId ||
            orderPayload.MarketRef.RecordId != state.RecordId)
            throw new InvalidDataException("qa04.market-ref.canonical-target-kind-drift");

        if (Qa04ReferenceWorldDependencyContractV1.Blockers.Any(
                static blocker => blocker.DependencyId.Value == ParentWorldDependencyId ||
                                  blocker.FailureCode.Value == ParentWorldFailureCode))
            throw new InvalidDataException("qa04.market-ref.parent-world-blocker-still-present");
    }
}
