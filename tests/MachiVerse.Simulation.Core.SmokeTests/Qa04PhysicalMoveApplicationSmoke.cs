using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04PhysicalMoveApplicationSmoke
{
    public static void Run()
    {
        var descriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value == "physical-item-movement-work");
        var binding = Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1);
        var target = Material(descriptor.FamilyOrdinal);
        var otherOrdinal = descriptor.FamilyOrdinal == 0 ? 1UL : 0UL;
        var nonTarget = Material(otherOrdinal);
        var identity = StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId);
        var current = new DomainPartitionStateV1<PhysicalPresencePayloadV1>(
            identity,
            new[] { target.Presence, nonTarget.Presence });
        var references = Resolver.For(target, nonTarget);

        var result = Qa04PhysicalMoveApplicationV1.Apply(binding, current, references);
        var expectedVelocity = new Vec3Int64V1(
            Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(descriptor.OperationId, "vx"),
            Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(descriptor.OperationId, "vy"),
            0);

        Require(result.PresenceState.ItemCount == current.ItemCount,
            "physical move must preserve presence item count");
        Require(result.AppliedRecord.RecordId == target.Presence.RecordId,
            "physical move must preserve target RecordId");
        Require(result.AppliedRecord.RecordSchema == target.Presence.RecordSchema,
            "physical move must preserve target record schema");
        Require(result.AppliedRecord.Revision == 2,
            "physical move must revise the canonical target exactly once");
        Require(result.AppliedRecord.CreatedStep == 0 && result.AppliedRecord.RetiredStep is null,
            "physical move must preserve target lifecycle envelope");
        Require(result.AppliedRecord.DetailLevel == DetailLevelV1.D0Entity && result.AppliedRecord.LineageRef is null,
            "physical move must preserve target detail/lineage envelope");
        Require(result.AppliedRecord.Payload.LinearVelocity == expectedVelocity,
            "physical move must apply the canonical bound velocity");
        Require(result.AppliedRecord.Payload.SubjectRef == target.Presence.Payload.SubjectRef &&
                result.AppliedRecord.Payload.FrameRef == target.Presence.Payload.FrameRef &&
                result.AppliedRecord.Payload.Position == target.Presence.Payload.Position &&
                result.AppliedRecord.Payload.Orientation == target.Presence.Payload.Orientation &&
                result.AppliedRecord.Payload.AngularRateUradPerSecond == target.Presence.Payload.AngularRateUradPerSecond &&
                result.AppliedRecord.Payload.ShapeRef == target.Presence.Payload.ShapeRef &&
                result.AppliedRecord.Payload.ContainmentRef == target.Presence.Payload.ContainmentRef &&
                result.AppliedRecord.Payload.PresenceMode == target.Presence.Payload.PresenceMode,
            "physical move must not mutate non-velocity Presence fields");
        Require(result.PresenceState.TryGet(nonTarget.Presence.RecordId, out var afterNonTarget) &&
                afterNonTarget == nonTarget.Presence,
            "physical move must preserve non-target Presence records exactly");

        var replayFromSamePreState = Qa04PhysicalMoveApplicationV1.Apply(binding, current, references);
        Require(replayFromSamePreState.AppliedRecord == result.AppliedRecord,
            "physical move replay from the same pre-state must be deterministic");

        var otherDescriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value != "physical-item-movement-work");
        var otherBinding = Qa04CanonicalOperationBindingV1.Bind(otherDescriptor, schedulingPolicyGeneration: 1);
        ExpectInvalid(
            () => Qa04PhysicalMoveApplicationV1.Apply(otherBinding, current, references),
            "qa04.physical.move-family");

        var tamperedOperation = binding.Operation.Clone();
        var tamperedPayload = tamperedOperation.OperationPayload.ToByteArray();
        tamperedPayload[^1] ^= 0x01;
        tamperedOperation.OperationPayload = ByteString.CopyFrom(tamperedPayload);
        var tamperedBinding = binding with { Operation = tamperedOperation };
        ExpectInvalid(
            () => Qa04PhysicalMoveApplicationV1.Apply(tamperedBinding, current, references),
            "qa04.physical.move-binding-drift");

        var missingTarget = new DomainPartitionStateV1<PhysicalPresencePayloadV1>(
            identity,
            new[] { nonTarget.Presence });
        ExpectInvalid(
            () => Qa04PhysicalMoveApplicationV1.Apply(binding, missingTarget, references),
            "qa04.physical.move-target-missing");

        var driftedTarget = new DomainRecordEnvelopeV1<PhysicalPresencePayloadV1>(
            target.Presence.RecordId,
            target.Presence.RecordSchema,
            revision: 2,
            target.Presence.CreatedStep,
            target.Presence.RetiredStep,
            target.Presence.DetailLevel,
            target.Presence.LineageRef,
            target.Presence.Payload);
        var driftedState = new DomainPartitionStateV1<PhysicalPresencePayloadV1>(
            identity,
            new[] { driftedTarget, nonTarget.Presence });
        ExpectInvalid(
            () => Qa04PhysicalMoveApplicationV1.Apply(binding, driftedState, references),
            "qa04.physical.move-target-drift");

        ExpectInvalid(
            () => Qa04PhysicalMoveApplicationV1.Apply(binding, result.PresenceState, references),
            "qa04.physical.move-target-drift");
    }

    private static Qa04PhysicalD0RecordMaterialV1 Material(ulong ordinal)
        => Qa04PhysicalD0MaterializerV1.Create(ordinal, PresenceBinding(ordinal), TerrainBinding);

    private static Qa04PhysicalPresenceGenesisBindingV1 PresenceBinding(ulong ordinal)
        => new(
            new PartitionRecordRefV1("resident.identity_lifecycle", Id(checked((byte)(1 + ordinal % 200)))),
            new PartitionRecordRefV1("spatial.world_frame", Id(0xF0)),
            new Vec3Int64V1(1000, 2000, 3000),
            new QuaternionQ30V1(0, 0, 0, 1 << 30),
            new Vec3Int64V1(0, 0, 0),
            new Vec3Int64V1(0, 0, 0),
            null,
            new StableToken("active"));

    private static Qa04PhysicalTerrainRootBindingV1 TerrainBinding(ushort _)
        => new(
            new PartitionRecordRefV1("spatial.terrain_geometry", Id(0xE0)),
            new Vec3Int64V1(-500, -500, -500),
            new Vec3Int64V1(500, 500, 500));

    private static OpaqueId128 Id(byte suffix)
    {
        var bytes = new byte[16];
        bytes[^1] = suffix;
        return OpaqueId128.FromBytes(bytes);
    }

    private static void ExpectInvalid(Action action, string expectedMessage)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected physical move application to reject invalid input.");
        }
        catch (InvalidDataException ex)
        {
            if (ex.Message != expectedMessage)
                throw new InvalidOperationException($"Unexpected rejection code: {ex.Message}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Resolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public static Resolver For(params Qa04PhysicalD0RecordMaterialV1[] materials)
        {
            var resolver = new Resolver();
            foreach (var material in materials)
            {
                resolver.Set(
                    material.Presence.Payload.SubjectRef,
                    StandardDomainPartitionRegistry.Get(material.Presence.Payload.SubjectRef.PartitionId.Value).RecordSchema);
                resolver.Set(
                    material.Presence.Payload.FrameRef,
                    StandardDomainPartitionRegistry.Get(material.Presence.Payload.FrameRef.PartitionId.Value).RecordSchema);
                resolver.Set(material.Presence.Payload.ShapeRef, material.CollisionShape.RecordSchema);
                if (material.Presence.Payload.ContainmentRef is { } containment)
                {
                    resolver.Set(
                        containment,
                        StandardDomainPartitionRegistry.Get(containment.PartitionId.Value).RecordSchema);
                }
            }
            return resolver;
        }

        public void Set(PartitionRecordRefV1 reference, SchemaRefV1 schema)
            => _records[reference] = schema;

        public bool Exists(PartitionRecordRefV1 reference)
            => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
