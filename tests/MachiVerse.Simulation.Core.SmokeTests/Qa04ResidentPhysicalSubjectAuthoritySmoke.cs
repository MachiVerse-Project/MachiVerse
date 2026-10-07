using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04ResidentPhysicalSubjectAuthoritySmoke
{
    internal static void Run()
    {
        var ordinals = new[]
        {
            0UL,
            Qa04ReferenceLoadV1.SteadyOperationsPerStep + Qa04ReferenceLoadV1.BurstOperations,
            Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalPhysicalCount - 1,
        };

        foreach (var ordinal in ordinals)
        {
            var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(ordinal);
            var physicalBinding = Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1
                .CreateCanonicalPresenceBinding(ordinal);

            Require(
                physicalBinding.SubjectRef.PartitionId.Value == ResidentIdentityLifecyclePayloadV1.PartitionId,
                $"QA-04 physical subject partition drifted at ordinal={ordinal}.");
            Require(
                physicalBinding.SubjectRef.RecordId == resident.RecordId,
                $"QA-04 physical subject resident authority drifted at ordinal={ordinal}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
