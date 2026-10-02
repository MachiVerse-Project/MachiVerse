using System.Globalization;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04SocietyCurrencyMoneyCanonicalMaterializationV1
{
    internal Qa04SocietyCurrencyMoneyCanonicalMaterializationV1(
        DomainPartitionStateV1<SocietyOrganizationPayloadV1> organizations,
        DomainPartitionStateV1<SocietyCurrencyMoneyPayloadV1> currencies,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyCurrencyMoneyPayloadV1>> recordsByOrdinal,
        IDomainRecordSchemaResolverV1 references)
    {
        Organizations = organizations;
        Currencies = currencies;
        RecordsByOrdinal = recordsByOrdinal;
        References = references;
    }

    public DomainPartitionStateV1<SocietyOrganizationPayloadV1> Organizations { get; }
    public DomainPartitionStateV1<SocietyCurrencyMoneyPayloadV1> Currencies { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyCurrencyMoneyPayloadV1>> RecordsByOrdinal { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public ulong MaterializedRecordCount => Currencies.ItemCount;
}

/// <summary>
/// Production-path materialization for the #240-approved perf.reference.v1 society.currency_money
/// 100-record package. It consumes the already-accepted Organization materializer as issuer authority
/// and does not synthesize accounts, balances, policy, fiscal, exchange-rate, or settlement authority.
/// </summary>
public static class Qa04SocietyCurrencyMoneyCanonicalAuthorityV1
{
    public const ulong CanonicalCount = 100;
    public const ulong OrganizationCount = Qa04SocietyOrganizationResolvedMaterializerV1.CanonicalCount;
    public const long GenesisSupplyMicrounit = 0;
    public const uint UnitScale = 6;

    public static readonly StableToken Active = new("active");

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SocietyGovernanceCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04SocietyOrganizationResolvedMaterializerV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyCurrencyMoneyPayloadV1.PartitionId);
        if (slice.StartOrdinal != 320_000 || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.currency-money-slice-drift");
        if (CanonicalCount != 100 || OrganizationCount != 10_000 || CanonicalCount > OrganizationCount ||
            GenesisSupplyMicrounit != 0 || UnitScale != 6 || Active.Value != "active")
            throw new InvalidDataException("qa04.society.currency-money-contract-drift");

        var identity = StandardDomainPartitionRegistry.Get(SocietyCurrencyMoneyPayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != "society_economy")
            throw new InvalidDataException("qa04.society.currency-money-owner-drift");

        var indexes = StandardSecondaryIndexRegistry.ForPartition(SocietyCurrencyMoneyPayloadV1.PartitionId);
        if (indexes.Count != 1 || indexes[0].IndexId.Value != "society.currency-by-token" ||
            indexes[0].Authority != IndexAuthorityV1.DerivedRebuildable)
            throw new InvalidDataException("qa04.society.currency-money-secondary-index-registration-drift");

        var expectedTokens = Enumerable.Range(0, checked((int)CanonicalCount))
            .Select(static i => CurrencyToken(checked((ulong)i)).Value)
            .ToArray();
        if (expectedTokens.Distinct(StringComparer.Ordinal).Count() != checked((int)CanonicalCount) ||
            expectedTokens[0] != "perf.currency-000" || expectedTokens[^1] != "perf.currency-099")
            throw new InvalidDataException("qa04.society.currency-money-token-vocabulary-drift");
    }

    public static Qa04SocietyCurrencyMoneyCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();

        var organizationRecords = Qa04SocietyOrganizationResolvedMaterializerV1.MaterializeResolved(
                static _ => Qa04SocietyGovernanceCanonicalAuthorityV1.OrganizationClass)
            .ToArray();
        if ((ulong)organizationRecords.Length != OrganizationCount)
            throw new InvalidDataException("qa04.society.currency-money-organization-count-drift");

        var organizations = new DomainPartitionStateV1<SocietyOrganizationPayloadV1>(
            StandardDomainPartitionRegistry.Get(SocietyOrganizationPayloadV1.PartitionId),
            organizationRecords);
        var organizationById = organizationRecords.ToDictionary(static record => record.RecordId);
        var references = new CanonicalReferenceResolver();
        foreach (var organization in organizationRecords)
            references.Add(
                new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, organization.RecordId),
                organization.RecordSchema);

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var identity = StandardDomainPartitionRegistry.Get(SocietyCurrencyMoneyPayloadV1.PartitionId);
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyCurrencyMoneyPayloadV1.PartitionId);
        var organizationSlice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyOrganizationPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<SocietyCurrencyMoneyPayloadV1>[checked((int)CanonicalCount)];

        for (ulong c = 0; c < CanonicalCount; c++)
        {
            var organizationBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
                checked(organizationSlice.StartOrdinal + c));
            if (!organizationById.TryGetValue(organizationBinding.Descriptor.RecordId, out var organization))
                throw new InvalidDataException("qa04.society.currency-money-issuer-authority-missing");
            var issuerRef = new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, organization.RecordId);

            var payload = new SocietyCurrencyMoneyPayloadV1(
                CurrencyToken(c),
                issuerRef,
                GenesisSupplyMicrounit,
                Active,
                Array.Empty<PartitionRecordRefV1>(),
                UnitScale);
            validator.Validate(SocietyCurrencyMoneyPayloadV1.PartitionId, payload.ToStandardPayload(), references);

            var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + c));
            if (binding.PartitionId.Value != SocietyCurrencyMoneyPayloadV1.PartitionId ||
                binding.PartitionLocalOrdinal != c || binding.UsesSpecializedIdentity ||
                binding.Descriptor.DetailLevel != DetailLevelV1.D2RegionalAggregate)
                throw new InvalidDataException("qa04.society.currency-money-descriptor-binding-drift");

            records[checked((int)c)] = new DomainRecordEnvelopeV1<SocietyCurrencyMoneyPayloadV1>(
                binding.Descriptor.RecordId,
                identity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                binding.Descriptor.DetailLevel,
                lineageRef: null,
                payload);
        }

        ValidateInvariants(records);
        for (ulong c = 0; c < CanonicalCount; c++)
            ValidateCanonicalRecord(c, records[checked((int)c)], references);

        var currencies = new DomainPartitionStateV1<SocietyCurrencyMoneyPayloadV1>(identity, records);
        if (currencies.ItemCount != CanonicalCount)
            throw new InvalidDataException("qa04.society.currency-money-partition-count-drift");
        ValidateSecondaryIndex(currencies);

        return new Qa04SocietyCurrencyMoneyCanonicalMaterializationV1(
            organizations,
            currencies,
            Array.AsReadOnly(records),
            references);
    }

    public static void ValidateCanonicalRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<SocietyCurrencyMoneyPayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyCurrencyMoneyPayloadV1.PartitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        var organizationSlice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyOrganizationPayloadV1.PartitionId);
        var organizationBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(organizationSlice.StartOrdinal + localOrdinal));
        var expectedIssuer = new PartitionRecordRefV1(
            SocietyOrganizationPayloadV1.PartitionId,
            organizationBinding.Descriptor.RecordId);
        var identity = StandardDomainPartitionRegistry.Get(SocietyCurrencyMoneyPayloadV1.PartitionId);

        if (record.RecordId != binding.Descriptor.RecordId ||
            record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != binding.Descriptor.DetailLevel || record.LineageRef is not null)
            throw new InvalidDataException("qa04.society.currency-money-envelope-drift");

        var payload = record.Payload;
        if (payload.CurrencyToken != CurrencyToken(localOrdinal) ||
            payload.IssuerRef != expectedIssuer ||
            payload.SupplyMicrounit != GenesisSupplyMicrounit ||
            payload.Status != Active || payload.PolicyRefs.Count != 0 ||
            payload.UnitScale != UnitScale)
            throw new InvalidDataException("qa04.society.currency-money-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyCurrencyMoneyPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
    }

    public static void ValidateInvariants(
        IEnumerable<DomainRecordEnvelopeV1<SocietyCurrencyMoneyPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var material = records.ToArray();
        if ((ulong)material.Length != CanonicalCount)
            throw new InvalidDataException("qa04.society.currency-money-count-drift");
        ValidateUniqueness(material);
        if ((ulong)material.Select(static record => record.Payload.IssuerRef).Distinct().Count() != CanonicalCount)
            throw new InvalidDataException("qa04.society.currency-money-issuer-cardinality-drift");
    }

    public static void ValidateUniqueness(
        IEnumerable<DomainRecordEnvelopeV1<SocietyCurrencyMoneyPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var issuers = new HashSet<PartitionRecordRefV1>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.society.currency-money-record-id-duplicate");
            if (!tokens.Add(record.Payload.CurrencyToken.Value))
                throw new InvalidDataException("qa04.society.currency-money-token-duplicate");
            if (!issuers.Add(record.Payload.IssuerRef))
                throw new InvalidDataException("qa04.society.currency-money-issuer-duplicate");
        }
    }

    public static void ValidateSecondaryIndex(DomainPartitionStateV1<SocietyCurrencyMoneyPayloadV1> currencies)
    {
        ArgumentNullException.ThrowIfNull(currencies);
        var byToken = DerivedRecordIndexV1<string>.Rebuild(
            "society.currency-by-token",
            currencies,
            static record => new[] { record.Payload.CurrencyToken.Value },
            StringComparer.Ordinal);
        var entries = byToken.CanonicalEntries.ToArray();
        if (entries.Length != checked((int)CanonicalCount) ||
            entries.Aggregate(0UL, static (sum, entry) => checked(sum + (ulong)entry.Value.Count)) != CanonicalCount ||
            entries.Any(static entry => entry.Value.Count != 1))
            throw new InvalidDataException("qa04.society.currency-money-by-token-index-drift");
    }

    public static StableToken CurrencyToken(ulong localOrdinal)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        return new StableToken("perf.currency-" + localOrdinal.ToString("D3", CultureInfo.InvariantCulture));
    }

    private sealed class CanonicalReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.society.currency-money-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.society.currency-money-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
