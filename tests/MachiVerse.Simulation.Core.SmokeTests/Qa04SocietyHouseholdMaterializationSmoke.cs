using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04SocietyHouseholdMaterializationSmoke
{
    private static readonly StableToken ResidentClass = new("resident.persistent-identity");

    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyHouseholdMaterializerV1.ValidateCanonicalContract();
        var records = Qa04SocietyHouseholdMaterializerV1.MaterializeCanonical().ToArray();

        Require(records.LongLength == checked((long)Qa04SocietyHouseholdMaterializerV1.CanonicalCount),
            "Canonical Society household materialization must contain exactly 40,000 records.");
        Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
            "Canonical Society household record ids must be unique.");

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyHouseholdPayloadV1.PartitionId);
        for (var index = 0; index < records.Length; index++)
        {
            var localOrdinal = checked((ulong)index);
            var descriptor = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
                checked(slice.StartOrdinal + localOrdinal));
            var resident = Qa04ReferenceLoadV1.Record(ResidentClass, localOrdinal);
            var record = records[index];

            Require(record.RecordId == descriptor.Descriptor.RecordId &&
                    record.Revision == 1 && record.CreatedStep == 0 && record.RetiredStep is null,
                "Society household descriptor identity/lifecycle mapping drifted.");
            Require(record.Payload.Status.Value == "active" &&
                    record.Payload.MemberRefs.Count == 1 &&
                    record.Payload.MemberRefs[0].PartitionId.Value == "resident.identity_lifecycle" &&
                    record.Payload.MemberRefs[0].RecordId == resident.RecordId,
                "Society household member Ref must close to the canonical Resident authority.");
            Require(record.Payload.SharedAccountRefs.Count == 0 &&
                    record.Payload.ResidenceRefs.Count == 0 &&
                    record.Payload.ResourceBudgetRefs.Count == 0,
                "Society household empty-permitted relation lists must remain empty at genesis.");
        }

        var partition = Qa04SocietyHouseholdMaterializerV1.MaterializeCanonicalPartition();
        Require(partition.ItemCount == Qa04SocietyHouseholdMaterializerV1.CanonicalCount,
            "Canonical Society household partition must retain all 40,000 records.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
