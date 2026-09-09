using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04ReferenceWorldRefOwnershipContractInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Qa04ReferenceWorldRefOwnershipContractV1.ValidateCanonicalContract();

        Require(Qa04ReferenceWorldRefOwnershipContractV1.RecordSchemaRepairs.Count == 4,
            "QA-04 reference Ref ownership repair count drifted.");
        Require(Qa04ReferenceWorldRefOwnershipContractV1.RefClosures.Count == 5,
            "QA-04 reference Ref closure count drifted.");

        var terrain = Qa04ReferenceWorldRefOwnershipContractV1.RefClosures.Single(
            static closure => closure.DependencyId.Value == "spatial.terrain-geometry.root-brick-target");
        Require(terrain.TargetPartitionId.Value == "spatial.terrain_geometry" &&
                terrain.TargetRecordKind.Value == "terrain_brick",
            "QA-04 terrain brick ownership decision drifted.");

        var shape = Qa04ReferenceWorldRefOwnershipContractV1.RefClosures.Single(
            static closure => closure.DependencyId.Value == "physical.presence.shape-ref-target");
        Require(shape.TargetPartitionId.Value == "physical.occupancy" &&
                shape.TargetRecordKind.Value == "collision_shape",
            "QA-04 collision shape ownership decision drifted.");

        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.Any(
                static code => code.Value == "qa04.material.physical-presence-shape-authority-undefined"),
            "QA-04 runtime material gate must remain fail-closed until the physical v2 schema is implemented.");
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.Any(
                static code => code.Value == "qa04.material.market-ref-authority-undefined"),
            "QA-04 runtime material gate must remain fail-closed until the market v2 schema is implemented.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
