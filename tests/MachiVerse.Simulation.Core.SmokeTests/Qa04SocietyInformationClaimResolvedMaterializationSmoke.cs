using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyInformationClaimResolvedMaterializationSmoke
{
    private static readonly StableToken FixtureClaim = new("fixture.claim");

    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyInformationClaimResolvedMaterializerV1.ValidateCanonicalContract();
        Require(Qa04SocietyInformationClaimDependencyContractV1.Blockers.Count == 0,
            "InformationClaim canonical authority blockers must be closed.");

        var resolver = new CanonicalPolityReferenceResolver();
        var refs = resolver.References;
        var records = Qa04SocietyInformationClaimResolvedMaterializerV1.MaterializeResolved(
            localOrdinal => new Qa04SocietyInformationClaimResolvedAuthorityV1(
                refs[checked((int)(localOrdinal % (ulong)refs.Count))], FixtureClaim, 0), resolver).ToArray();
        Require(records.LongLength == 25_000 && records.Select(static r => r.RecordId).Distinct().Count() == records.Length,
            "InformationClaim canonical mechanics/cardinality drifted.");
        Require(records.All(static r => r.Payload.CreatedStep == 0 && r.Payload.Status.Value == "active" &&
            r.Payload.SubjectRefs.Count == 0 && r.Payload.ProvenanceRefs.Count == 0),
            "InformationClaim decided genesis mechanics drifted.");

        var missingStepRejected = false;
        try { _ = Qa04SocietyInformationClaimResolvedMaterializerV1.CreateResolved(0,
            new Qa04SocietyInformationClaimResolvedAuthorityV1(refs[0], FixtureClaim, null), resolver, out _); }
        catch (InvalidDataException ex) when (ex.Message == "qa04.society.info-claim-created-step-authority-required") { missingStepRejected = true; }
        Require(missingStepRejected, "InformationClaim must fail closed without payload created_step authority.");

        var unresolvedRejected = false;
        try { _ = Qa04SocietyInformationClaimResolvedMaterializerV1.CreateResolved(0,
            new Qa04SocietyInformationClaimResolvedAuthorityV1(refs[0], FixtureClaim, 0), new RejectAllReferenceResolver(), out _); }
        catch (InvalidDataException ex) when (ex.Message == "domain.payload.reference-validation:society.information_claim:claimant_ref") { unresolvedRejected = true; }
        Require(unresolvedRejected, "InformationClaim must fail closed for unresolved claimant Ref.");
    }

    private sealed class CanonicalPolityReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records;
        public CanonicalPolityReferenceResolver()
        {
            var schema = StandardDomainPartitionRegistry.Get(GovernancePolityPayloadV1.PartitionId).RecordSchema;
            _records = Qa04GovernancePolityMaterializerV1.MaterializeCanonical().ToDictionary(
                static record => new PartitionRecordRefV1(GovernancePolityPayloadV1.PartitionId, record.RecordId), _ => schema);
            References = _records.Keys.OrderBy(static reference => reference.RecordId).ToArray();
        }
        public IReadOnlyList<PartitionRecordRefV1> References { get; }
        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);
        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema) => _records.TryGetValue(reference, out schema);
    }

    private sealed class RejectAllReferenceResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => false;
        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema) { schema = default; return false; }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
