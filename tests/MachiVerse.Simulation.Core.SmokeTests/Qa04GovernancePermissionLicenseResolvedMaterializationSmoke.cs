using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04GovernancePermissionLicenseResolvedMaterializationSmoke
{
    private static readonly StableToken FixtureInstitutionKind = new("fixture.institution-kind");
    private static readonly StableToken FixtureDecisionMethod = new("fixture.decision-method");
    private static readonly StableToken FixtureAuthorityToken = new("fixture.authority-token");
    private static readonly StableToken FixturePermissionA = new("fixture.permission-a");
    private static readonly StableToken FixturePermissionB = new("fixture.permission-b");

    [ModuleInitializer]
    internal static void Run()
    {
        Qa04GovernancePermissionLicenseResolvedMaterializerV1.ValidateCanonicalContract();

        var blockerCodes = Qa04GovernancePermissionLicenseDependencyContractV1.Blockers
            .Select(static blocker => blocker.FailureCode.Value)
            .ToHashSet(StringComparer.Ordinal);
        Require(blockerCodes.SetEquals(new[]
            {
                "qa04.material.permission-kind-vocabulary-undefined",
                "qa04.material.permission-public-authority-undefined",
                "qa04.material.permission-scope-mapping-undefined",
                "qa04.material.permission-effective-from-undefined",
            }),
            "Resolved PermissionLicense mechanics must not release the four canonical authority blockers.");

        var resolver = BuildFixtureResolver(out var publicAuthorityRef, out var scopeRef);
        var records = Qa04GovernancePermissionLicenseResolvedMaterializerV1.MaterializeResolved(
                localOrdinal => Authority(localOrdinal, publicAuthorityRef, scopeRef),
                resolver)
            .ToArray();

        Require(records.LongLength == checked((long)Qa04GovernancePermissionLicenseResolvedMaterializerV1.CanonicalCount),
            "Resolved PermissionLicense materialization must retain the canonical 70,000-record cardinality.");
        Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
            "Resolved PermissionLicense materialization must retain unique canonical descriptor RecordIds.");
        Require(records.Select(static record => record.Payload.SubjectRef).Distinct().Count() == records.Length,
            "The first 70,000 canonical PermissionLicense records must map to distinct Resident subjects.");

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(GovernancePermissionLicensePayloadV1.PartitionId);
        for (var index = 0; index < records.Length; index++)
        {
            var localOrdinal = checked((ulong)index);
            var descriptor = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
                checked(slice.StartOrdinal + localOrdinal));
            var record = records[index];
            var expectedAuthority = Authority(localOrdinal, publicAuthorityRef, scopeRef);
            var expectedSubject = Qa04GovernancePermissionLicenseResolvedMaterializerV1.ResolveCanonicalSubjectRef(localOrdinal);

            Require(record.RecordId == descriptor.Descriptor.RecordId &&
                    record.Revision == 1 && record.CreatedStep == 0 && record.RetiredStep is null &&
                    record.DetailLevel == DetailLevelV1.D2RegionalAggregate,
                "Resolved PermissionLicense descriptor identity/genesis envelope drifted.");
            Require(record.Payload.SubjectRef == expectedSubject &&
                    record.Payload.AuthorityRef == expectedAuthority.AuthorityRef &&
                    record.Payload.PermissionKind == expectedAuthority.PermissionKind &&
                    record.Payload.ScopeRefs.SequenceEqual(expectedAuthority.ScopeRefs) &&
                    record.Payload.EffectiveFrom == expectedAuthority.EffectiveFrom &&
                    record.Payload.EffectiveUntil is null &&
                    record.Payload.Status.Value == "active",
                "Resolved PermissionLicense must preserve supplied authority while using the canonical Resident subject selector.");
            Require(record.Payload.ConditionsDigest.SequenceEqual(
                    Qa04GovernancePermissionLicenseDependencyContractV1.ConditionsDigest(record.RecordId)),
                "Resolved PermissionLicense conditions digest must remain canonical and descriptor-derived.");
        }

        var partition = Qa04GovernancePermissionLicenseResolvedMaterializerV1.MaterializeResolvedPartition(
            localOrdinal => Authority(localOrdinal, publicAuthorityRef, scopeRef),
            resolver);
        Require(partition.ItemCount == Qa04GovernancePermissionLicenseResolvedMaterializerV1.CanonicalCount,
            "Resolved PermissionLicense partition must retain all 70,000 records.");

        var missingKindRejected = false;
        try
        {
            _ = Qa04GovernancePermissionLicenseResolvedMaterializerV1.CreateResolved(
                0,
                new Qa04GovernancePermissionLicenseResolvedAuthorityV1(
                    publicAuthorityRef,
                    default,
                    new[] { scopeRef },
                    EffectiveFrom: 0),
                resolver,
                out _);
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.governance.permission-license-kind-authority-required")
        {
            missingKindRejected = true;
        }
        Require(missingKindRejected,
            "Resolved PermissionLicense materialization must fail closed when permission_kind authority is absent.");

        var unresolvedSubjectRejected = false;
        try
        {
            _ = Qa04GovernancePermissionLicenseResolvedMaterializerV1.CreateResolved(
                0,
                Authority(0, publicAuthorityRef, scopeRef),
                new RejectResidentReferenceResolver(resolver),
                out _);
        }
        catch (InvalidDataException ex) when (
            ex.Message == "domain.payload.reference-validation:governance.permission_license:subject_ref")
        {
            unresolvedSubjectRejected = true;
        }
        Require(unresolvedSubjectRejected,
            "Resolved PermissionLicense materialization must fail closed when the canonical Resident subject is not resolvable.");
    }

    private static Qa04GovernancePermissionLicenseResolvedAuthorityV1 Authority(
        ulong localOrdinal,
        PartitionRecordRefV1 publicAuthorityRef,
        PartitionRecordRefV1 scopeRef)
        => new(
            publicAuthorityRef,
            (localOrdinal & 1UL) == 0 ? FixturePermissionA : FixturePermissionB,
            new[] { scopeRef },
            localOrdinal % 23UL);

    private static FixtureReferenceResolver BuildFixtureResolver(
        out PartitionRecordRefV1 publicAuthorityRef,
        out PartitionRecordRefV1 scopeRef)
    {
        var resolver = new FixtureReferenceResolver();

        var polity = Qa04GovernancePolityMaterializerV1.Create(0, out _);
        var polityRef = new PartitionRecordRefV1(GovernancePolityPayloadV1.PartitionId, polity.RecordId);
        resolver.Add(polityRef, StandardDomainPartitionRegistry.Get(GovernancePolityPayloadV1.PartitionId).RecordSchema);

        var institution = Qa04GovernanceInstitutionResolvedMaterializerV1.CreateResolved(
            0,
            new Qa04GovernanceInstitutionResolvedAuthorityV1(
                FixtureInstitutionKind,
                Array.Empty<PartitionRecordRefV1>(),
                FixtureDecisionMethod),
            resolver,
            out _);
        var institutionRef = new PartitionRecordRefV1(GovernanceInstitutionPayloadV1.PartitionId, institution.RecordId);
        resolver.Add(institutionRef, StandardDomainPartitionRegistry.Get(GovernanceInstitutionPayloadV1.PartitionId).RecordSchema);

        var residentSchema = StandardDomainPartitionRegistry.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).RecordSchema;
        var holderRef = Qa04GovernancePublicAuthorityResolvedMaterializerV1.ResolveCanonicalHolderRef(0);
        resolver.Add(holderRef, residentSchema);

        var scope = Qa04SpatialTileScopeAuthorityV1.MaterializeTile(0);
        scopeRef = new PartitionRecordRefV1(SpatialScopeRegistryPayloadV1.PartitionId, scope.RecordId);
        resolver.Add(scopeRef, StandardDomainPartitionRegistry.Get(SpatialScopeRegistryPayloadV1.PartitionId).RecordSchema);

        var publicAuthority = Qa04GovernancePublicAuthorityResolvedMaterializerV1.CreateResolved(
            0,
            new Qa04GovernancePublicAuthorityResolvedAuthorityV1(
                institutionRef,
                new[] { FixtureAuthorityToken },
                new[] { scopeRef },
                EffectiveFrom: 0),
            resolver,
            out _);
        publicAuthorityRef = new PartitionRecordRefV1(GovernancePublicAuthorityPayloadV1.PartitionId, publicAuthority.RecordId);
        resolver.Add(publicAuthorityRef, StandardDomainPartitionRegistry.Get(GovernancePublicAuthorityPayloadV1.PartitionId).RecordSchema);

        for (ulong localOrdinal = 0; localOrdinal < Qa04GovernancePermissionLicenseResolvedMaterializerV1.CanonicalCount; localOrdinal++)
        {
            var subjectRef = Qa04GovernancePermissionLicenseResolvedMaterializerV1.ResolveCanonicalSubjectRef(localOrdinal);
            resolver.Add(subjectRef, residentSchema);
        }

        return resolver;
    }

    private sealed class FixtureReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
            => _records[reference] = schema;

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }

    private sealed class RejectResidentReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly IDomainRecordSchemaResolverV1 _inner;

        public RejectResidentReferenceResolver(IDomainRecordSchemaResolverV1 inner)
            => _inner = inner;

        public bool Exists(PartitionRecordRefV1 reference)
            => reference.PartitionId.Value != ResidentIdentityLifecyclePayloadV1.PartitionId && _inner.Exists(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            if (reference.PartitionId.Value == ResidentIdentityLifecyclePayloadV1.PartitionId)
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
