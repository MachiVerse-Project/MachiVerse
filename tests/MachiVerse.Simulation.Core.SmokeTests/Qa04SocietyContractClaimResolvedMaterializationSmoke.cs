using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyContractClaimResolvedMaterializationSmoke
{
    private static readonly StableToken FixtureKindA = new("fixture.contract-kind-a");
    private static readonly StableToken FixtureKindB = new("fixture.contract-kind-b");

    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyContractClaimResolvedMaterializerV1.ValidateCanonicalContract();
        Require(Qa04SocietyContractClaimDependencyContractV1.Blockers.Count == 0,
            "Resolved ContractClaim dependency contract must reflect the decided canonical authorities.");

        var resolver = new CanonicalPolityFixtureReferenceResolver();
        var records = Qa04SocietyContractClaimResolvedMaterializerV1.MaterializeResolved(
                localOrdinal => Authority(localOrdinal, resolver), resolver).ToArray();

        Require(records.LongLength == checked((long)Qa04SocietyContractClaimResolvedMaterializerV1.CanonicalCount),
            "Resolved ContractClaim materialization must retain the canonical 60,000-record cardinality.");
        Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
            "Resolved ContractClaim materialization must retain unique canonical descriptor RecordIds.");

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyContractClaimPayloadV1.PartitionId);
        for (var index = 0; index < records.Length; index++)
        {
            var localOrdinal = checked((ulong)index);
            var descriptor = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
            var record = records[index];
            var expectedAuthority = Authority(localOrdinal, resolver);
            Require(record.RecordId == descriptor.Descriptor.RecordId && record.Revision == 1 && record.CreatedStep == 0 &&
                    record.RetiredStep is null && record.DetailLevel == DetailLevelV1.D2RegionalAggregate,
                "Resolved ContractClaim descriptor identity/genesis envelope drifted.");
            Require(record.Payload.ContractKind == expectedAuthority.ContractKind &&
                    record.Payload.PartyRefs.SequenceEqual(expectedAuthority.PartyRefs) && record.Payload.Status.Value == "active",
                "Resolved ContractClaim must preserve the supplied authorities and canonical status.");
            Require(record.Payload.ClaimantRef is null && record.Payload.ObligorRef is null &&
                    record.Payload.Amount is null && record.Payload.Quantity is null && record.Payload.DueStep is null,
                "Resolved ContractClaim optional genesis fields must remain absent.");
            Require(record.Payload.TermsDigest.SequenceEqual(Qa04SocietyContractClaimDependencyContractV1.TermsDigest(record.RecordId)),
                "Resolved ContractClaim terms_digest must come from the canonical genesis source.");
        }

        var partyDistribution = records.GroupBy(static record => record.Payload.PartyRefs.Single()).Select(static group => group.Count()).ToArray();
        Require(partyDistribution.Length == 1_000 && partyDistribution.All(static count => count == 60),
            "Fixture-only party mapping must exercise all actual target records evenly.");

        var missingKindRejected = false;
        try
        {
            _ = Qa04SocietyContractClaimResolvedMaterializerV1.CreateResolved(0,
                new Qa04SocietyContractClaimResolvedAuthorityV1(default, new[] { resolver.RefFor(0) }), resolver, out _);
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.society.contract-kind-authority-required") { missingKindRejected = true; }
        Require(missingKindRejected, "Resolved ContractClaim materialization must fail closed when contract_kind authority is absent.");

        var missingPartyRejected = false;
        try
        {
            _ = Qa04SocietyContractClaimResolvedMaterializerV1.CreateResolved(0,
                new Qa04SocietyContractClaimResolvedAuthorityV1(FixtureKindA, Array.Empty<PartitionRecordRefV1>()), resolver, out _);
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.society.contract-party-authority-required") { missingPartyRejected = true; }
        Require(missingPartyRejected, "Resolved ContractClaim materialization must fail closed when party authority is absent.");

        var unresolvedPartyRejected = false;
        try
        {
            _ = Qa04SocietyContractClaimResolvedMaterializerV1.CreateResolved(0,
                new Qa04SocietyContractClaimResolvedAuthorityV1(FixtureKindA, new[] { resolver.RefFor(0) }),
                new RejectAllReferenceResolver(), out _);
        }
        catch (InvalidDataException ex) when (ex.Message == "domain.payload.reference-validation:society.contract_claim:party_refs") { unresolvedPartyRejected = true; }
        Require(unresolvedPartyRejected, "Resolved ContractClaim materialization must fail closed when actual party authority is not resolvable.");
    }

    private static Qa04SocietyContractClaimResolvedAuthorityV1 Authority(ulong localOrdinal, CanonicalPolityFixtureReferenceResolver resolver)
        => new((localOrdinal & 1UL) == 0 ? FixtureKindA : FixtureKindB, new[] { resolver.RefFor(localOrdinal % 1_000UL) });

    private sealed class CanonicalPolityFixtureReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly PartitionRecordRefV1[] _refs;
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records;
        public CanonicalPolityFixtureReferenceResolver()
        {
            var schema = StandardDomainPartitionRegistry.Get(GovernancePolityPayloadV1.PartitionId).RecordSchema;
            _refs = Qa04GovernancePolityMaterializerV1.MaterializeCanonical()
                .Select(static record => new PartitionRecordRefV1(GovernancePolityPayloadV1.PartitionId, record.RecordId)).ToArray();
            _records = _refs.ToDictionary(static reference => reference, _ => schema);
        }
        public PartitionRecordRefV1 RefFor(ulong localOrdinal) => _refs[checked((int)localOrdinal)];
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
