using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04SocietyGovernanceCanonicalMaterializationV1
{
    internal Qa04SocietyGovernanceCanonicalMaterializationV1(
        DomainPartitionStateV1<SocietyOrganizationPayloadV1> organizations,
        DomainPartitionStateV1<GovernanceInstitutionPayloadV1> institutions,
        DomainPartitionStateV1<SocietyContractClaimPayloadV1> contractClaims,
        DomainPartitionStateV1<SocietyInformationClaimPayloadV1> informationClaims,
        DomainPartitionStateV1<GovernancePublicAuthorityPayloadV1> publicAuthorities,
        DomainPartitionStateV1<GovernancePermissionLicensePayloadV1> permissionLicenses)
    {
        Organizations = organizations;
        Institutions = institutions;
        ContractClaims = contractClaims;
        InformationClaims = informationClaims;
        PublicAuthorities = publicAuthorities;
        PermissionLicenses = permissionLicenses;
    }

    public DomainPartitionStateV1<SocietyOrganizationPayloadV1> Organizations { get; }
    public DomainPartitionStateV1<GovernanceInstitutionPayloadV1> Institutions { get; }
    public DomainPartitionStateV1<SocietyContractClaimPayloadV1> ContractClaims { get; }
    public DomainPartitionStateV1<SocietyInformationClaimPayloadV1> InformationClaims { get; }
    public DomainPartitionStateV1<GovernancePublicAuthorityPayloadV1> PublicAuthorities { get; }
    public DomainPartitionStateV1<GovernancePermissionLicensePayloadV1> PermissionLicenses { get; }

    public ulong MaterializedRecordCount
        => checked(
            Organizations.ItemCount +
            Institutions.ItemCount +
            ContractClaims.ItemCount +
            InformationClaims.ItemCount +
            PublicAuthorities.ItemCount +
            PermissionLicenses.ItemCount);
}

/// <summary>
/// Full production-path materialization for the six Society/Governance slices decided by
/// phase4-alpha11-society-governance-reference-authority.md.
///
/// Reference validation is backed only by records produced by canonical production materializers:
/// Resident, Polity and TileScope are seeded first; generated Institution and PublicAuthority records
/// are then registered before their downstream dependents are materialized. No smoke-only fixture
/// records or permissive exists-everywhere resolver participate in this chain.
/// </summary>
public static class Qa04SocietyGovernanceCanonicalMaterializerV1
{
    public const ulong CanonicalMaterializedCount =
        Qa04SocietyOrganizationResolvedMaterializerV1.CanonicalCount +
        Qa04GovernanceInstitutionResolvedMaterializerV1.CanonicalCount +
        Qa04SocietyContractClaimResolvedMaterializerV1.CanonicalCount +
        Qa04SocietyInformationClaimResolvedMaterializerV1.CanonicalCount +
        Qa04GovernancePublicAuthorityResolvedMaterializerV1.CanonicalCount +
        Qa04GovernancePermissionLicenseResolvedMaterializerV1.CanonicalCount;

    private const ulong RequiredResidentAuthorityCount = 70_000;

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();
        Qa04GovernancePolityMaterializerV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

