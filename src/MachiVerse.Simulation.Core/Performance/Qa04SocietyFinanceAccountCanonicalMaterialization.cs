using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04SocietyFinanceAccountCanonicalMaterializationV1
{
    internal Qa04SocietyFinanceAccountCanonicalMaterializationV1(
        DomainPartitionStateV1<SocietyCurrencyMoneyPayloadV1> currencies,
        DomainPartitionStateV1<SocietyFinanceAccountPayloadV1> accounts,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyFinanceAccountPayloadV1>> recordsByOrdinal,
        IDomainRecordSchemaResolverV1 references)
    {
        Currencies = currencies;
        Accounts = accounts;
        RecordsByOrdinal = recordsByOrdinal;
        References = references;
    }

    public DomainPartitionStateV1<SocietyCurrencyMoneyPayloadV1> Currencies { get; }
    public DomainPartitionStateV1<SocietyFinanceAccountPayloadV1> Accounts { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyFinanceAccountPayloadV1>> RecordsByOrdinal { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public ulong MaterializedRecordCount => Accounts.ItemCount;
}

/// <summary>
/// Production-path materialization for the #240-approved perf.reference.v1 society.finance_account
/// 80,000-record package. Owners are actual canonical Residents, currencies come only from the
/// already-approved CurrencyMoney vocabulary, and the empty SHA-256 digest is a benchmark genesis
/// sentinel for zero postings rather than a general non-empty ledger-chain protocol.
/// </summary>
public static class Qa04SocietyFinanceAccountCanonicalAuthorityV1
{
    public const ulong CanonicalCount = 80_000;
    public const ulong CurrencyCount = Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.CanonicalCount;
    public const ulong AccountsPerCurrency = 800;
    public const long GenesisBalanceMicrounit = 0;
    public const long GenesisCreditLimitMicrounit = 0;

    public const string EmptyLedgerDigestHex =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public static readonly StableToken Active = new("active");
    private static readonly byte[] EmptyLedgerDigestValue = Convert.FromHexString(EmptyLedgerDigestHex);

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();
        Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyFinanceAccountPayloadV1.PartitionId);
        if (slice.StartOrdinal != 320_100 || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.finance-account-slice-drift");
        if (CanonicalCount != 80_000 || CurrencyCount != 100 || AccountsPerCurrency != 800 ||
            checked(CurrencyCount * AccountsPerCurrency) != CanonicalCount ||
            CanonicalCount > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount ||
            GenesisBalanceMicrounit != 0 || GenesisCreditLimitMicrounit != 0 || Active.Value != "active")
            throw new InvalidDataException("qa04.society.finance-account-contract-drift");

        var computedEmpty = SHA256.HashData(Array.Empty<byte>());
        if (EmptyLedgerDigestValue.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(computedEmpty, EmptyLedgerDigestValue))
            throw new InvalidDataException("qa04.society.finance-account-empty-ledger-digest-drift");

        var identity = StandardDomainPartitionRegistry.Get(SocietyFinanceAccountPayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != "society_economy")
            throw new InvalidDataException("qa04.society.finance-account-owner-drift");

        var indexes = StandardSecondaryIndexRegistry.ForPartition(SocietyFinanceAccountPayloadV1.PartitionId);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "society.account-by-owner",
            "society.account-by-currency",
        };
        if (indexes.Count != expected.Count ||
            indexes.Any(static index => index.Authority != IndexAuthorityV1.DerivedRebuildable) ||
            !expected.SetEquals(indexes.Select(static index => index.IndexId.Value)))
            throw new InvalidDataException("qa04.society.finance-account-secondary-index-registration-drift");
    }

    public static Qa04SocietyFinanceAccountCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();

        var currencyMaterialization = Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.MaterializeCanonical();
        if (currencyMaterialization.Currencies.ItemCount != CurrencyCount)
            throw new InvalidDataException("qa04.society.finance-account-currency-authority-count-drift");
        var approvedCurrencies = currencyMaterialization.RecordsByOrdinal
            .Select(static record => record.Payload.CurrencyToken.Value)
            .ToHashSet(StringComparer.Ordinal);
        if ((ulong)approvedCurrencies.Count != CurrencyCount)
            throw new InvalidDataException("qa04.society.finance-account-currency-authority-cardinality-drift");

        var references = new CanonicalReferenceResolver();
        var ownerRefs = new PartitionRecordRefV1[checked((int)CanonicalCount)];
        for (ulong a = 0; a < CanonicalCount; a++)
        {
            var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(a);
            var ownerRef = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
            references.Add(ownerRef, resident.RecordSchema);
            ownerRefs[checked((int)a)] = ownerRef;
        }

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var identity = StandardDomainPartitionRegistry.Get(SocietyFinanceAccountPayloadV1.PartitionId);
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyFinanceAccountPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<SocietyFinanceAccountPayloadV1>[checked((int)CanonicalCount)];

        for (ulong a = 0; a < CanonicalCount; a++)
        {
            var currency = Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.CurrencyToken(a % CurrencyCount);
            if (!approvedCurrencies.Contains(currency.Value))
                throw new InvalidDataException("qa04.society.finance-account-currency-authority-missing");

            var payload = new SocietyFinanceAccountPayloadV1(
                ownerRefs[checked((int)a)],
                InstitutionRef: null,
                currency,
                GenesisBalanceMicrounit,
                GenesisCreditLimitMicrounit,
                Active,
                EmptyLedgerDigest());
            validator.Validate(SocietyFinanceAccountPayloadV1.PartitionId, payload.ToStandardPayload(), references);

            var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + a));
            if (binding.PartitionId.Value != SocietyFinanceAccountPayloadV1.PartitionId ||
                binding.PartitionLocalOrdinal != a || binding.UsesSpecializedIdentity ||
                binding.Descriptor.DetailLevel != DetailLevelV1.D2RegionalAggregate)
                throw new InvalidDataException("qa04.society.finance-account-descriptor-binding-drift");

            var record = new DomainRecordEnvelopeV1<SocietyFinanceAccountPayloadV1>(
                binding.Descriptor.RecordId,
                identity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                binding.Descriptor.DetailLevel,
                lineageRef: null,
                payload);
            new FinanceAccountBalanceV1(
                record.RecordId,
                payload.CurrencyToken,
                payload.BalanceMicrounit,
                payload.CreditLimitMicrounit).Validate();
            records[checked((int)a)] = record;
        }

        ValidateInvariants(records);
        for (ulong a = 0; a < CanonicalCount; a++)
            ValidateCanonicalRecord(a, records[checked((int)a)], references);

        var accounts = new DomainPartitionStateV1<SocietyFinanceAccountPayloadV1>(identity, records);
        if (accounts.ItemCount != CanonicalCount)
            throw new InvalidDataException("qa04.society.finance-account-partition-count-drift");
        ValidateSecondaryIndexes(accounts);

        return new Qa04SocietyFinanceAccountCanonicalMaterializationV1(
            currencyMaterialization.Currencies,
            accounts,
            Array.AsReadOnly(records),
            references);
    }

    public static void ValidateCanonicalRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<SocietyFinanceAccountPayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyFinanceAccountPayloadV1.PartitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(localOrdinal);
        var expectedOwner = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
        var expectedCurrency = Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.CurrencyToken(localOrdinal % CurrencyCount);
        var identity = StandardDomainPartitionRegistry.Get(SocietyFinanceAccountPayloadV1.PartitionId);

        if (record.RecordId != binding.Descriptor.RecordId ||
            record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != binding.Descriptor.DetailLevel || record.LineageRef is not null)
            throw new InvalidDataException("qa04.society.finance-account-envelope-drift");

        var payload = record.Payload;
        if (payload.OwnerRef != expectedOwner || payload.InstitutionRef is not null ||
            payload.CurrencyToken != expectedCurrency ||
            payload.BalanceMicrounit != GenesisBalanceMicrounit ||
            payload.CreditLimitMicrounit != GenesisCreditLimitMicrounit ||
            payload.Status != Active || !IsEmptyLedgerDigest(payload.LedgerHeadDigest))
            throw new InvalidDataException("qa04.society.finance-account-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyFinanceAccountPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
        new FinanceAccountBalanceV1(
            record.RecordId,
            payload.CurrencyToken,
            payload.BalanceMicrounit,
            payload.CreditLimitMicrounit).Validate();
    }

    public static void ValidateInvariants(
        IEnumerable<DomainRecordEnvelopeV1<SocietyFinanceAccountPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var material = records.ToArray();
        if ((ulong)material.Length != CanonicalCount)
            throw new InvalidDataException("qa04.society.finance-account-count-drift");
        ValidateUniqueness(material);

        var currencyGroups = material
            .GroupBy(static record => record.Payload.CurrencyToken.Value, StringComparer.Ordinal)
            .ToArray();
        var expectedCurrencies = Enumerable.Range(0, checked((int)CurrencyCount))
            .Select(static ordinal => Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.CurrencyToken(checked((ulong)ordinal)).Value)
            .ToHashSet(StringComparer.Ordinal);
        if ((ulong)currencyGroups.Length != CurrencyCount ||
            currencyGroups.Any(group => group.Count() != checked((int)AccountsPerCurrency)) ||
            !expectedCurrencies.SetEquals(currencyGroups.Select(static group => group.Key)))
            throw new InvalidDataException("qa04.society.finance-account-currency-cardinality-drift");
    }

    public static void ValidateUniqueness(
        IEnumerable<DomainRecordEnvelopeV1<SocietyFinanceAccountPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var owners = new HashSet<PartitionRecordRefV1>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.society.finance-account-record-id-duplicate");
            if (!owners.Add(record.Payload.OwnerRef))
                throw new InvalidDataException("qa04.society.finance-account-owner-duplicate");
        }
    }

    public static void ValidateSecondaryIndexes(DomainPartitionStateV1<SocietyFinanceAccountPayloadV1> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var byOwner = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "society.account-by-owner",
            accounts,
            static record => new[] { record.Payload.OwnerRef.RecordId });
        var byCurrency = DerivedRecordIndexV1<string>.Rebuild(
            "society.account-by-currency",
            accounts,
            static record => new[] { record.Payload.CurrencyToken.Value },
            StringComparer.Ordinal);

        var ownerEntries = byOwner.CanonicalEntries.ToArray();
        if (ownerEntries.Length != checked((int)CanonicalCount) ||
            ownerEntries.Aggregate(0UL, static (sum, entry) => checked(sum + (ulong)entry.Value.Count)) != CanonicalCount ||
            ownerEntries.Any(static entry => entry.Value.Count != 1))
            throw new InvalidDataException("qa04.society.finance-account-by-owner-index-drift");

        var currencyEntries = byCurrency.CanonicalEntries.ToArray();
        if (currencyEntries.Length != checked((int)CurrencyCount) ||
            currencyEntries.Aggregate(0UL, static (sum, entry) => checked(sum + (ulong)entry.Value.Count)) != CanonicalCount ||
            currencyEntries.Any(entry => entry.Value.Count != checked((int)AccountsPerCurrency)))
            throw new InvalidDataException("qa04.society.finance-account-by-currency-index-drift");
    }

    public static byte[] EmptyLedgerDigest() => EmptyLedgerDigestValue.ToArray();

    public static bool IsEmptyLedgerDigest(byte[] digest)
        => digest.Length == EmptyLedgerDigestValue.Length &&
           CryptographicOperations.FixedTimeEquals(digest, EmptyLedgerDigestValue);

    private sealed class CanonicalReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.society.finance-account-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.society.finance-account-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
