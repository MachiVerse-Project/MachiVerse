using System.Runtime.CompilerServices;
using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04EnvironmentHazardApplicationSmoke
{
    [ModuleInitializer]
    internal static void Register() => Run();

    public static void Run()
    {
        var descriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value == "environment-spatial-admin-synthetic");
        var binding = Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1);
        var effectiveStep = checked(descriptor.InjectionStep + 1UL);
        var tile = checked((ushort)(descriptor.FamilyOrdinal % Qa04ReferenceLoadV1.RegionalTileCount));
        var scopeRef = Qa04SpatialTileScopeAuthorityV1.ScopeRef(tile);

        var hazardSlice = Qa04EnvironmentReferenceDecompositionV1.Get(EnvironmentHazardPayloadV1.PartitionId);
        var baselineBinding = Qa04EnvironmentReferenceDecompositionV1.BindD0(hazardSlice.D0StartOrdinal);
        var baselineScope = Qa04EnvironmentD0PartitionMaterializerV1.ResolveSpatialScope(baselineBinding);
        var references = Resolver.For(scopeRef, baselineScope);
        var payloadSource = new Qa04EnvironmentCanonicalD0PayloadSourceV1();
        var baseline = Qa04EnvironmentD0PartitionMaterializerV1.CreateRecord(
            hazardSlice.D0StartOrdinal,
            payloadSource.Hazard,
            static payload => payload.ToStandardPayload(),
            references);

        var identity = StandardDomainPartitionRegistry.Get(EnvironmentHazardPayloadV1.PartitionId);
        var current = new DomainPartitionStateV1<EnvironmentHazardPayloadV1>(identity, new[] { baseline });
        var result = Qa04EnvironmentHazardApplicationV1.Apply(binding, current, references);

        var expectedId = DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            new StableToken("environment"),
            descriptor.OperationId,
            new StableToken("perf.environment-hazard-operation"),
            localOrdinal: 0);
        var expectedIntensity = checked((uint)(100_000UL + descriptor.FamilyOrdinal % 800_001UL));
        var expectedEndStep = checked(effectiveStep + 30UL);
        var created = result.CreatedHazard;

        Require(result.HazardState.ItemCount == current.ItemCount + 1UL,
            "environment hazard must add exactly one record");
        Require(created.RecordId == expectedId && !created.RecordId.IsZero,
            "environment hazard created RecordId drift");
        Require(created.RecordSchema == identity.RecordSchema && created.Revision == 1 && created.CreatedStep == effectiveStep,
            "environment hazard envelope schema/revision/created_step drift");
        Require(created.RetiredStep is null && created.DetailLevel == DetailLevelV1.D0Entity && created.LineageRef is null,
            "environment hazard envelope lifecycle/detail drift");
        Require(created.Payload.SpatialScope == scopeRef &&
                created.Payload.HazardKind == new StableToken("perf.synthetic-hazard") &&
                created.Payload.IntensityPpm == expectedIntensity &&
                created.Payload.StartedStep == effectiveStep &&
                created.Payload.ExpectedEndStep == expectedEndStep &&
                created.Payload.DriverRefs.Count == 0 &&
                created.Payload.AffectedScopeRefs.Count == 1 &&
                created.Payload.AffectedScopeRefs[0] == scopeRef,
            "environment hazard payload drift");
        Require(result.HazardState.TryGet(baseline.RecordId, out var afterBaseline) && ReferenceEquals(afterBaseline, baseline),
            "environment hazard must preserve pre-existing records exactly");
        Require(result.HazardState.TryGet(expectedId, out var afterCreated) && ReferenceEquals(afterCreated, created),
            "environment hazard created record missing from next state");

        var replay = Qa04EnvironmentHazardApplicationV1.Apply(binding, current, references);
        Require(replay.CreatedHazard.RecordId == created.RecordId &&
                replay.CreatedHazard.Payload.CanonicalDigest().AsSpan().SequenceEqual(created.Payload.CanonicalDigest()),
            "environment hazard replay from same pre-state must be deterministic");

        ExpectInvalid(
            () => Qa04EnvironmentHazardApplicationV1.Apply(binding, result.HazardState, references),
            "qa04.environment.hazard-record-id-collision");

        var otherDescriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value == "physical-item-movement-work");
        var otherBinding = Qa04CanonicalOperationBindingV1.Bind(otherDescriptor, schedulingPolicyGeneration: 1);
        ExpectInvalid(
            () => Qa04EnvironmentHazardApplicationV1.Apply(otherBinding, current, references),
            "qa04.environment.hazard-family");

        var tamperedOperation = binding.Operation.Clone();
        var tamperedPayload = tamperedOperation.OperationPayload.ToByteArray();
        tamperedPayload[^1] ^= 0x01;
        tamperedOperation.OperationPayload = ByteString.CopyFrom(tamperedPayload);
        var tamperedBinding = binding with { Operation = tamperedOperation };
        ExpectInvalid(
            () => Qa04EnvironmentHazardApplicationV1.Apply(tamperedBinding, current, references),
            "qa04.environment.hazard-binding-drift");

        var wrongPartition = new DomainPartitionStateV1<EnvironmentHazardPayloadV1>(
            StandardDomainPartitionRegistry.Get(EnvironmentContaminantPayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<EnvironmentHazardPayloadV1>>());
        ExpectInvalid(
            () => Qa04EnvironmentHazardApplicationV1.Apply(binding, wrongPartition, references),
            "qa04.environment.hazard-partition-identity");

        var missingScope = Resolver.For(baselineScope);
        ExpectInvalid(
            () => Qa04EnvironmentHazardApplicationV1.Apply(binding, current, missingScope),
            "qa04.environment.hazard-scope-ref");

        var wrongScopeSchema = Resolver.For(scopeRef, baselineScope);
        wrongScopeSchema.Set(
            scopeRef,
            StandardDomainPartitionRegistry.Get(EnvironmentHazardPayloadV1.PartitionId).RecordSchema);
        ExpectInvalid(
            () => Qa04EnvironmentHazardApplicationV1.Apply(binding, current, wrongScopeSchema),
            "qa04.environment.hazard-scope-ref");
    }

    private static void ExpectInvalid(Action action, string expectedMessage)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected environment hazard application to reject invalid input.");
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

        public static Resolver For(params PartitionRecordRefV1[] references)
        {
            var resolver = new Resolver();
            foreach (var reference in references)
            {
                resolver.Set(
                    reference,
                    StandardDomainPartitionRegistry.Get(reference.PartitionId.Value).RecordSchema);
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
