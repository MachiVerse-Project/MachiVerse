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

static DomainRecordEnvelopeV1<SocietyFinanceAccountPayloadV1> CopyWithPayload(
    DomainRecordEnvelopeV1<SocietyFinanceAccountPayloadV1> record,
    SocietyFinanceAccountPayloadV1 payload)
    => new(
        record.RecordId,
        record.RecordSchema,
        record.Revision,
        record.CreatedStep,
        record.RetiredStep,
        record.DetailLevel,
        record.LineageRef,
        payload);

Console.WriteLine("Validating approved Society FinanceAccount authority (80,000 records)...");
Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalContract();
var materialization = Qa04SocietyFinanceAccountCanonicalAuthorityV1.MaterializeCanonical();
var records = materialization.RecordsByOrdinal;

Require(materialization.MaterializedRecordCount == 80_000 &&
        materialization.Accounts.ItemCount == 80_000 &&
        materialization.Currencies.ItemCount == 100 &&
        records.Count == 80_000,
    "Society FinanceAccount production materialization counts drifted.");
Require(records.Select(static record => record.Payload.OwnerRef).Distinct().Count() == 80_000,
    "Society FinanceAccount must bind exactly 80,000 distinct actual Resident owners.");
var currencyGroups = records.GroupBy(static record => record.Payload.CurrencyToken.Value, StringComparer.Ordinal).ToArray();
Require(currencyGroups.Length == 100 && currencyGroups.All(static group => group.Count() == 800),
    "Society FinanceAccount must bind exactly 800 accounts to each approved currency token.");
Require(records.All(static record =>
        record.Payload.InstitutionRef is null &&
        record.Payload.BalanceMicrounit == 0 &&
        record.Payload.CreditLimitMicrounit == 0 &&
        record.Payload.Status.Value == "active" &&
        Qa04SocietyFinanceAccountCanonicalAuthorityV1.IsEmptyLedgerDigest(record.Payload.LedgerHeadDigest)),
    "Society FinanceAccount approved benchmark genesis payload drifted.");

Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateSecondaryIndexes(materialization.Accounts);
var recovered = Qa04SocietyFinanceAccountSnapshotRecoveryEvidenceV1.Verify(materialization);
Require(recovered == 80_000,
    "Society FinanceAccount Snapshot/recovery must semantically recover all 80,000 records.");

var first = records[0];
ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
        SocietyFinanceAccountPayloadV1.PartitionId,
        first.Payload.ToStandardPayload(),
        new EmptyReferenceResolver()),
    "FinanceAccount must fail closed without upstream Resident authority.");
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { OwnerRef = records[1].Payload.OwnerRef }),
        materialization.References),
    "FinanceAccount wrong owner mapping must fail closed.");
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with
        {
            OwnerRef = new PartitionRecordRefV1(SocietyFinanceAccountPayloadV1.PartitionId, first.Payload.OwnerRef.RecordId),
        }),
        materialization.References),
    "FinanceAccount wrong owner partition must fail closed.");
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { InstitutionRef = first.Payload.OwnerRef }),
        materialization.References),
    "FinanceAccount institution injection must fail closed.");
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { CurrencyToken = new StableToken("perf.currency-999") }),
        materialization.References),
    "FinanceAccount non-approved currency must fail closed.");
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { BalanceMicrounit = 1 }),
        materialization.References),
    "FinanceAccount balance drift must fail closed.");
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { CreditLimitMicrounit = 1 }),
        materialization.References),
    "FinanceAccount credit-limit drift must fail closed.");
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { Status = new StableToken("inactive") }),
        materialization.References),
    "FinanceAccount status drift must fail closed.");
var wrongDigest = first.Payload.LedgerHeadDigest.ToArray();
wrongDigest[0] ^= 0x01;
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { LedgerHeadDigest = wrongDigest }),
        materialization.References),
    "FinanceAccount empty-ledger digest drift must fail closed.");

var duplicateOwner = records.ToArray();
duplicateOwner[1] = CopyWithPayload(duplicateOwner[1], duplicateOwner[1].Payload with
{
    OwnerRef = duplicateOwner[0].Payload.OwnerRef,
});
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateUniqueness(duplicateOwner),
    "Duplicate FinanceAccount owner must fail closed at full population.");

var duplicateId = records.ToArray();
duplicateId[1] = new DomainRecordEnvelopeV1<SocietyFinanceAccountPayloadV1>(
    duplicateId[0].RecordId,
    duplicateId[1].RecordSchema,
    duplicateId[1].Revision,
    duplicateId[1].CreatedStep,
    duplicateId[1].RetiredStep,
    duplicateId[1].DetailLevel,
    duplicateId[1].LineageRef,
    duplicateId[1].Payload);
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateUniqueness(duplicateId),
    "Duplicate FinanceAccount RecordId must fail closed at full population.");

var wrongDistribution = records.ToArray();
wrongDistribution[1] = CopyWithPayload(wrongDistribution[1], wrongDistribution[1].Payload with
{
    CurrencyToken = wrongDistribution[0].Payload.CurrencyToken,
});
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateInvariants(wrongDistribution),
    "FinanceAccount currency cardinality drift must fail closed at full population.");

var shortPopulation = records.Take(records.Count - 1).ToArray();
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateInvariants(shortPopulation),
    "FinanceAccount population cardinality drift must fail closed.");
var shortPartition = new DomainPartitionStateV1<SocietyFinanceAccountPayloadV1>(
    StandardDomainPartitionRegistry.Get(SocietyFinanceAccountPayloadV1.PartitionId),
    shortPopulation);
ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateSecondaryIndexes(shortPartition),
    "FinanceAccount secondary-index cardinality drift must fail closed.");

Console.WriteLine($"society-finance-account-full-production-pass records={materialization.MaterializedRecordCount} recovered={recovered} owners={records.Select(static record => record.Payload.OwnerRef).Distinct().Count()} currencies={currencyGroups.Length} accountsPerCurrency={Qa04SocietyFinanceAccountCanonicalAuthorityV1.AccountsPerCurrency} emptyLedgerDigest={Qa04SocietyFinanceAccountCanonicalAuthorityV1.EmptyLedgerDigestHex}");

sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
{
    public bool Exists(PartitionRecordRefV1 reference) => false;

    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
    {
        schema = default;
        return false;
    }
}
