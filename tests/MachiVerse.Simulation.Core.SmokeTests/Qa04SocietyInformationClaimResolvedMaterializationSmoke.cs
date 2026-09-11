using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyInformationClaimResolvedMaterializationSmoke
{
    private static readonly StableToken FixtureClaimA = new("fixture.claim-a");
    private static readonly StableToken FixtureClaimB = new("fixture.claim-b");

    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyInformationClaimResolvedMaterializerV1.ValidateCanonicalContract();

        var blockerCodes = Qa04SocietyInformationClaimDependencyContractV1.Blockers
            .Select(static blocker => blocker.FailureCode.Value)
            .ToHashSet(StringComparer.Ordinal);
        Require(blockerCodes.SetEquals(new[]
            {
                "qa04.material.info-claim-claimant-undefined",
                "qa04.material.info-claim-token-undefined",
                "qa04.material.info-claim-created-step-undefined",
            }),
            "Resolved InformationClaim mechanics must not release the three canonical authority blockers.");

        var resolver = new CanonicalPolityReferenceResolver();
        var polityRefs = resolver.References;
        var records = Qa04SocietyInformationClaimResolvedMaterializerV1.MaterializeResolved(
                localOrdinal => Authority(localOrdinal, polityRefs),
                resolver)
            .ToArray();

        Require(records.LongLength == checked((long)Qa04SocietyInformationClaimResolvedMaterializerV1.CanonicalCount),
            "Resolved InformationClaim materialization must retain the canonical 25,000-record cardinality.");
        Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
            "Resolved InformationClaim materialization must retain unique canonical descriptor RecordIds.");

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get("society.information_claim");
        for (var index = 0; index < records.Length; index++)
        {
            var localOrdinal = checked((ulong)index);
            var descriptor = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
                checked(slice.StartOrdinal + localOrdinal));
            var record = records[index];
            var expectedAuthority = Authority(localOrdinal, polityRefs);

            Require(record.RecordId == descriptor.Descriptor.RecordId &&
                    record.Revision == 1 && record.CreatedStep == 0 && record.RetiredStep is null &&
                    record.DetailLevel == DetailLevelV1.D2RegionalAggregate,
                "Resolved InformationClaim descriptor identity/genesis envelope drifted.");
            Require(record.Payload.ClaimantRef == expectedAuthority.ClaimantRef &&
                    record.Payload.ClaimToken == expectedAuthority.ClaimToken &&
                    record.Payload.CreatedStep == expectedAuthority.CreatedStep &&
                    record.Payload.Status.Value == "active",
                "Resolved InformationClaim must preserve supplied claimant/token/payload-Step authorities.");
            Require(record.Payload.SubjectRefs.Count == 0 && record.Payload.ProvenanceRefs.Count == 0,
                "Resolved InformationClaim must not synthesize subject/provenance refs without a semantic rule.");
            Require(record.Payload.ContentDigest.SequenceEqual(
                    Qa04SocietyInformationClaimDependencyContractV1.ContentDigest(record.RecordId)),
                "Resolved InformationClaim content digest must remain canonical and descriptor-derived.");
        }

        var partition = Qa04SocietyInformationClaimResolvedMaterializerV1.MaterializeResolvedPartition(
            localOrdinal => Authority(localOrdinal, polityRefs),
            resolver);
        Require(partition.ItemCount == Qa04SocietyInformationClaimResolvedMaterializerV1.CanonicalCount,
            "Resolved InformationClaim partition must retain all 25,000 records.");

        var missingCreatedStepRejected = false;
        try
        {
            _ = Qa04SocietyInformationClaimResolvedMaterializerV1.CreateResolved(
                0,
                new Qa04SocietyInformationClaimResolvedAuthorityV1(polityRefs[0], FixtureClaimA, null),
                resolver,
                out _);
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.society.info-claim-created-step-authority-required")
        {
            missingCreatedStepRejected = true;
        }
        Require(missingCreatedStepRejected,
            "Resolved InformationClaim materialization must fail closed when payload created_step authority is absent.");

        var unresolvedClaimantRejected = false;
        try
        {
            _ = Qa04SocietyInformationClaimResolvedMaterializerV1.CreateResolved(
                0,
                Authority(0, polityRefs),
                new RejectAllReferenceResolver(),
                out _);
        }
        catch (InvalidDataException ex) when (
            ex.Message == "domain.payload.reference-validation:society.information_claim:claimant_ref")
        {
            unresolvedClaimantRejected = true;
        }
        Require(unresolvedClaimantRejected,
            "Resolved InformationClaim materialization must fail closed when claimant authority is not resolvable.");
    }

    private static Qa04SocietyInformationClaimResolvedAuthorityV1 Authority(
        ulong localOrdinal,
        IReadOnlyList<PartitionRecordRefV1> polityRefs)
        => new(
            polityRefs[checked((int)(localOrdinal % (ulong)polityRefs.Count))],
            (localOrdinal & 1UL) == 0 ? FixtureClaimA : FixtureClaimB,
            localOrdinal % 17UL);

    private sealed class CanonicalPolityReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records;

        public CanonicalPolityReferenceResolver()
        {
            var schema = StandardDomainPartitionRegistry.Get(GovernancePolityPayloadV1.PartitionId).RecordSchema;
            _records = Qa04GovernancePolityMaterializerV1.MaterializeCanonical()
                .ToDictionary(
                    static record => new PartitionRecordRefV1(GovernancePolityPayloadV1.PartitionId, record.RecordId),
                    _ => schema);
            References = _records.Keys.OrderBy(static reference => reference.RecordId).ToArray();
        }

        public IReadOnlyList<PartitionRecordRefV1> References { get; }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }

    private sealed class RejectAllReferenceResolver : IDomainRecordSchemaResolverV1
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
