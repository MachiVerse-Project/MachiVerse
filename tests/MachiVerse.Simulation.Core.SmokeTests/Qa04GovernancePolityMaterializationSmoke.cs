using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04GovernancePolityMaterializationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04GovernancePolityMaterializerV1.ValidateCanonicalContract();
        var records = Qa04GovernancePolityMaterializerV1.MaterializeCanonical().ToArray();

        Require(records.LongLength == checked((long)Qa04GovernancePolityMaterializerV1.CanonicalCount),
            "Canonical Governance polity materialization must contain exactly 1,000 records.");
        Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
            "Canonical Governance polity record ids must be unique.");

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(GovernancePolityPayloadV1.PartitionId);
        for (var index = 0; index < records.Length; index++)
        {
            var localOrdinal = checked((ulong)index);
            var descriptor = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
                checked(slice.StartOrdinal + localOrdinal));
            var record = records[index];
            var payload = record.Payload;

            Require(record.RecordId == descriptor.Descriptor.RecordId &&
                    record.Revision == 1 && record.CreatedStep == 0 && record.RetiredStep is null &&
                    record.DetailLevel == DetailLevelV1.D2RegionalAggregate && record.LineageRef is null,
                "Governance polity descriptor identity/lifecycle mapping drifted.");
            Require(payload.Lifecycle.Value == "active",
                "Governance polity genesis lifecycle must remain active.");
            Require(payload.RelatedOrgRefs.Count == 0 &&
                    payload.InstitutionRefs.Count == 0 &&
                    payload.JurisdictionRefs.Count == 0 &&
                    payload.ClaimRefs.Count == 0 &&
                    payload.ControlRefs.Count == 0 &&
                    payload.RecognitionRefs.Count == 0 &&
                    payload.FiscalRefs.Count == 0,
                "Governance polity empty-permitted relation lists must remain empty at genesis.");
        }

        var partition = Qa04GovernancePolityMaterializerV1.MaterializeCanonicalPartition();
        Require(partition.ItemCount == Qa04GovernancePolityMaterializerV1.CanonicalCount,
            "Canonical Governance polity partition must retain all 1,000 records.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
