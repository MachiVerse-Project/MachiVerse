using System.Runtime.CompilerServices;
using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04GovernanceIncidentApplicationSmoke
{
    [ModuleInitializer]
    internal static void Register() => Run();

    public static void Run()
    {
        var descriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value == "governance-security");
        var binding = Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1);
        var effectiveStep = checked(descriptor.InjectionStep + 1UL);

        var resident = Qa04ReferenceLoadV1.Record(
            new StableToken("resident.persistent-identity"),
            descriptor.FamilyOrdinal);
        var residentRef = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            resident.RecordId);
        var scopeRef = Qa04SpatialTileScopeAuthorityV1.ScopeRef(
            Qa04ReferenceLoadV1.RegionalTileIndex(resident.RecordId));
        var claimSlice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(
            SocietyInformationClaimPayloadV1.PartitionId);
        var claimLocalOrdinal = descriptor.FamilyOrdinal % claimSlice.Count;
        var claimBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(claimSlice.StartOrdinal + claimLocalOrdinal));
        var claimRef = new PartitionRecordRefV1(claimBinding.PartitionId, claimBinding.Descriptor.RecordId);

        var identity = StandardDomainPartitionRegistry.Get(GovernanceSecurityIncidentPayloadV1.PartitionId);
        var baselineId = DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            new StableToken("governance_security"),
            descriptor.OperationId,
            new StableToken("perf.governance-incident-operation"),
            localOrdinal: 1);
        var baselinePayload = new GovernanceSecurityIncidentPayloadV1(
            new StableToken("perf.incident"),
            Array.AsReadOnly(new[] { residentRef }),
            scopeRef,
            effectiveStep,
            Array.AsReadOnly(new[] { claimRef }),
            new StableToken("active"),
            500_000);
        var baseline = new DomainRecordEnvelopeV1<GovernanceSecurityIncidentPayloadV1>(
            baselineId,
            identity.RecordSchema,
            revision: 1,
            createdStep: effectiveStep,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            baselinePayload);
        var current = new DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1>(identity, new[] { baseline });
        var references = Resolver.For(residentRef, scopeRef, claimRef);

        var result = Qa04GovernanceIncidentApplicationV1.Apply(binding, current, references);
        var expectedId = DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            new StableToken("governance_security"),
            descriptor.OperationId,
            new StableToken("perf.governance-incident-operation"),
            localOrdinal: 0);
        var expectedSeverity = checked((uint)(500_000UL + descriptor.FamilyOrdinal % 500_001UL));
        var created = result.CreatedIncident;

        Require(result.IncidentState.ItemCount == current.ItemCount + 1UL,
            "governance incident must add exactly one record");
        Require(created.RecordId == expectedId && !created.RecordId.IsZero,
            "governance incident created RecordId drift");
        Require(created.RecordSchema == identity.RecordSchema && created.Revision == 1 && created.CreatedStep == effectiveStep,
            "governance incident envelope schema/revision/created_step drift");
        Require(created.RetiredStep is null && created.DetailLevel == DetailLevelV1.D0Entity && created.LineageRef is null,
            "governance incident envelope lifecycle/detail drift");
        Require(created.Payload.IncidentKind == new StableToken("perf.incident") &&
                created.Payload.SubjectRefs.Count == 1 && created.Payload.SubjectRefs[0] == residentRef &&
                created.Payload.ScopeRef == scopeRef && created.Payload.OccurredStep == effectiveStep &&
                created.Payload.FactEventRefs.Count == 1 && created.Payload.FactEventRefs[0] == claimRef &&
                created.Payload.Status == new StableToken("active") && created.Payload.SeverityPpm == expectedSeverity,
            "governance incident payload drift");
        Require(result.IncidentState.TryGet(baseline.RecordId, out var afterBaseline) && ReferenceEquals(afterBaseline, baseline),
            "governance incident must preserve pre-existing records exactly");
        Require(result.IncidentState.TryGet(expectedId, out var afterCreated) && ReferenceEquals(afterCreated, created),
            "governance incident created record missing from next state");

        var replay = Qa04GovernanceIncidentApplicationV1.Apply(binding, current, references);
        Require(replay.CreatedIncident.RecordId == created.RecordId &&
                replay.CreatedIncident.Payload.CanonicalDigest().AsSpan().SequenceEqual(created.Payload.CanonicalDigest()),
            "governance incident replay from same pre-state must be deterministic");

        ExpectInvalid(
            () => Qa04GovernanceIncidentApplicationV1.Apply(binding, result.IncidentState, references),
            "qa04.governance.incident-record-id-collision");

        var otherDescriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value == "physical-item-movement-work");
        var otherBinding = Qa04CanonicalOperationBindingV1.Bind(otherDescriptor, schedulingPolicyGeneration: 1);
        ExpectInvalid(
            () => Qa04GovernanceIncidentApplicationV1.Apply(otherBinding, current, references),
            "qa04.governance.incident-family");

        var tamperedOperation = binding.Operation.Clone();
        var tamperedPayload = tamperedOperation.OperationPayload.ToByteArray();
        tamperedPayload[^1] ^= 0x01;
        tamperedOperation.OperationPayload = ByteString.CopyFrom(tamperedPayload);
        var tamperedBinding = binding with { Operation = tamperedOperation };
        ExpectInvalid(
            () => Qa04GovernanceIncidentApplicationV1.Apply(tamperedBinding, current, references),
            "qa04.governance.incident-binding-drift");

        var wrongPartition = new DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1>(
            StandardDomainPartitionRegistry.Get(GovernanceInvestigationPayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<GovernanceSecurityIncidentPayloadV1>>());
        ExpectInvalid(
            () => Qa04GovernanceIncidentApplicationV1.Apply(binding, wrongPartition, references),
            "qa04.governance.incident-partition-identity");

        var missingClaim = Resolver.For(residentRef, scopeRef);
        ExpectInvalid(
            () => Qa04GovernanceIncidentApplicationV1.Apply(binding, current, missingClaim),
            "qa04.governance.incident-claim-ref");

        var wrongScopeSchema = Resolver.For(residentRef, scopeRef, claimRef);
        wrongScopeSchema.Set(
            scopeRef,
            StandardDomainPartitionRegistry.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).RecordSchema);
        ExpectInvalid(
            () => Qa04GovernanceIncidentApplicationV1.Apply(binding, current, wrongScopeSchema),
            "qa04.governance.incident-scope-ref");
    }

    private static void ExpectInvalid(Action action, string expectedMessage)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected governance incident application to reject invalid input.");
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
