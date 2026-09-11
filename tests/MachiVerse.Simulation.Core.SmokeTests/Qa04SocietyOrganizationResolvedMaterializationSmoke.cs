using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04SocietyOrganizationResolvedMaterializationSmoke
{
    private static readonly StableToken FixtureClassA = new("fixture.organization-class-a");
    private static readonly StableToken FixtureClassB = new("fixture.organization-class-b");

    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyOrganizationResolvedMaterializerV1.ValidateCanonicalContract();

        Require(Qa04SocietyOrganizationDependencyContractV1.Blockers.Count == 1 &&
                Qa04SocietyOrganizationDependencyContractV1.Blockers[0].FailureCode.Value ==
                    "qa04.material.organization-class-vocabulary-undefined",
            "Resolved Organization materialization mechanics must not release the canonical vocabulary blocker.");

        var records = Qa04SocietyOrganizationResolvedMaterializerV1.MaterializeResolved(
                static localOrdinal => (localOrdinal & 1UL) == 0 ? FixtureClassA : FixtureClassB)
            .ToArray();

        Require(records.LongLength == checked((long)Qa04SocietyOrganizationResolvedMaterializerV1.CanonicalCount),
            "Resolved Organization materialization must retain the canonical 10,000-record cardinality.");
        Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
            "Resolved Organization materialization must retain unique canonical descriptor RecordIds.");

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyOrganizationPayloadV1.PartitionId);
        for (var index = 0; index < records.Length; index++)
        {
            var localOrdinal = checked((ulong)index);
            var descriptor = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
                checked(slice.StartOrdinal + localOrdinal));
            var record = records[index];
            var expectedClass = (localOrdinal & 1UL) == 0 ? FixtureClassA : FixtureClassB;

            Require(record.RecordId == descriptor.Descriptor.RecordId &&
                    record.Payload.OrganizationId == record.RecordId &&
                    record.Revision == 1 && record.CreatedStep == 0 && record.RetiredStep is null &&
                    record.DetailLevel == DetailLevelV1.D2RegionalAggregate,
                "Resolved Organization descriptor identity/genesis envelope drifted.");
            Require(record.Payload.OrganizationClass == expectedClass &&
                    record.Payload.Lifecycle.Value == "active" &&
                    record.Payload.FoundedStep == 0,
                "Resolved Organization must preserve the explicitly supplied class and decided genesis semantics.");
            Require(record.Payload.PurposeTokens.Count == 0 &&
                    record.Payload.ParentRefs.Count == 0 &&
                    record.Payload.FacilityRefs.Count == 0,
                "Resolved Organization empty-permitted genesis lists must remain empty.");
        }

        var partition = Qa04SocietyOrganizationResolvedMaterializerV1.MaterializeResolvedPartition(
            static localOrdinal => (localOrdinal & 1UL) == 0 ? FixtureClassA : FixtureClassB);
        Require(partition.ItemCount == Qa04SocietyOrganizationResolvedMaterializerV1.CanonicalCount,
            "Resolved Organization partition must retain all 10,000 records.");

        var missingAuthorityRejected = false;
        try
        {
            _ = Qa04SocietyOrganizationResolvedMaterializerV1.CreateResolved(0, default, out _);
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.society.organization-class-authority-required")
        {
            missingAuthorityRejected = true;
        }

        Require(missingAuthorityRejected,
            "Resolved Organization materialization must fail closed when organization_class authority is absent.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
