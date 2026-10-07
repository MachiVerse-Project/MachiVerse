using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04InfrastructureDependencyCanonicalAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04InfrastructureDependencyCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04InfrastructureDependencyCanonicalAuthorityV1.MaterializeCanonical();
        var records = materialization.RecordsByOrdinal;

        Require(materialization.MaterializedRecordCount == 20_000 &&
                materialization.Partition.ItemCount == 20_000 &&
                records.Count == 20_000 &&
                materialization.RuntimeDependencies.Count == 20_000,
            "Canonical infrastructure.dependency authority must materialize exactly 20,000 records.");

        Require(records.Take(10_000).All(static record =>
                    record.Payload.ConsumerRef.PartitionId.Value == InfrastructureWaterServicePayloadV1.PartitionId &&
                    record.Payload.ProviderRef.PartitionId.Value == InfrastructurePowerServicePayloadV1.PartitionId) &&
                records.Skip(10_000).All(static record =>
                    record.Payload.ConsumerRef.PartitionId.Value == InfrastructureCommunicationServicePayloadV1.PartitionId &&
                    record.Payload.ProviderRef.PartitionId.Value == InfrastructurePowerServicePayloadV1.PartitionId),
            "Canonical infrastructure.dependency must retain the exact Water->Power and Communication->Power split.");

        Require(records.Select(static record => record.Payload.ConsumerRef).Distinct().Count() == 20_000,
            "Every canonical Water/Communication consumer must have exactly one dependency.");
        var providers = records.GroupBy(static record => record.Payload.ProviderRef).ToArray();
        Require(providers.Length == 10_000 && providers.All(static group => group.Count() == 2),
            "Every canonical Power provider must back exactly two dependencies.");

        Require(records.All(static record =>
                record.Payload.DependencyKind.Value == "perf.power-supply" &&
                record.Payload.MinimumServicePpm == 1_000_000 &&
                record.Payload.DegradationCurveRef is null &&
                record.Payload.FallbackRefs.Count == 0 &&
                record.Payload.Status.Value == "active"),
            "Canonical infrastructure.dependency genesis payload drifted.");

        for (var i = 0; i < records.Count; i++)
        {
            var runtime = materialization.RuntimeDependencies[i];
            var payload = records[i].Payload;
            Require(runtime.UpstreamId == payload.ProviderRef.RecordId &&
                    runtime.DownstreamId == payload.ConsumerRef.RecordId,
                "Runtime dependency orientation must be provider->consumer.");
        }

        var power0 = records[0].Payload.ProviderRef.RecordId;
        var water0 = records[0].Payload.ConsumerRef.RecordId;
        var communication0 = records[10_000].Payload.ConsumerRef.RecordId;
        var propagated = DeterministicOutageCascadeV1.Propagate(new[] { power0 }, materialization.RuntimeDependencies);
        Require(propagated.Count == 3 && propagated.Contains(power0) && propagated.Contains(water0) && propagated.Contains(communication0),
            "Power failure must propagate to the exact Water and Communication consumers through provider->consumer orientation.");

        var recovered = Qa04InfrastructureDependencySnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(recovered == 20_000,
            "Canonical infrastructure.dependency Snapshot/recovery must semantically recover all 20,000 records.");

        var first = records[0];
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                InfrastructureDependencyPayloadV1.PartitionId,
                first.Payload.ToStandardPayload(),
                new EmptyReferenceResolver()),
            "Missing service authority must fail closed.");

        var reversed = first.Payload with
        {
            ConsumerRef = first.Payload.ProviderRef,
            ProviderRef = first.Payload.ConsumerRef,
        };
        ExpectInvalid(() => Qa04InfrastructureDependencyCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, reversed), materialization.ServiceAuthority),
            "Reversed persistent dependency refs must fail closed.");

        var wrongPartition = first.Payload with
        {
            ConsumerRef = new PartitionRecordRefV1(InfrastructurePowerServicePayloadV1.PartitionId, first.Payload.ConsumerRef.RecordId),
        };
        ExpectInvalid(() => Qa04InfrastructureDependencyCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, wrongPartition), materialization.ServiceAuthority),
            "Wrong service partition must fail closed.");

        ExpectInvalid(() => Qa04InfrastructureDependencyCanonicalAuthorityV1.ValidateIdentityUniqueness(new[] { first, first }),
            "Duplicate dependency identity must fail closed.");

        ExpectInvalid(() => DeterministicOutageCascadeV1.Propagate(
                Array.Empty<OpaqueId128>(),
                new[] { new InfrastructureDependencyV1(power0, power0) }),
            "Self dependency must fail closed.");
        ExpectInvalid(() => DeterministicOutageCascadeV1.Propagate(
                Array.Empty<OpaqueId128>(),
                new[]
                {
                    new InfrastructureDependencyV1(power0, water0),
                    new InfrastructureDependencyV1(water0, power0),
                }),
            "Dependency cycle must fail closed.");

        var badRatio = first.Payload with { MinimumServicePpm = 999_999 };
        ExpectInvalid(() => Qa04InfrastructureDependencyCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, badRatio), materialization.ServiceAuthority),
            "Dependency minimum_service_ppm drift must fail closed.");
        var badKind = first.Payload with { DependencyKind = new StableToken("invalid-dependency") };
        ExpectInvalid(() => Qa04InfrastructureDependencyCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, badKind), materialization.ServiceAuthority),
            "Dependency kind Token drift must fail closed.");
        var badStatus = first.Payload with { Status = new StableToken("inactive") };
        ExpectInvalid(() => Qa04InfrastructureDependencyCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, badStatus), materialization.ServiceAuthority),
            "Dependency genesis status drift must fail closed.");
    }

    private static DomainRecordEnvelopeV1<InfrastructureDependencyPayloadV1> CopyWithPayload(
        DomainRecordEnvelopeV1<InfrastructureDependencyPayloadV1> record,
        InfrastructureDependencyPayloadV1 payload)
        => new(
            record.RecordId,
            record.RecordSchema,
            record.Revision,
            record.CreatedStep,
            record.RetiredStep,
            record.DetailLevel,
            record.LineageRef,
            payload);

    private static void ExpectInvalid(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => false;

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = default;
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
