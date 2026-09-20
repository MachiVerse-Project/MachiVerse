using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04GovernanceIncidentApplicationResultV1(
    DomainRecordEnvelopeV1<GovernanceSecurityIncidentPayloadV1> CreatedIncident,
    DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1> IncidentState);

public static class Qa04GovernanceIncidentApplicationV1
{
    private const string Family = "governance-security";
    private const string OperationKind = "governance.incident.register";

    private static readonly StableToken OwnerDomain = new("governance_security");
    private static readonly StableToken ResidentClass = new("resident.persistent-identity");
    private static readonly StableToken IncidentKind = new("perf.incident");
    private static readonly StableToken Status = new("active");
    private static readonly StableToken CreationKind = new("perf.governance-incident-operation");

    public static Qa04GovernanceIncidentApplicationResultV1 Apply(
        Qa04CanonicalOperationBindingResultV1 binding,
        DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1> current,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(references);

        RequireCanonicalBinding(binding);
        var expectedIdentity = StandardDomainPartitionRegistry.Get(GovernanceSecurityIncidentPayloadV1.PartitionId);
        if (current.Identity != expectedIdentity)
            throw new InvalidDataException("qa04.governance.incident-partition-identity");

        var descriptor = binding.SourceDescriptor;
        var effectiveStep = checked(descriptor.InjectionStep + 1UL);
        var resident = Qa04ReferenceLoadV1.Record(ResidentClass, descriptor.FamilyOrdinal);
        var residentRef = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            resident.RecordId);
        if (binding.PrimaryTarget != residentRef || binding.ScheduledOperation.EffectiveStep != effectiveStep)
            throw new InvalidDataException("qa04.governance.incident-target-step-drift");

        var scopeRef = Qa04SpatialTileScopeAuthorityV1.ScopeRef(
            Qa04ReferenceLoadV1.RegionalTileIndex(resident.RecordId));
        var claimSlice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyInformationClaimPayloadV1.PartitionId);
        var claimLocalOrdinal = descriptor.FamilyOrdinal % claimSlice.Count;
        var claimBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(claimSlice.StartOrdinal + claimLocalOrdinal));
        if (claimBinding.PartitionId.Value != SocietyInformationClaimPayloadV1.PartitionId ||
            claimBinding.PartitionLocalOrdinal != claimLocalOrdinal ||
            claimBinding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.governance.incident-claim-ref-drift");
        var claimRef = new PartitionRecordRefV1(claimBinding.PartitionId, claimBinding.Descriptor.RecordId);

        RequireSchema(references, residentRef, "qa04.governance.incident-subject-ref");
        RequireSchema(references, scopeRef, "qa04.governance.incident-scope-ref");
        RequireSchema(references, claimRef, "qa04.governance.incident-claim-ref");

        var recordId = DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            OwnerDomain,
            descriptor.OperationId,
            CreationKind,
            localOrdinal: 0);
        if (recordId.IsZero)
            throw new InvalidDataException("qa04.governance.incident-record-id-zero");
        if (current.TryGet(recordId, out _))
            throw new InvalidDataException("qa04.governance.incident-record-id-collision");

        var severity = checked((uint)(500_000UL + descriptor.FamilyOrdinal % 500_001UL));
        var payload = new GovernanceSecurityIncidentPayloadV1(
            IncidentKind,
            Array.AsReadOnly(new[] { residentRef }),
            scopeRef,
            effectiveStep,
            Array.AsReadOnly(new[] { claimRef }),
            Status,
            severity);
        var created = new DomainRecordEnvelopeV1<GovernanceSecurityIncidentPayloadV1>(
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
            created.Payload.IncidentKind != IncidentKind ||
            created.Payload.SubjectRefs.Count != 1 || created.Payload.SubjectRefs[0] != residentRef ||
            created.Payload.ScopeRef != scopeRef || created.Payload.OccurredStep != effectiveStep ||
            created.Payload.FactEventRefs.Count != 1 || created.Payload.FactEventRefs[0] != claimRef ||
            created.Payload.Status != Status || created.Payload.SeverityPpm != severity)
            throw new InvalidDataException("qa04.governance.incident-created-record-drift");

        var next = new DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1>(
            expectedIdentity,
            current.RecordsCanonical.Concat(new[] { created }));
        if (next.ItemCount != checked(current.ItemCount + 1UL))
            throw new InvalidDataException("qa04.governance.incident-create-count-drift");
        foreach (var existing in current.RecordsCanonical)
        {
            if (!next.TryGet(existing.RecordId, out var after) || !ReferenceEquals(existing, after))
                throw new InvalidDataException("qa04.governance.incident-existing-record-drift");
        }

        return new Qa04GovernanceIncidentApplicationResultV1(created, next);
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
            throw new InvalidDataException("qa04.governance.incident-binding-null");
        if (binding.SourceDescriptor.FamilyToken.Value != Family)
            throw new InvalidDataException("qa04.governance.incident-family");
        if (binding.Operation.OperationKind != OperationKind)
            throw new InvalidDataException("qa04.governance.incident-operation-kind");
        if (binding.OwnerDomain != OwnerDomain)
            throw new InvalidDataException("qa04.governance.incident-owner-domain");
        if (binding.Operation.Admission is null || binding.Operation.Admission.SchedulingPolicyGeneration == 0)
            throw new InvalidDataException("qa04.governance.incident-admission");

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
            throw new InvalidDataException("qa04.governance.incident-binding-drift");
    }
}
