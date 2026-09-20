using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04EnvironmentHazardApplicationResultV1(
    DomainRecordEnvelopeV1<EnvironmentHazardPayloadV1> CreatedHazard,
    DomainPartitionStateV1<EnvironmentHazardPayloadV1> HazardState);

public static class Qa04EnvironmentHazardApplicationV1
{
    private const string Family = "environment-spatial-admin-synthetic";
    private const string OperationKind = "environment.hazard.inject";
    private const ulong DurationSteps = 30;

    private static readonly StableToken OwnerDomain = new("environment");
    private static readonly StableToken HazardKind = new("perf.synthetic-hazard");
    private static readonly StableToken CreationKind = new("perf.environment-hazard-operation");

    public static Qa04EnvironmentHazardApplicationResultV1 Apply(
        Qa04CanonicalOperationBindingResultV1 binding,
        DomainPartitionStateV1<EnvironmentHazardPayloadV1> current,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(references);

        RequireCanonicalBinding(binding);
        var expectedIdentity = StandardDomainPartitionRegistry.Get(EnvironmentHazardPayloadV1.PartitionId);
        if (current.Identity != expectedIdentity)
            throw new InvalidDataException("qa04.environment.hazard-partition-identity");

        var descriptor = binding.SourceDescriptor;
        var effectiveStep = checked(descriptor.InjectionStep + 1UL);
        var tile = checked((ushort)(descriptor.FamilyOrdinal % Qa04ReferenceLoadV1.RegionalTileCount));
        var scopeRef = Qa04SpatialTileScopeAuthorityV1.ScopeRef(tile);
        if (binding.PrimaryTarget != scopeRef || binding.ScheduledOperation.EffectiveStep != effectiveStep)
            throw new InvalidDataException("qa04.environment.hazard-target-step-drift");

        RequireSchema(references, scopeRef, "qa04.environment.hazard-scope-ref");

        var intensityPpm = checked((uint)(100_000UL + descriptor.FamilyOrdinal % 800_001UL));
        if (intensityPpm < 100_000U || intensityPpm > 900_000U)
            throw new InvalidDataException("qa04.environment.hazard-intensity-range");
        var expectedEndStep = checked(effectiveStep + DurationSteps);

        var recordId = DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            OwnerDomain,
            descriptor.OperationId,
            CreationKind,
            localOrdinal: 0);
        if (recordId.IsZero)
            throw new InvalidDataException("qa04.environment.hazard-record-id-zero");
        if (current.TryGet(recordId, out _))
            throw new InvalidDataException("qa04.environment.hazard-record-id-collision");

        var payload = new EnvironmentHazardPayloadV1(
            scopeRef,
            HazardKind,
            intensityPpm,
            effectiveStep,
            expectedEndStep,
            Array.Empty<PartitionRecordRefV1>(),
            Array.AsReadOnly(new[] { scopeRef }));
        new StandardDomainPayloadCodecValidatorV1().Validate(
            EnvironmentHazardPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);

        var created = new DomainRecordEnvelopeV1<EnvironmentHazardPayloadV1>(
            recordId,
            expectedIdentity.RecordSchema,
            revision: 1,
            createdStep: effectiveStep,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);

        if (created.Revision != 1 || created.CreatedStep != effectiveStep || created.RetiredStep is not null ||
            created.DetailLevel != DetailLevelV1.D0Entity || created.LineageRef is not null ||
            created.RecordSchema != expectedIdentity.RecordSchema ||
            created.Payload.SpatialScope != scopeRef || created.Payload.HazardKind != HazardKind ||
            created.Payload.IntensityPpm != intensityPpm || created.Payload.StartedStep != effectiveStep ||
            created.Payload.ExpectedEndStep != expectedEndStep || created.Payload.DriverRefs.Count != 0 ||
            created.Payload.AffectedScopeRefs.Count != 1 || created.Payload.AffectedScopeRefs[0] != scopeRef)
            throw new InvalidDataException("qa04.environment.hazard-created-record-drift");

        var next = new DomainPartitionStateV1<EnvironmentHazardPayloadV1>(
            expectedIdentity,
            current.RecordsCanonical.Concat(new[] { created }));
        if (next.ItemCount != checked(current.ItemCount + 1UL))
            throw new InvalidDataException("qa04.environment.hazard-create-count-drift");
        foreach (var existing in current.RecordsCanonical)
        {
            if (!next.TryGet(existing.RecordId, out var after) || !ReferenceEquals(existing, after))
                throw new InvalidDataException("qa04.environment.hazard-existing-record-drift");
        }

        return new Qa04EnvironmentHazardApplicationResultV1(created, next);
    }

    private static void RequireSchema(
        IDomainRecordSchemaResolverV1 references,
        PartitionRecordRefV1 reference,
        string code)
    {
        var expected = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value).RecordSchema;
        if (!references.TryGetRecordSchema(reference, out var actual) || actual != expected)
            throw new InvalidDataException(code);
    }

    private static void RequireCanonicalBinding(Qa04CanonicalOperationBindingResultV1 binding)
    {
        if (binding.SourceDescriptor is null || binding.BoundDescriptor is null || binding.Operation is null ||
            binding.OrderKey is null || binding.ScheduledOperation is null)
            throw new InvalidDataException("qa04.environment.hazard-binding-null");
        if (binding.SourceDescriptor.FamilyToken.Value != Family)
            throw new InvalidDataException("qa04.environment.hazard-family");
        if (binding.Operation.OperationKind != OperationKind)
            throw new InvalidDataException("qa04.environment.hazard-operation-kind");
        if (binding.OwnerDomain != OwnerDomain)
            throw new InvalidDataException("qa04.environment.hazard-owner-domain");
        if (binding.Operation.Admission is null || binding.Operation.Admission.SchedulingPolicyGeneration == 0)
            throw new InvalidDataException("qa04.environment.hazard-admission");

        if (Qa04CanonicalOperationBindingV1.HasCanonicalAuthority(binding))
            return;

        var expected = Qa04CanonicalOperationBindingV1.Bind(
            binding.SourceDescriptor,
            binding.Operation.Admission.SchedulingPolicyGeneration);
        if (!expected.Operation.ToByteArray().AsSpan().SequenceEqual(binding.Operation.ToByteArray()) ||
            expected.OwnerDomain != binding.OwnerDomain || expected.PrimaryTarget != binding.PrimaryTarget ||
            expected.ScheduledOperation.OperationId != binding.ScheduledOperation.OperationId ||
            expected.ScheduledOperation.EffectiveStep != binding.ScheduledOperation.EffectiveStep ||
            !expected.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(binding.OrderKey.ToDatabaseBytes()) ||
            !expected.ScheduledOperation.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(binding.ScheduledOperation.OrderKey.ToDatabaseBytes()) ||
            expected.BoundDescriptor.InjectionStep != binding.BoundDescriptor.InjectionStep ||
            expected.BoundDescriptor.FamilyToken != binding.BoundDescriptor.FamilyToken ||
            expected.BoundDescriptor.FamilyOrdinal != binding.BoundDescriptor.FamilyOrdinal ||
            expected.BoundDescriptor.OperationId != binding.BoundDescriptor.OperationId ||
            !expected.BoundDescriptor.PayloadDigest.AsSpan().SequenceEqual(binding.BoundDescriptor.PayloadDigest))
            throw new InvalidDataException("qa04.environment.hazard-binding-drift");
    }
}
