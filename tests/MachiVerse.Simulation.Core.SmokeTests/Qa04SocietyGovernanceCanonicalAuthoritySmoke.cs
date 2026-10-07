using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyGovernanceCanonicalAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyGovernanceCanonicalAuthorityV1.ValidateCanonicalContract();

        Require(Qa04SocietyGovernanceCanonicalAuthorityV1.OrganizationClass.Value == "perf.organization", "Organization benchmark token drifted.");
        Require(Qa04SocietyGovernanceCanonicalAuthorityV1.InstitutionKind.Value == "perf.institution", "Institution benchmark token drifted.");
        Require(Qa04SocietyGovernanceCanonicalAuthorityV1.DecisionMethod.Value == "perf.decision", "Decision benchmark token drifted.");
        Require(Qa04SocietyGovernanceCanonicalAuthorityV1.ContractKind.Value == "perf.contract", "Contract benchmark token drifted.");
        Require(Qa04SocietyGovernanceCanonicalAuthorityV1.ClaimToken.Value == "perf.claim", "Claim benchmark token drifted.");
        Require(Qa04SocietyGovernanceCanonicalAuthorityV1.AuthorityToken.Value == "perf.authority", "Authority benchmark token drifted.");
        Require(Qa04SocietyGovernanceCanonicalAuthorityV1.PermissionKind.Value == "perf.permission", "Permission benchmark token drifted.");
        Require(Qa04SocietyGovernanceCanonicalAuthorityV1.GenesisStep == 0, "Benchmark payload genesis Step must remain zero.");

        var resolver = new FixtureReferenceResolver();
        var residentSchema = StandardDomainPartitionRegistry.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).RecordSchema;
        var resident0 = Qa04SocietyGovernanceCanonicalAuthorityV1.ResolveResidentRef(0);
        resolver.Add(resident0, residentSchema);

        var polity = Qa04GovernancePolityMaterializerV1.Create(0, out _);
        var polityRef = new PartitionRecordRefV1(GovernancePolityPayloadV1.PartitionId, polity.RecordId);
        resolver.Add(polityRef, StandardDomainPartitionRegistry.Get(GovernancePolityPayloadV1.PartitionId).RecordSchema);

        var scope0 = Qa04SpatialTileScopeAuthorityV1.ScopeRef(0);
        resolver.Add(scope0, StandardDomainPartitionRegistry.Get(SpatialScopeRegistryPayloadV1.PartitionId).RecordSchema);

        var organization = Qa04SocietyGovernanceCanonicalAuthorityV1.CreateOrganization(0, out _);
        Require(organization.Payload.OrganizationClass == Qa04SocietyGovernanceCanonicalAuthorityV1.OrganizationClass &&
                organization.Payload.Lifecycle.Value == "active" && organization.Payload.FoundedStep == 0,
            "Canonical Organization authority did not preserve the decided benchmark genesis semantics.");

        var institution = Qa04SocietyGovernanceCanonicalAuthorityV1.CreateInstitution(0, resolver, out _);
        var institutionRef = new PartitionRecordRefV1(GovernanceInstitutionPayloadV1.PartitionId, institution.RecordId);
        resolver.Add(institutionRef, StandardDomainPartitionRegistry.Get(GovernanceInstitutionPayloadV1.PartitionId).RecordSchema);
        Require(institution.Payload.InstitutionKind == Qa04SocietyGovernanceCanonicalAuthorityV1.InstitutionKind &&
                institution.Payload.DecisionMethod == Qa04SocietyGovernanceCanonicalAuthorityV1.DecisionMethod &&
                institution.Payload.OfficeRefs.Count == 0 && institution.Payload.PolityRef == polityRef,
            "Canonical Institution authority did not preserve the decided benchmark genesis semantics.");

        var contract = Qa04SocietyGovernanceCanonicalAuthorityV1.CreateContractClaim(0, resolver, out _);
        Require(contract.Payload.ContractKind == Qa04SocietyGovernanceCanonicalAuthorityV1.ContractKind &&
                contract.Payload.PartyRefs.SequenceEqual(new[] { resident0 }),
            "Canonical ContractClaim authority did not bind the decided Resident party.");

        var informationClaim = Qa04SocietyGovernanceCanonicalAuthorityV1.CreateInformationClaim(0, resolver, out _);
        Require(informationClaim.Payload.ClaimantRef == resident0 &&
                informationClaim.Payload.ClaimToken == Qa04SocietyGovernanceCanonicalAuthorityV1.ClaimToken &&
                informationClaim.Payload.CreatedStep == 0,
            "Canonical InformationClaim authority did not bind the decided claimant/token/Step.");

        var publicAuthority = Qa04SocietyGovernanceCanonicalAuthorityV1.CreatePublicAuthority(0, resolver, out _);
        var publicAuthorityRef = new PartitionRecordRefV1(GovernancePublicAuthorityPayloadV1.PartitionId, publicAuthority.RecordId);
        resolver.Add(publicAuthorityRef, StandardDomainPartitionRegistry.Get(GovernancePublicAuthorityPayloadV1.PartitionId).RecordSchema);
        Require(publicAuthority.Payload.InstitutionRef == institutionRef &&
                publicAuthority.Payload.AuthorityTokens.SequenceEqual(new[] { Qa04SocietyGovernanceCanonicalAuthorityV1.AuthorityToken }) &&
                publicAuthority.Payload.EffectiveFrom == 0 &&
                publicAuthority.Payload.ScopeRefs.SequenceEqual(new[] { scope0 }),
            "Canonical PublicAuthority authority did not bind the decided Institution/token/Step.");

        var permission = Qa04SocietyGovernanceCanonicalAuthorityV1.CreatePermissionLicense(0, resolver, out _);
        Require(permission.Payload.AuthorityRef == publicAuthorityRef &&
                permission.Payload.PermissionKind == Qa04SocietyGovernanceCanonicalAuthorityV1.PermissionKind &&
                permission.Payload.EffectiveFrom == 0 &&
                permission.Payload.ScopeRefs.SequenceEqual(new[] { scope0 }),
            "Canonical PermissionLicense authority did not bind the decided PublicAuthority/token/Step.");

        Require(Qa04SocietyGovernanceCanonicalAuthorityV1.ResolveInstitutionRef(5_000) ==
                Qa04SocietyGovernanceCanonicalAuthorityV1.ResolveInstitutionRef(0),
            "Institution modulo mapping must wrap at 5,000.");
        Require(Qa04SocietyGovernanceCanonicalAuthorityV1.ResolvePublicAuthorityRef(25_000) ==
                Qa04SocietyGovernanceCanonicalAuthorityV1.ResolvePublicAuthorityRef(0),
            "PublicAuthority modulo mapping must wrap at 25,000.");

        var missingAuthorityRejected = false;
        try
        {
            _ = Qa04SocietyGovernanceCanonicalAuthorityV1.CreatePermissionLicense(
                0,
                new RejectPartitionReferenceResolver(resolver, GovernancePublicAuthorityPayloadV1.PartitionId),
                out _);
        }
        catch (InvalidDataException ex) when (
            ex.Message == "domain.payload.reference-validation:governance.permission_license:authority_ref")
        {
            missingAuthorityRejected = true;
        }
        Require(missingAuthorityRejected, "Canonical PermissionLicense must fail closed without actual PublicAuthority authority.");
    }

    private sealed class FixtureReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema) => _records[reference] = schema;
        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);
        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema) => _records.TryGetValue(reference, out schema);
    }

    private sealed class RejectPartitionReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly IDomainRecordSchemaResolverV1 _inner;
        private readonly string _partitionId;

        public RejectPartitionReferenceResolver(IDomainRecordSchemaResolverV1 inner, string partitionId)
        {
            _inner = inner;
            _partitionId = partitionId;
        }

        public bool Exists(PartitionRecordRefV1 reference) => reference.PartitionId.Value != _partitionId && _inner.Exists(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            if (reference.PartitionId.Value == _partitionId)
            {
                schema = default;
                return false;
            }
            return _inner.TryGetRecordSchema(reference, out schema);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
