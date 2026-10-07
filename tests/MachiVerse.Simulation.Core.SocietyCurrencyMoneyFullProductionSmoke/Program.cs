using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectInvalid(Action action, string message)
{
    try
    {
        action();
    }
    catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static DomainRecordEnvelopeV1<SocietyCurrencyMoneyPayloadV1> CopyWithPayload(
    DomainRecordEnvelopeV1<SocietyCurrencyMoneyPayloadV1> record,
    SocietyCurrencyMoneyPayloadV1 payload)
    => new(
        record.RecordId,
        record.RecordSchema,
        record.Revision,
        record.CreatedStep,
        record.RetiredStep,
        record.DetailLevel,
        record.LineageRef,
        payload);

Console.WriteLine("Validating approved Society CurrencyMoney authority (100 records)...");
Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateCanonicalContract();
var materialization = Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.MaterializeCanonical();
var records = materialization.RecordsByOrdinal;

Require(materialization.MaterializedRecordCount == 100 &&
        materialization.Currencies.ItemCount == 100 &&
        materialization.Organizations.ItemCount == 10_000 &&
        records.Count == 100,
    "Society CurrencyMoney production materialization counts drifted.");
Require(records.Select(static record => record.Payload.CurrencyToken.Value).Distinct(StringComparer.Ordinal).Count() == 100,
    "Society CurrencyMoney must use exactly 100 unique currency tokens.");
Require(records.Select(static record => record.Payload.IssuerRef).Distinct().Count() == 100,
    "Society CurrencyMoney must bind 100 distinct actual Organization issuers.");
Require(records[0].Payload.CurrencyToken.Value == "perf.currency-000" &&
        records[^1].Payload.CurrencyToken.Value == "perf.currency-099",
    "Society CurrencyMoney exact token vocabulary drifted.");
Require(records.All(static record =>
        record.Payload.SupplyMicrounit == 0 &&
        record.Payload.Status.Value == "active" &&
        record.Payload.PolicyRefs.Count == 0 &&
        record.Payload.UnitScale == 6),
    "Society CurrencyMoney approved benchmark payload drifted.");

Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateSecondaryIndex(materialization.Currencies);
var recovered = Qa04SocietyCurrencyMoneySnapshotRecoveryEvidenceV1.Verify(materialization);
Require(recovered == 100,
    "Society CurrencyMoney Snapshot/recovery must semantically recover all 100 records.");

var first = records[0];
ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
        SocietyCurrencyMoneyPayloadV1.PartitionId,
        first.Payload.ToStandardPayload(),
        new EmptyReferenceResolver()),
    "CurrencyMoney must fail closed without upstream Organization authority.");
ExpectInvalid(() => Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { IssuerRef = records[1].Payload.IssuerRef }),
        materialization.References),
    "CurrencyMoney wrong issuer mapping must fail closed.");
ExpectInvalid(() => Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with
        {
            IssuerRef = new PartitionRecordRefV1(SocietyCurrencyMoneyPayloadV1.PartitionId, first.Payload.IssuerRef.RecordId),
        }),
        materialization.References),
    "CurrencyMoney wrong issuer partition must fail closed.");
ExpectInvalid(() => Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { CurrencyToken = new StableToken("perf.currency-999") }),
        materialization.References),
    "CurrencyMoney wrong token must fail closed.");
ExpectInvalid(() => Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { SupplyMicrounit = 1 }),
        materialization.References),
    "CurrencyMoney supply drift must fail closed.");
ExpectInvalid(() => Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { Status = new StableToken("inactive") }),
        materialization.References),
    "CurrencyMoney status drift must fail closed.");
ExpectInvalid(() => Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { PolicyRefs = new[] { first.Payload.IssuerRef } }),
        materialization.References),
    "CurrencyMoney policy injection must fail closed.");
ExpectInvalid(() => Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { UnitScale = 5 }),
        materialization.References),
    "CurrencyMoney unit-scale drift must fail closed.");

var duplicateToken = records.ToArray();
duplicateToken[1] = CopyWithPayload(duplicateToken[1], duplicateToken[1].Payload with
{
    CurrencyToken = duplicateToken[0].Payload.CurrencyToken,
});
ExpectInvalid(() => Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateUniqueness(duplicateToken),
    "Duplicate CurrencyMoney token must fail closed at full population.");

var duplicateIssuer = records.ToArray();
duplicateIssuer[1] = CopyWithPayload(duplicateIssuer[1], duplicateIssuer[1].Payload with
{
    IssuerRef = duplicateIssuer[0].Payload.IssuerRef,
});
ExpectInvalid(() => Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateUniqueness(duplicateIssuer),
    "Duplicate CurrencyMoney issuer must fail closed at full population.");

var duplicateId = records.ToArray();
duplicateId[1] = new DomainRecordEnvelopeV1<SocietyCurrencyMoneyPayloadV1>(
    duplicateId[0].RecordId,
    duplicateId[1].RecordSchema,
    duplicateId[1].Revision,
    duplicateId[1].CreatedStep,
    duplicateId[1].RetiredStep,
    duplicateId[1].DetailLevel,
    duplicateId[1].LineageRef,
    duplicateId[1].Payload);
ExpectInvalid(() => Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateUniqueness(duplicateId),
    "Duplicate CurrencyMoney RecordId must fail closed at full population.");

Console.WriteLine($"society-currency-money-full-production-pass records={materialization.MaterializedRecordCount} recovered={recovered} currencies={records.Select(static record => record.Payload.CurrencyToken.Value).Distinct(StringComparer.Ordinal).Count()} issuers={records.Select(static record => record.Payload.IssuerRef).Distinct().Count()} unitScale={Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.UnitScale}");

sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
{
    public bool Exists(PartitionRecordRefV1 reference) => false;

    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
    {
        schema = default;
        return false;
    }
}
