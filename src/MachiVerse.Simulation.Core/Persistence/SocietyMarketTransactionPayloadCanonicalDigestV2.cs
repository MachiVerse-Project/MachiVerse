using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public static class SocietyMarketTransactionPayloadCanonicalDigestV2
{
    public static byte[] Compute(
        SocietyMarketTransactionRecordPayloadV2 payload,
        IDomainRecordSchemaResolverV1? references = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        SocietyMarketTransactionRecordSchemaV2.ValidateCanonicalContract();
        return HashSuite.DomainHash(StandardDomainPayloadCanonicalDigestV1.HashDomain, writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteAsciiText(SocietyMarketTransactionRecordSchemaV2.PartitionId);
            writer.WriteUnsigned(1); writer.WriteAsciiText(SocietyMarketTransactionRecordSchemaV2.RecordSchema.SchemaId.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(SocietyMarketTransactionRecordSchemaV2.RecordSchema.Version.Major);
            writer.WriteUnsigned(3); writer.WriteUnsigned(SocietyMarketTransactionRecordSchemaV2.RecordSchema.Version.Minor);
            writer.WriteUnsigned(4); WritePayload(writer, payload, references);
        });
    }

    private static void WritePayload(
        MvDcborWriter writer,
        SocietyMarketTransactionRecordPayloadV2 payload,
        IDomainRecordSchemaResolverV1? references)
    {
        switch (payload)
        {
            case SocietyMarketStatePayloadV2 state:
                writer.WriteArrayStart(8);
                Field(writer, 0, w => w.WriteAsciiText(state.RecordKind));
                Field(writer, 1, w => WriteReference(w, state.ScopeRef, references, "scope_ref",
                    StandardDomainPartitionRegistry.Get("spatial.scope_registry").RecordSchema));
                Field(writer, 2, w => w.WriteAsciiText(state.InstrumentToken.Value));
                Field(writer, 3, w => w.WriteAsciiText(state.CurrencyToken.Value));
                Field(writer, 4, w => w.WriteAsciiText(state.Status.Value));
                Field(writer, 5, w => w.WriteUnsigned(state.ClearingCadenceSteps));
                Field(writer, 6, w => WriteOptionalUnsigned(w, state.LastClearingStep));
                Field(writer, 7, w => WriteOptionalInt64(w, state.LastClearingPriceMicrounit));
                break;
            case SocietyMarketOrderPayloadV2 order:
                writer.WriteArrayStart(10);
                Field(writer, 0, w => w.WriteAsciiText(order.RecordKind));
                Field(writer, 1, w => WriteReference(w, order.MarketRef, references, "market_ref",
                    SocietyMarketTransactionRecordSchemaV2.RecordSchema));
                Field(writer, 2, w => WriteReference(w, order.OwnerRef, references, "owner_ref", null));
                Field(writer, 3, w => w.WriteAsciiText(order.InstrumentToken.Value));
                Field(writer, 4, w => w.WriteAsciiText(order.Side.Value));
                Field(writer, 5, w => w.WriteInt64(order.LimitPriceMicrounit));
                Field(writer, 6, w => w.WriteInt64(order.Quantity));
                Field(writer, 7, w => w.WriteInt64(order.RemainingQuantity));
                Field(writer, 8, w => w.WriteUnsigned(order.EligibleStep));
                Field(writer, 9, w => w.WriteAsciiText(order.Status.Value));
                break;
            case SocietyMarketFactPayloadV2 fact:
                writer.WriteArrayStart(12);
                Field(writer, 0, w => w.WriteAsciiText(fact.RecordKind));
                Field(writer, 1, w => w.WriteAsciiText(fact.FactKind.Value));
                Field(writer, 2, w => WriteReference(w, fact.MarketRef, references, "market_ref",
                    SocietyMarketTransactionRecordSchemaV2.RecordSchema));
                Field(writer, 3, w => w.WriteAsciiText(fact.InstrumentToken.Value));
                Field(writer, 4, w => WriteOptionalToken(w, fact.OrderSide));
                Field(writer, 5, w => WriteOptionalInt64(w, fact.LimitPriceMicrounit));
                Field(writer, 6, w => w.WriteInt64(fact.Quantity));
                Field(writer, 7, w => WriteOptionalInt64(w, fact.ClearingPriceMicrounit));
                Field(writer, 8, w => WriteOptionalReference(w, fact.BuyerRef, references, "buyer_ref"));
                Field(writer, 9, w => WriteOptionalReference(w, fact.SellerRef, references, "seller_ref"));
                Field(writer, 10, w => w.WriteUnsigned(fact.EligibleStep));
                Field(writer, 11, w => w.WriteAsciiText(fact.Status.Value));
                break;
            default:
                throw new InvalidDataException("domain.payload.digest-market-v2-kind");
        }
    }

    private static void WriteReference(
        MvDcborWriter writer,
        PartitionRecordRefV1 reference,
        IDomainRecordSchemaResolverV1? references,
        string field,
        SchemaRefV1? requiredSchema)
    {
        if (reference.RecordId.IsZero)
            throw new InvalidDataException($"domain.payload.digest-market-v2-ref-zero:{field}");
        _ = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value);
        if (references is not null)
        {
            if (!references.TryGetRecordSchema(reference, out var actual))
                throw new InvalidDataException($"domain.payload.digest-reference-missing:{SocietyMarketTransactionRecordSchemaV2.PartitionId}:{field}");
            if (requiredSchema is { } expected && actual != expected)
                throw new InvalidDataException($"domain.payload.digest-reference-schema:{SocietyMarketTransactionRecordSchemaV2.PartitionId}:{field}");
        }
        writer.WriteArrayStart(2);
        writer.WriteAsciiText(reference.PartitionId.Value);
        writer.WriteBytes(reference.RecordId.ToBytes());
    }

    private static void WriteOptionalReference(
        MvDcborWriter writer,
        PartitionRecordRefV1? reference,
        IDomainRecordSchemaResolverV1? references,
        string field)
    {
        writer.WriteArrayStart(reference.HasValue ? 2UL : 1UL);
        writer.WriteBoolean(reference.HasValue);
        if (reference is { } value) WriteReference(writer, value, references, field, null);
    }

    private static void WriteOptionalToken(MvDcborWriter writer, StableToken? value)
    {
        writer.WriteArrayStart(value.HasValue ? 2UL : 1UL);
        writer.WriteBoolean(value.HasValue);
        if (value is { } token) writer.WriteAsciiText(token.Value);
    }

    private static void WriteOptionalUnsigned(MvDcborWriter writer, ulong? value)
    {
        writer.WriteArrayStart(value.HasValue ? 2UL : 1UL);
        writer.WriteBoolean(value.HasValue);
        if (value is { } present) writer.WriteUnsigned(present);
    }

    private static void WriteOptionalInt64(MvDcborWriter writer, long? value)
    {
        writer.WriteArrayStart(value.HasValue ? 2UL : 1UL);
        writer.WriteBoolean(value.HasValue);
        if (value is { } present) writer.WriteInt64(present);
    }

    private static void Field(MvDcborWriter writer, ulong ordinal, Action<MvDcborWriter> value)
    {
        writer.WriteArrayStart(2);
        writer.WriteUnsigned(ordinal);
        value(writer);
    }
}
