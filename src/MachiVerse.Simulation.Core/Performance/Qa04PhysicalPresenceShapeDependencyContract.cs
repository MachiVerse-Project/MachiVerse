using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Regression guard for the resolved physical.presence.shape_ref authority dependency.
/// The world blocker remains removed only while ownership, v2 schema/migration, and the canonical
/// Physical shape/D0 materialization contracts continue to agree.
/// </summary>
public static class Qa04PhysicalPresenceShapeDependencyContractV1
{
    public const string ParentWorldDependencyId = "physical.presence.shape-ref-target";
    public const string ParentWorldFailureCode = "qa04.material.physical-presence-shape-authority-undefined";

    public static void ValidateCanonicalContract()
    {
        Qa04PhysicalShapeMaterializerV1.ValidateCanonicalContract();
        Qa04PhysicalD0MaterializerV1.ValidateCanonicalContract();
        PhysicalOccupancyRecordSchemaV2.ValidateCanonicalContract();

        var migration = StandardDomainRecordSchemaMigrationRegistryV1.Get(PhysicalOccupancyRecordSchemaV2.PartitionId);
        if (migration.SourceRecordSchema.Version != new SchemaVersionV1(1, 0) ||
            migration.TargetRecordSchema != PhysicalOccupancyRecordSchemaV2.RecordSchema)
            throw new InvalidDataException("qa04.physical-shape.migration-drift");

        var closure = Qa04ReferenceWorldRefOwnershipContractV1.RefClosures.SingleOrDefault(
            static value => value.DependencyId.Value == ParentWorldDependencyId)
            ?? throw new InvalidDataException("qa04.physical-shape.ownership-closure-missing");
        if (closure.SourcePartitionId.Value != PhysicalPresencePayloadV1.PartitionId ||
            !string.Equals(closure.SourceFieldName, "shape_ref", StringComparison.Ordinal) ||
            closure.TargetPartitionId.Value != PhysicalOccupancyRecordSchemaV2.PartitionId ||
            closure.TargetRecordKind.Value != PhysicalOccupancyRecordSchemaV2.CollisionShapeKind)
            throw new InvalidDataException("qa04.physical-shape.ownership-closure-drift");

        if (Qa04PhysicalShapeMaterializerV1.CanonicalPhysicalCount != 500_000 ||
            Qa04PhysicalD0MaterializerV1.CanonicalPhysicalCount != 500_000)
            throw new InvalidDataException("qa04.physical-shape.canonical-count-drift");

        if (Qa04ReferenceWorldDependencyContractV1.Blockers.Any(
                static blocker => blocker.DependencyId.Value == ParentWorldDependencyId ||
                                  blocker.FailureCode.Value == ParentWorldFailureCode))
            throw new InvalidDataException("qa04.physical-shape.parent-world-blocker-still-present");
    }
}
