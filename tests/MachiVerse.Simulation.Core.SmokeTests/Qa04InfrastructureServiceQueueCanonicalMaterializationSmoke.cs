using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04InfrastructureServiceQueueCanonicalMaterializationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04InfrastructureServiceQueueCanonicalMaterializerV1.ValidateCanonicalContract();
        var materialization = Qa04InfrastructureServiceQueueCanonicalMaterializerV1.MaterializeCanonical();

        Require(materialization.MaterializedRecordCount == 290_000,
            "Canonical Infrastructure service/queue proof must materialize exactly 290,000 records.");
        Require(materialization.TransportServices.ItemCount == 10_000 &&
                materialization.WaterServices.ItemCount == 10_000 &&
                materialization.PowerServices.ItemCount == 10_000 &&
                materialization.CommunicationServices.ItemCount == 10_000 &&
                materialization.ServiceQueue.ItemCount == 250_000,
            "Canonical Infrastructure service/queue partition cardinality drifted.");

        var recovered = Qa04InfrastructureServiceQueueSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(recovered == Qa04InfrastructureServiceQueueSnapshotRecoveryEvidenceV1.CanonicalRecordCount,
            "Canonical Infrastructure service/queue Snapshot/recovery must semantically recover all 290,000 records.");

        var transport9999 = FindDescriptorRecord(
            materialization.TransportServices,
            "transport_service",
            9_999);
        var transportNetworkOrdinal = 96u;
        var expectedTransportNetwork = new PartitionRecordRefV1(
            InfrastructureNetworkTopologyRecordSchemaV2.PartitionId,
            Qa04InfrastructureNetworkMaterializerV1.NetworkId(transportNetworkOrdinal));
        var expectedTransportEdge = new PartitionRecordRefV1(
            InfrastructureNetworkTopologyRecordSchemaV2.PartitionId,
            Qa04ReferenceScenariosV1.InfrastructureEdgeId(96_399));
        Require(transport9999.Payload.NetworkRef == expectedTransportNetwork &&
                transport9999.Payload.ServiceKind.Value == "perf.transport-service" &&
                transport9999.Payload.RouteRefs.SequenceEqual(new[] { expectedTransportEdge }) &&
                transport9999.Payload.CapacityPerStep == 1_000 &&
                transport9999.Payload.Load == 0 &&
                transport9999.Payload.ScheduleRef is null &&
                transport9999.Payload.AvailabilityPpm == 1_000_000 &&
                transport9999.Payload.Status.Value == "active",
            "Canonical TransportService authority drifted.");

        var communication0 = FindDescriptorRecord(
            materialization.CommunicationServices,
            "communication_service",
            0);
        var communication2500 = FindDescriptorRecord(
            materialization.CommunicationServices,
            "communication_service",
            2_500);
        Require(communication0.Payload.QueuedUnits == 7 && communication2500.Payload.QueuedUnits == 6 &&
                communication0.Payload.LatencySteps == 1 && communication0.Payload.AvailabilityPpm == 1_000_000,
            "CommunicationService queued_units must equal exact canonical ServiceQueue fan-in.");

        var queue0 = FindQueueRecord(materialization.ServiceQueue, 0);
        var queue39999 = FindQueueRecord(materialization.ServiceQueue, 39_999);
        var queue40000 = FindQueueRecord(materialization.ServiceQueue, 40_000);
        var transport0Ref = DescriptorRef("transport_service", InfrastructureTransportServicePayloadV1.PartitionId, 0);
        var communication9999Ref = DescriptorRef("communication_service", InfrastructureCommunicationServicePayloadV1.PartitionId, 9_999);
        Require(queue0.Payload.ServiceRef == transport0Ref &&
                queue39999.Payload.ServiceRef == communication9999Ref &&
                queue40000.Payload.ServiceRef == transport0Ref,
            "ServiceQueue canonical 40,000-service modulo mapping drifted.");

        var residentClass = new MachiVerse.Simulation.Core.Determinism.StableToken("resident.persistent-identity");
        var resident0 = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            Qa04ReferenceLoadV1.Record(residentClass, 0).RecordId);
        var resident249999 = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            Qa04ReferenceLoadV1.Record(residentClass, 249_999).RecordId);
        var queueLast = FindQueueRecord(materialization.ServiceQueue, 249_999);
        Require(queue0.Payload.RequesterRef == resident0 && queueLast.Payload.RequesterRef == resident249999 &&
                queue0.Payload.EligibleStep == 0 && queue0.Payload.SemanticPriority == 0 &&
                queue0.Payload.RequestedUnits == 1 && queue0.Payload.AllocatedUnits == 0 &&
                queue0.Payload.Status.Value == "queued",
            "ServiceQueue requester/genesis authority drifted.");

        Require(materialization.References.TryGetRecordSchema(expectedTransportNetwork, out var topologySchema) &&
                topologySchema == InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema,
            "Service authority resolver must retain the actual topology v2 target schema.");
        Require(materialization.References.TryGetRecordSchema(transport0Ref, out var serviceSchema) &&
                serviceSchema == StandardDomainPartitionRegistry.Get(InfrastructureTransportServicePayloadV1.PartitionId).RecordSchema,
            "Service authority resolver must retain actual service record schema.");
    }

    private static DomainRecordEnvelopeV1<TPayload> FindDescriptorRecord<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        string materialClass,
        ulong localOrdinal)
    {
        var slice = Qa04InfrastructureReferenceDecompositionV1.Get(materialClass);
        var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        return partition.RecordsCanonical.Single(record => record.RecordId == binding.Descriptor.RecordId);
    }

    private static DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1> FindQueueRecord(
        DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1> partition,
        int localOrdinal)
    {
        var id = Qa04ReferenceScenariosV1.InfrastructureServiceRequestId(localOrdinal);
        return partition.RecordsCanonical.Single(record => record.RecordId == id);
    }

    private static PartitionRecordRefV1 DescriptorRef(string materialClass, string partitionId, ulong localOrdinal)
    {
        var slice = Qa04InfrastructureReferenceDecompositionV1.Get(materialClass);
        var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        return new PartitionRecordRefV1(partitionId, binding.Descriptor.RecordId);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
