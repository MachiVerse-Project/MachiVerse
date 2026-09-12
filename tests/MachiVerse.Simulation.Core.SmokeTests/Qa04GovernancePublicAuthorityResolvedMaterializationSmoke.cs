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
    private static readonly StableToken FixtureAuthority = new("fixture.authority");

    [ModuleInitializer]
    internal static void Run()
    {
        Qa04GovernancePublicAuthorityResolvedMaterializerV1.ValidateCanonicalContract();
        Require(Qa04GovernancePublicAuthorityDependencyContractV1.Blockers.Count == 0,
            "PublicAuthority canonical authority blockers must be closed.");

        var resolver = BuildResolver(out var institutionRef);
        var records = Qa04GovernancePublicAuthorityResolvedMaterializerV1.MaterializeResolved(
            _ => new Qa04GovernancePublicAuthorityResolvedAuthorityV1(institutionRef, new[] { FixtureAuthority }, 0), resolver).ToArray();
        Require(records.LongLength == 25_000 && records.Select(static r => r.RecordId).Distinct().Count() == records.Length,
            "PublicAuthority canonical mechanics/cardinality drifted.");
        Require(records.All(static r => r.Payload.EffectiveFrom == 0 && r.Payload.EffectiveUntil is null && r.Payload.Status.Value == "active" && r.Payload.ScopeRefs.Count == 1),
            "PublicAuthority decided genesis mechanics drifted.");

        var missingTokenRejected = false;
        try { _ = Qa04GovernancePublicAuthorityResolvedMaterializerV1.CreateResolved(0,
            new Qa04GovernancePublicAuthorityResolvedAuthorityV1(institutionRef, Array.Empty<StableToken>(), 0), resolver, out _); }
        catch (InvalidDataException ex) when (ex.Message == "qa04.governance.public-authority-token-authority-required") { missingTokenRejected = true; }
        Require(missingTokenRejected, "PublicAuthority must fail closed without authority token input.");

        var unresolvedHolderRejected = false;
        try { _ = Qa04GovernancePublicAuthorityResolvedMaterializerV1.CreateResolved(0,
            new Qa04GovernancePublicAuthorityResolvedAuthorityV1(institutionRef, new[] { FixtureAuthority }, 0),
            new RejectPartitionReferenceResolver(resolver, ResidentIdentityLifecyclePayloadV1.PartitionId), out _); }
        catch (InvalidDataException ex) when (ex.Message == "domain.payload.reference-validation:governance.public_authority:holder_ref") { unresolvedHolderRejected = true; }
        Require(unresolvedHolderRejected, "PublicAuthority must fail closed for unresolved Resident holder Ref.");
    }

    private static FixtureReferenceResolver BuildResolver(out PartitionRecordRefV1 institutionRef)
    {
        var resolver = new FixtureReferenceResolver();
        foreach (var polity in Qa04GovernancePolityMaterializerV1.MaterializeCanonical())
            resolver.Add(new PartitionRecordRefV1(GovernancePolityPayloadV1.PartitionId, polity.RecordId), polity.RecordSchema);
        var institution = Qa04GovernanceInstitutionResolvedMaterializerV1.CreateResolved(0,
            new Qa04GovernanceInstitutionResolvedAuthorityV1(FixtureInstitutionKind, Array.Empty<PartitionRecordRefV1>(), FixtureDecisionMethod), resolver, out _);
        institutionRef = new PartitionRecordRefV1(GovernanceInstitutionPayloadV1.PartitionId, institution.RecordId);
        resolver.Add(institutionRef, institution.RecordSchema);
        var residentSchema = StandardDomainPartitionRegistry.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).RecordSchema;
        for (ulong i = 0; i < Qa04GovernancePublicAuthorityResolvedMaterializerV1.CanonicalCount; i++)
            resolver.Add(Qa04GovernancePublicAuthorityResolvedMaterializerV1.ResolveCanonicalHolderRef(i), residentSchema);
        var scopeSchema = StandardDomainPartitionRegistry.Get(SpatialScopeRegistryPayloadV1.PartitionId).RecordSchema;
        for (var tile = 0; tile < Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount; tile++)
            resolver.Add(Qa04SpatialTileScopeAuthorityV1.ScopeRef(checked((ushort)tile)), scopeSchema);
        return resolver;
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
        public RejectPartitionReferenceResolver(IDomainRecordSchemaResolverV1 inner, string partitionId) { _inner = inner; _partitionId = partitionId; }
        public bool Exists(PartitionRecordRefV1 reference) => reference.PartitionId.Value != _partitionId && _inner.Exists(reference);
        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            if (reference.PartitionId.Value == _partitionId) { schema = default; return false; }
            return _inner.TryGetRecordSchema(reference, out schema);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
