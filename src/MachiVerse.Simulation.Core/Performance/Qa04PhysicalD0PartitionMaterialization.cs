using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04PhysicalD0PartitionMaterializationV1
{
    internal Qa04PhysicalD0PartitionMaterializationV1(
        ulong descriptorCount,
        DomainPartitionStateV1<PhysicalPresencePayloadV1> presence,
        PhysicalOccupancyPartitionStateV2 occupancy)
    {
        DescriptorCount = descriptorCount;
        Presence = presence ?? throw new ArgumentNullException(nameof(presence));
        Occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));

        if (Presence.Identity != StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId))
            throw new InvalidDataException("qa04.materialization.physical-presence-partition-identity");
        if (Occupancy.State.Identity != PhysicalOccupancyPartitionIdentityV2.Identity)
            throw new InvalidDataException("qa04.materialization.physical-occupancy-partition-identity");
        if (Presence.ItemCount != DescriptorCount)
            throw new InvalidDataException("qa04.materialization.physical-presence-count");
        if (Occupancy.State.ItemCount != checked(DescriptorCount * 2))
            throw new InvalidDataException("qa04.materialization.physical-occupancy-record-count");

        var occupancyArmCount = Occupancy.RecordSet.RecordsCanonical.Count(
            static record => record.Payload is PhysicalOccupancyStatePayloadV2);
        var shapeArmCount = Occupancy.RecordSet.RecordsCanonical.Count(
            static record => record.Payload is PhysicalCollisionShapePayloadV2);
        if ((ulong)occupancyArmCount != DescriptorCount || (ulong)shapeArmCount != DescriptorCount)
            throw new InvalidDataException("qa04.materialization.physical-occupancy-arm-count");
    }

    public ulong DescriptorCount { get; }
    public DomainPartitionStateV1<PhysicalPresencePayloadV1> Presence { get; }
    public PhysicalOccupancyPartitionStateV2 Occupancy { get; }
    public bool IsCanonicalCount => DescriptorCount == Qa04PhysicalD0MaterializerV1.CanonicalPhysicalCount;
}

public static class Qa04PhysicalD0PartitionMaterializerV1
{
    public static Qa04PhysicalD0PartitionMaterializationV1 MaterializeCanonical(
        Func<ulong, Qa04PhysicalPresenceGenesisBindingV1> presenceBindingForOrdinal,
        Func<ushort, Qa04PhysicalTerrainRootBindingV1> terrainRootForTile)
        => Materialize(
            Qa04PhysicalD0MaterializerV1.CanonicalPhysicalCount,
            presenceBindingForOrdinal,
            terrainRootForTile);

    public static Qa04PhysicalD0PartitionMaterializationV1 Materialize(
        ulong count,
        Func<ulong, Qa04PhysicalPresenceGenesisBindingV1> presenceBindingForOrdinal,
        Func<ushort, Qa04PhysicalTerrainRootBindingV1> terrainRootForTile)
    {
        Qa04PhysicalD0MaterializerV1.ValidateCanonicalContract();
        var materials = Qa04PhysicalD0MaterializerV1.Materialize(
            count,
            presenceBindingForOrdinal,
            terrainRootForTile).ToArray();

        var presenceState = new DomainPartitionStateV1<PhysicalPresencePayloadV1>(
            StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId),
            materials.Select(static material => material.Presence));
        var occupancyState = new PhysicalOccupancyPartitionStateV2(
            materials.SelectMany(static material => new[] { material.Occupancy, material.CollisionShape }));

        ValidateOneToOneClosure(materials, presenceState, occupancyState);
        return new Qa04PhysicalD0PartitionMaterializationV1(count, presenceState, occupancyState);
    }

    private static void ValidateOneToOneClosure(
        IReadOnlyList<Qa04PhysicalD0RecordMaterialV1> materials,
        DomainPartitionStateV1<PhysicalPresencePayloadV1> presenceState,
        PhysicalOccupancyPartitionStateV2 occupancyState)
    {
        if (materials.Count != checked((int)presenceState.ItemCount))
            throw new InvalidDataException("qa04.materialization.physical-material-count");

        var shapeIds = new HashSet<MachiVerse.Simulation.Core.Determinism.OpaqueId128>();
        var occupancyPresenceIds = new HashSet<MachiVerse.Simulation.Core.Determinism.OpaqueId128>();
        foreach (var material in materials)
        {
            material.ValidateRefClosure();
            if (!shapeIds.Add(material.CollisionShape.RecordId))
                throw new InvalidDataException("qa04.materialization.physical-shape-id-duplicate");

            var occupancy = (PhysicalOccupancyStatePayloadV2)material.Occupancy.Payload;
            if (!occupancyPresenceIds.Add(occupancy.PresenceRef.RecordId))
                throw new InvalidDataException("qa04.materialization.physical-presence-ref-duplicate");

            if (!occupancyState.RecordSet.TryGet(material.Presence.Payload.ShapeRef.RecordId, out var shape) ||
                shape?.Payload is not PhysicalCollisionShapePayloadV2)
                throw new InvalidDataException("qa04.materialization.physical-shape-target-kind");
        }

        if (shapeIds.Count != materials.Count || occupancyPresenceIds.Count != materials.Count)
            throw new InvalidDataException("qa04.materialization.physical-one-to-one-closure");
    }
}
