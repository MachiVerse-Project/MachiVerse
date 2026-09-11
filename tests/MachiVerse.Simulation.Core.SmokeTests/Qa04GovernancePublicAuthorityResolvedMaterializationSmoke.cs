using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04GovernancePublicAuthorityResolvedMaterializationSmoke
{
    private static readonly StableToken FixtureInstitutionKind = new("fixture.institution-kind");
    private static readonly StableToken FixtureDecisionMethod = new("fixture.decision-method");
    private static readonly StableToken FixtureAuthorityA = new("fixture.authority-a");
    private static readonly StableToken FixtureAuthorityB = new("fixture.authority-b");

    [ModuleInitializer]
    internal static void Run()
    {
        Qa04GovernancePublicAuthorityResolvedMaterializerV1.ValidateCanonicalContract();

        var blockerCodes = Qa04GovernancePublicAuthorityDependencyContractV1.Blockers
            .Select(static blocker => blocker.FailureCode.Value)
            .ToHashSet(StringComparer.Ordinal);
        Require(blockerCodes.SetEquals(new[]
            {
                "qa04.material.public-authority-institution-undefined",
                "qa04.material.public-authority-token-vocabulary-undefined",
                "qa04.material.public-authority-scope-mapping-undefined",
                "qa04.material.public-authority-effective-from-undefined",
            }),
            "Resolved PublicAuthority mechanics must not release the four canonical authority blockers.");

        var resolver = BuildFixtureResolver(out var institutionRefs, out var scopeRefs);
        var records = Qa04GovernancePublicAuthorityResolvedMaterializerV1.MaterializeResolved(
                localOrdinal => Authority(localOrdinal, institutionRefs, scopeRefs),
                resolver)
            .ToArray();

        Require(records.LongLength == checked((long)Qa04GovernancePublicAuthorityResolvedMaterializerV1.CanonicalCount),
            "Resolved PublicAuthority materialization must retain the canonical 25,000-record cardinality.");
        Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
            "Resolved PublicAuthority materialization must retain unique canonical descriptor RecordIds.");
        Require(records.Select(static record => record.Payload.HolderRef).Distinct().Count() == records.Length,
            "The first 25,000 canonical PublicAuthority records must map to distinct Resident holders.");

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(GovernancePublicAuthorityPayloadV1.PartitionId);
        for (var index = 0; index < records.Length; index++)
        {
            var localOrdinal = checked((ulong)index);
            var descriptor = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
                checked(slice.StartOrdinal + localOrdinal));
            var record = records[index];
            var expectedAuthority = Authority(localOrdinal, institutionRefs, scopeRefs);
            var expectedHolder = Qa04GovernancePublicAuthorityResolvedMaterializerV1.ResolveCanonicalHolderRef(localOrdinal);

            Require(record.RecordId == descriptor.Descriptor.RecordId &&
                    record.Revision == 1 && record.CreatedStep == 0 && record.RetiredStep is null &&
                    record.DetailLevel == DetailLevelV1.D2RegionalAggregate,
                "Resolved PublicAuthority descriptor identity/genesis envelope drifted.");
            Require(record.Payload.InstitutionRef == expectedAuthority.InstitutionRef &&
                    record.Payload.HolderRef == expectedHolder &&
                    record.Payload.AuthorityTokens.SequenceEqual(expectedAuthority.AuthorityTokens) &&
                    record.Payload.ScopeRefs.SequenceEqual(expectedAuthority.ScopeRefs) &&
                    record.Payload.EffectiveFrom == expectedAuthority.EffectiveFrom &&
                    record.Payload.EffectiveUntil is null &&
                    record.Payload.Status.Value == "active",
                "Resolved PublicAuthority must preserve supplied authority while using the canonical Resident holder selector.");
        }

        var partition = Qa04GovernancePublicAuthorityResolvedMaterializerV1.MaterializeResolvedPartition(
            localOrdinal => Authority(localOrdinal, institutionRefs, scopeRefs),
            resolver);
        Require(partition.ItemCount == Qa04GovernancePublicAuthorityResolvedMaterializerV1.CanonicalCount,
            "Resolved PublicAuthority partition must retain all 25,000 records.");

        var missingTokenAuthorityRejected = false;
        try
        {
            _ = Qa04GovernancePublicAuthorityResolvedMaterializerV1.CreateResolved(
                0,
                new Qa04GovernancePublicAuthorityResolvedAuthorityV1(
                    institutionRefs[0],
                    Array.Empty<StableToken>(),
                    new[] { scopeRefs[0] },
                    EffectiveFrom: 0),
                resolver,
                out _);
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.governance.public-authority-token-authority-required")
        {
            missingTokenAuthorityRejected = true;
        }
        Require(missingTokenAuthorityRejected,
            "Resolved PublicAuthority materialization must fail closed when authority token input is absent.");

        var unresolvedHolderRejected = false;
        try
        {
            _ = Qa04GovernancePublicAuthorityResolvedMaterializerV1.CreateResolved(
                0,
                Authority(0, institutionRefs, scopeRefs),
                new RejectResidentReferenceResolver(resolver),
                out _);
        }
        catch (InvalidDataException ex) when (
            ex.Message == "domain.payload.reference-validation:governance.public_authority:holder_ref")
        {
            unresolvedHolderRejected = true;
        }
        Require(unresolvedHolderRejected,
            "Resolved PublicAuthority materialization must fail closed when the canonical Resident holder is not resolvable.");
    }

    private static Qa04GovernancePublicAuthorityResolvedAuthorityV1 Authority(
        ulong localOrdinal,
        IReadOnlyList<PartitionRecordRefV1> institutionRefs,
        IReadOnlyList<PartitionRecordRefV1> scopeRefs)
        => new(
            institutionRefs[checked((int)(localOrdinal % (ulong)institutionRefs.Count))],
            new[] { (localOrdinal & 1UL) == 0 ? FixtureAuthorityA : FixtureAuthorityB },
            new[] { scopeRefs[checked((int)(localOrdinal % (ulong)scopeRefs.Count))] },
            localOrdinal % 19UL);

    private static FixtureReferenceResolver BuildFixtureResolver(
        out IReadOnlyList<PartitionRecordRefV1> institutionRefs,
        out IReadOnlyList<PartitionRecordRefV1> scopeRefs)
    {
        var resolver = new FixtureReferenceResolver();

        var politySchema = StandardDomainPartitionRegistry.Get(GovernancePolityPayloadV1.PartitionId).RecordSchema;
        foreach (var polity in Qa04GovernancePolityMaterializerV1.MaterializeCanonical())
            resolver.Add(new PartitionRecordRefV1(GovernancePolityPayloadV1.PartitionId, polity.RecordId), politySchema);

        var institutions = Qa04GovernanceInstitutionResolvedMaterializerV1.MaterializeResolved(
                static _ => new Qa04GovernanceInstitutionResolvedAuthorityV1(
                    FixtureInstitutionKind,
                    Array.Empty<PartitionRecordRefV1>(),
                    FixtureDecisionMethod),
                resolver)
            .ToArray();
        var institutionSchema = StandardDomainPartitionRegistry.Get(GovernanceInstitutionPayloadV1.PartitionId).RecordSchema;
        var institutionArray = institutions
            .Select(static record => new PartitionRecordRefV1(GovernanceInstitutionPayloadV1.PartitionId, record.RecordId))
            .ToArray();
        foreach (var institutionRef in institutionArray)
            resolver.Add(institutionRef, institutionSchema);
        institutionRefs = institutionArray;

        var residentSchema = StandardDomainPartitionRegistry.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).RecordSchema;
        for (ulong localOrdinal = 0; localOrdinal < Qa04GovernancePublicAuthorityResolvedMaterializerV1.CanonicalCount; localOrdinal++)
        {
            var residentRef = Qa04GovernancePublicAuthorityResolvedMaterializerV1.ResolveCanonicalHolderRef(localOrdinal);
            resolver.Add(residentRef, residentSchema);
        }

        var scopePartition = Qa04SpatialTileScopeAuthorityV1.MaterializeCanonical();
        Require(scopePartition.ItemCount == checked((ulong)Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount),
            "Canonical TileScope fixture authority must retain 4,096 records.");
        var scopeSchema = StandardDomainPartitionRegistry.Get(SpatialScopeRegistryPayloadV1.PartitionId).RecordSchema;
        var scopeArray = new PartitionRecordRefV1[Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount];
        for (var tile = 0; tile < scopeArray.Length; tile++)
        {
            var scopeRef = Qa04SpatialTileScopeAuthorityV1.ScopeRef(checked((ushort)tile));
            scopeArray[tile] = scopeRef;
            resolver.Add(scopeRef, scopeSchema);
        }
        scopeRefs = scopeArray;

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
