using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04GovernanceInstitutionResolvedMaterializationSmoke
{
    private static readonly StableToken FixtureKindA = new("fixture.institution-kind-a");
    private static readonly StableToken FixtureKindB = new("fixture.institution-kind-b");
    private static readonly StableToken FixtureDecisionA = new("fixture.decision-method-a");
    private static readonly StableToken FixtureDecisionB = new("fixture.decision-method-b");

    [ModuleInitializer]
    internal static void Run()
    {
        Qa04GovernanceInstitutionResolvedMaterializerV1.ValidateCanonicalContract();

        var blockerCodes = Qa04GovernanceInstitutionDependencyContractV1.Blockers
            .Select(static blocker => blocker.FailureCode.Value)
            .ToHashSet(StringComparer.Ordinal);
        Require(blockerCodes.SetEquals(new[]
            {
                "qa04.material.institution-kind-vocabulary-undefined",
                "qa04.material.decision-method-vocabulary-undefined",
                "qa04.material.institution-office-mapping-undefined",
            }),
            "Resolved Institution mechanics must not release the three canonical authority blockers.");

        var resolver = new CanonicalPolityReferenceResolver();
        var records = Qa04GovernanceInstitutionResolvedMaterializerV1.MaterializeResolved(
                static localOrdinal => Authority(localOrdinal),
                resolver)
            .ToArray();

        Require(records.LongLength == checked((long)Qa04GovernanceInstitutionResolvedMaterializerV1.CanonicalCount),
            "Resolved Institution materialization must retain the canonical 5,000-record cardinality.");
        Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
            "Resolved Institution materialization must retain unique canonical descriptor RecordIds.");

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(GovernanceInstitutionPayloadV1.PartitionId);
        for (var index = 0; index < records.Length; index++)
        {
            var localOrdinal = checked((ulong)index);
            var descriptor = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
                checked(slice.StartOrdinal + localOrdinal));
            var record = records[index];
            var expectedAuthority = Authority(localOrdinal);
            var expectedPolity = Qa04GovernanceInstitutionResolvedMaterializerV1.ResolveCanonicalPolityRef(localOrdinal);

            Require(record.RecordId == descriptor.Descriptor.RecordId &&
                    record.Revision == 1 && record.CreatedStep == 0 && record.RetiredStep is null &&
                    record.DetailLevel == DetailLevelV1.D2RegionalAggregate,
                "Resolved Institution descriptor identity/genesis envelope drifted.");
            Require(record.Payload.PolityRef == expectedPolity &&
                    record.Payload.InstitutionKind == expectedAuthority.InstitutionKind &&
                    record.Payload.DecisionMethod == expectedAuthority.DecisionMethod &&
                    record.Payload.Lifecycle.Value == "active" &&
                    record.Payload.SelectionRuleRef is null,
                "Resolved Institution must preserve canonical Polity mapping and supplied authorities.");
            Require(record.Payload.OfficeRefs.Count == 0,
                "Fixture-only Institution office authority must remain exactly as supplied.");
        }

        var polityDistribution = records
            .GroupBy(static record => record.Payload.PolityRef)
            .Select(static group => group.Count())
            .ToArray();
        Require(checked((ulong)polityDistribution.Length) == Qa04GovernancePolityMaterializerV1.CanonicalCount &&
                polityDistribution.All(static count => count == 5),
            "Canonical Institution -> Polity modulo mapping must bind exactly five Institutions per Polity.");

        var partition = Qa04GovernanceInstitutionResolvedMaterializerV1.MaterializeResolvedPartition(
            static localOrdinal => Authority(localOrdinal),
            resolver);
        Require(partition.ItemCount == Qa04GovernanceInstitutionResolvedMaterializerV1.CanonicalCount,
            "Resolved Institution partition must retain all 5,000 records.");

        var missingKindRejected = false;
        try
        {
            _ = Qa04GovernanceInstitutionResolvedMaterializerV1.CreateResolved(
                0,
                new Qa04GovernanceInstitutionResolvedAuthorityV1(default, Array.Empty<PartitionRecordRefV1>(), FixtureDecisionA),
                resolver,
                out _);
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.governance.institution-kind-authority-required")
        {
            missingKindRejected = true;
        }
        Require(missingKindRejected,
            "Resolved Institution materialization must fail closed when institution_kind authority is absent.");

        var unresolvedPolityRejected = false;
        try
        {
            _ = Qa04GovernanceInstitutionResolvedMaterializerV1.CreateResolved(
                0,
                Authority(0),
                new RejectAllReferenceResolver(),
                out _);
        }
        catch (InvalidDataException ex) when (
            ex.Message == "domain.payload.reference-validation:governance.institution:polity_ref")
        {
            unresolvedPolityRejected = true;
        }
        Require(unresolvedPolityRejected,
            "Resolved Institution materialization must fail closed when actual Polity authority is not resolvable.");
    }

    private static Qa04GovernanceInstitutionResolvedAuthorityV1 Authority(ulong localOrdinal)
        => (localOrdinal & 1UL) == 0
            ? new Qa04GovernanceInstitutionResolvedAuthorityV1(
                FixtureKindA,
                Array.Empty<PartitionRecordRefV1>(),
                FixtureDecisionA)
            : new Qa04GovernanceInstitutionResolvedAuthorityV1(
                FixtureKindB,
                Array.Empty<PartitionRecordRefV1>(),
                FixtureDecisionB);

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
        }

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
