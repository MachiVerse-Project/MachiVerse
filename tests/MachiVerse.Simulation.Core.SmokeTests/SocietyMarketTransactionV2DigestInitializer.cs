using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class SocietyMarketTransactionV2DigestInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var scope = Ref("spatial.scope_registry", "00000000000000000000000000b10001");
        var market = Ref("society.market_transaction", "00000000000000000000000000b10002");
        var owner = Ref("resident.identity_lifecycle", "00000000000000000000000000b10003");
        var resolver = new Resolver(new Dictionary<PartitionRecordRefV1, SchemaRefV1>
        {
            [scope] = StandardDomainPartitionRegistry.Get("spatial.scope_registry").RecordSchema,
            [market] = SocietyMarketTransactionRecordSchemaV2.RecordSchema,
            [owner] = StandardDomainPartitionRegistry.Get("resident.identity_lifecycle").RecordSchema,
        });

        var state = new SocietyMarketStatePayloadV2(
            scope, new StableToken("perf.instrument"), new StableToken("perf.currency"),
            new StableToken("active"), 30, null, null);
        var stateDigest = SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(state, resolver);
        Require(stateDigest.Length == 32 && stateDigest.SequenceEqual(
                SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(state, resolver)),
            "Market v2 state canonical digest must be deterministic.");

        var order = new SocietyMarketOrderPayloadV2(
            market, owner, new StableToken("perf.instrument"), new StableToken("buy"),
            100_000, 5, 5, 0, new StableToken("open"));
        var orderDigest = SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(order, resolver);
        Require(orderDigest.Length == 32 && !orderDigest.SequenceEqual(stateDigest),
            "Market v2 heterogeneous record arms must have distinct semantic digests.");

        var wrongMarketResolver = new Resolver(new Dictionary<PartitionRecordRefV1, SchemaRefV1>
        {
            [market] = StandardDomainPartitionRegistry.Get("society.market_transaction").RecordSchema,
            [owner] = StandardDomainPartitionRegistry.Get("resident.identity_lifecycle").RecordSchema,
        });
        ExpectInvalid(() => _ = SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(order, wrongMarketResolver));

        var missingOwnerResolver = new Resolver(new Dictionary<PartitionRecordRefV1, SchemaRefV1>
        {
            [market] = SocietyMarketTransactionRecordSchemaV2.RecordSchema,
        });
        ExpectInvalid(() => _ = SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(order, missingOwnerResolver));
    }

    private static PartitionRecordRefV1 Ref(string partitionId, string id)
        => new(partitionId, OpaqueId128.Parse(id));

    private static void ExpectInvalid(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("Expected Market v2 digest reference rejection.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Resolver(IReadOnlyDictionary<PartitionRecordRefV1, SchemaRefV1> schemas) : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => schemas.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => schemas.TryGetValue(reference, out schema);
    }
}
