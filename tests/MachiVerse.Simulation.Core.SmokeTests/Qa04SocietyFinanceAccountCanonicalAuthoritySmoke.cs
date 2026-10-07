using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyFinanceAccountCanonicalAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04SocietyFinanceAccountCanonicalAuthorityV1.MaterializeCanonical();
        var records = materialization.RecordsByOrdinal;

        Require(materialization.MaterializedRecordCount == 80_000 &&
                materialization.Accounts.ItemCount == 80_000 &&
                materialization.Currencies.ItemCount == 100 &&
                records.Count == 80_000,
            "Canonical society.finance_account authority must materialize exactly 80,000 records.");
        Require(records.Select(static record => record.Payload.OwnerRef).Distinct().Count() == 80_000,
            "FinanceAccount owners must be 80,000 distinct canonical Residents.");
        var currencyGroups = records.GroupBy(static record => record.Payload.CurrencyToken.Value, StringComparer.Ordinal).ToArray();
        Require(currencyGroups.Length == 100 && currencyGroups.All(static group => group.Count() == 800),
            "FinanceAccount currency distribution must be exactly 800 accounts per approved token.");
        Require(records.All(static record =>
                record.Payload.InstitutionRef is null &&
                record.Payload.BalanceMicrounit == 0 &&
                record.Payload.CreditLimitMicrounit == 0 &&
                record.Payload.Status.Value == "active" &&
                Qa04SocietyFinanceAccountCanonicalAuthorityV1.IsEmptyLedgerDigest(record.Payload.LedgerHeadDigest)),
            "FinanceAccount approved zero-posting genesis payload drifted.");

        Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateSecondaryIndexes(materialization.Accounts);
        var recovered = Qa04SocietyFinanceAccountSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(recovered == 80_000,
            "FinanceAccount Snapshot/recovery must semantically recover all 80,000 records.");

        var first = records[0];
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                SocietyFinanceAccountPayloadV1.PartitionId,
                first.Payload.ToStandardPayload(),
                new EmptyReferenceResolver()),
            "FinanceAccount must fail closed without Resident authority.");
        ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
                0,
                CopyWithPayload(first, first.Payload with { OwnerRef = records[1].Payload.OwnerRef }),
                materialization.References),
            "Wrong FinanceAccount owner mapping must fail closed.");
        ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
                0,
                CopyWithPayload(first, first.Payload with { CurrencyToken = new MachiVerse.Simulation.Core.Determinism.StableToken("perf.currency-999") }),
                materialization.References),
            "Wrong FinanceAccount currency token must fail closed.");
        ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
                0,
                CopyWithPayload(first, first.Payload with { InstitutionRef = first.Payload.OwnerRef }),
                materialization.References),
            "FinanceAccount institution injection must fail closed.");
        var wrongDigest = first.Payload.LedgerHeadDigest.ToArray();
        wrongDigest[^1] ^= 0x01;
        ExpectInvalid(() => Qa04SocietyFinanceAccountCanonicalAuthorityV1.ValidateCanonicalRecord(
                0,
                CopyWithPayload(first, first.Payload with { LedgerHeadDigest = wrongDigest }),
                materialization.References),
            "FinanceAccount ledger-head genesis sentinel drift must fail closed.");
    }

    private static DomainRecordEnvelopeV1<SocietyFinanceAccountPayloadV1> CopyWithPayload(
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

    private static void ExpectInvalid(Action action, string message)
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

    private sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => false;

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = default;
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