        if (CanonicalMaterializedCount != 195_000 ||
            RequiredResidentAuthorityCount != 70_000 ||
            RequiredResidentAuthorityCount > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount)
            throw new InvalidDataException("qa04.society-governance.canonical-materialization-count-drift");
    }

    public static Qa04SocietyGovernanceCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();

        var resolver = new CanonicalReferenceResolver();
        SeedResidentAuthority(resolver);
        SeedPolityAuthority(resolver);
        SeedTileScopeAuthority(resolver);

        var organizationRecords = Qa04SocietyOrganizationResolvedMaterializerV1.MaterializeResolved(
                static _ => Qa04SocietyGovernanceCanonicalAuthorityV1.OrganizationClass)
            .ToArray();
        var organizations = Partition(SocietyOrganizationPayloadV1.PartitionId, organizationRecords);

        var institutionRecords = Qa04GovernanceInstitutionResolvedMaterializerV1.MaterializeResolved(
                static _ => new Qa04GovernanceInstitutionResolvedAuthorityV1(
                    Qa04SocietyGovernanceCanonicalAuthorityV1.InstitutionKind,
                    Array.Empty<PartitionRecordRefV1>(),
                    Qa04SocietyGovernanceCanonicalAuthorityV1.DecisionMethod),
                resolver)
            .ToArray();
        resolver.AddAll(GovernanceInstitutionPayloadV1.PartitionId, institutionRecords);
        var institutions = Partition(GovernanceInstitutionPayloadV1.PartitionId, institutionRecords);

        var contractClaimRecords = Qa04SocietyContractClaimResolvedMaterializerV1.MaterializeResolved(
                localOrdinal => new Qa04SocietyContractClaimResolvedAuthorityV1(
                    Qa04SocietyGovernanceCanonicalAuthorityV1.ContractKind,
                    new[] { Qa04SocietyGovernanceCanonicalAuthorityV1.ResolveResidentRef(localOrdinal) }),
                resolver)
            .ToArray();
        var contractClaims = Partition(SocietyContractClaimPayloadV1.PartitionId, contractClaimRecords);

        var informationClaimRecords = Qa04SocietyInformationClaimResolvedMaterializerV1.MaterializeResolved(
                localOrdinal => new Qa04SocietyInformationClaimResolvedAuthorityV1(
                    Qa04SocietyGovernanceCanonicalAuthorityV1.ResolveResidentRef(localOrdinal),
                    Qa04SocietyGovernanceCanonicalAuthorityV1.ClaimToken,
                    Qa04SocietyGovernanceCanonicalAuthorityV1.GenesisStep),
                resolver)
            .ToArray();
        var informationClaims = Partition(SocietyInformationClaimPayloadV1.PartitionId, informationClaimRecords);

        var publicAuthorityRecords = Qa04GovernancePublicAuthorityResolvedMaterializerV1.MaterializeResolved(
                localOrdinal => new Qa04GovernancePublicAuthorityResolvedAuthorityV1(
                    Qa04SocietyGovernanceCanonicalAuthorityV1.ResolveInstitutionRef(localOrdinal),
                    new[] { Qa04SocietyGovernanceCanonicalAuthorityV1.AuthorityToken },
                    Qa04SocietyGovernanceCanonicalAuthorityV1.GenesisStep),
                resolver)
            .ToArray();
        resolver.AddAll(GovernancePublicAuthorityPayloadV1.PartitionId, publicAuthorityRecords);
        var publicAuthorities = Partition(GovernancePublicAuthorityPayloadV1.PartitionId, publicAuthorityRecords);

        var permissionLicenseRecords = Qa04GovernancePermissionLicenseResolvedMaterializerV1.MaterializeResolved(
                localOrdinal => new Qa04GovernancePermissionLicenseResolvedAuthorityV1(
                    Qa04SocietyGovernanceCanonicalAuthorityV1.ResolvePublicAuthorityRef(localOrdinal),
                    Qa04SocietyGovernanceCanonicalAuthorityV1.PermissionKind,
                    Qa04SocietyGovernanceCanonicalAuthorityV1.GenesisStep),
                resolver)
            .ToArray();
        var permissionLicenses = Partition(GovernancePermissionLicensePayloadV1.PartitionId, permissionLicenseRecords);

        var materialization = new Qa04SocietyGovernanceCanonicalMaterializationV1(
            organizations,
            institutions,
            contractClaims,
            informationClaims,
            publicAuthorities,
            permissionLicenses);
        if (materialization.MaterializedRecordCount != CanonicalMaterializedCount)
            throw new InvalidDataException("qa04.society-governance.canonical-materialization-total-mismatch");

        return materialization;
    }

    private static void SeedResidentAuthority(CanonicalReferenceResolver resolver)
    {
        for (ulong ordinal = 0; ordinal < RequiredResidentAuthorityCount; ordinal++)
        {
            var record = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(ordinal);
            resolver.Add(
                new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, record.RecordId),
                record.RecordSchema);
        }
    }

    private static void SeedPolityAuthority(CanonicalReferenceResolver resolver)
    {
        for (ulong ordinal = 0; ordinal < Qa04GovernancePolityMaterializerV1.CanonicalCount; ordinal++)
        {
            var record = Qa04GovernancePolityMaterializerV1.Create(ordinal, out _);
            resolver.Add(
                new PartitionRecordRefV1(GovernancePolityPayloadV1.PartitionId, record.RecordId),
                record.RecordSchema);
        }
    }

    private static void SeedTileScopeAuthority(CanonicalReferenceResolver resolver)
    {
        for (ushort tile = 0; tile < Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount; tile++)
        {
            var record = Qa04SpatialTileScopeAuthorityV1.MaterializeTile(tile);
            resolver.Add(
                new PartitionRecordRefV1(SpatialScopeRegistryPayloadV1.PartitionId, record.RecordId),
                record.RecordSchema);
        }
    }

    private static DomainPartitionStateV1<TPayload> Partition<TPayload>(
        string partitionId,
        IReadOnlyList<DomainRecordEnvelopeV1<TPayload>> records)
        => new(StandardDomainPartitionRegistry.Get(partitionId), records);

    private sealed class CanonicalReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.society-governance.canonical-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.society-governance.canonical-reference-schema-conflict");
        }

        public void AddAll<TPayload>(
            string partitionId,
            IEnumerable<DomainRecordEnvelopeV1<TPayload>> records)
        {
            foreach (var record in records)
                Add(new PartitionRecordRefV1(partitionId, record.RecordId), record.RecordSchema);
        }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
