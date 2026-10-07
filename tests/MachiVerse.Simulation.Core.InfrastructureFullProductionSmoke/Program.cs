using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void RequireInvalid(Action action, string message)
{
    try
    {
        action();
    }
    catch (InvalidDataException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static PartitionRecordRefV1 ScopeForTile(ushort tile)
    => Qa04SpatialTileScopeAuthorityV1.ScopeRef(tile);

static void VerifyEveryNodeHasFiveOutgoingEdges(
    IReadOnlyList<InfrastructureNetworkTopologyRecordMaterialV2> records)
{
    var outgoing = records
        .Where(static record => record.Payload is InfrastructureNetworkEdgePayloadV2)
        .Select(static record => (InfrastructureNetworkEdgePayloadV2)record.Payload)
        .GroupBy(static edge => edge.FromNodeRef.RecordId)
        .ToDictionary(static group => group.Key, static group => group.Count());
    var nodeIds = records
        .Where(static record => record.Payload is InfrastructureNetworkNodePayloadV2)
        .Select(static record => record.RecordId)
        .ToArray();
    Require(nodeIds.All(nodeId => outgoing.TryGetValue(nodeId, out var count) && count == 5),
        "Every canonical Infrastructure node must own exactly five outgoing links.");
}

static void VerifyWrongNetworkEdgeFailsClosed(
    IReadOnlyList<InfrastructureNetworkTopologyRecordMaterialV2> records)
{
    var network0 = records.Single(record => record.RecordId == Qa04InfrastructureNetworkMaterializerV1.NetworkId(0));
    var nodeIds = ((InfrastructureNetworkPayloadV2)network0.Payload).NodeRefs.Select(static reference => reference.RecordId).ToHashSet();
    var edgeIds = ((InfrastructureNetworkPayloadV2)network0.Payload).EdgeRefs.Select(static reference => reference.RecordId).ToHashSet();
    var slice = records.Where(record => record.RecordId == network0.RecordId || nodeIds.Contains(record.RecordId) || edgeIds.Contains(record.RecordId)).ToArray();
    var edgeIndex = Array.FindIndex(slice, static record => record.Payload is InfrastructureNetworkEdgePayloadV2);
    Require(edgeIndex >= 0, "Network zero closure fixture must include an edge.");
    var original = slice[edgeIndex];
    var edge = (InfrastructureNetworkEdgePayloadV2)original.Payload;
    slice[edgeIndex] = new InfrastructureNetworkTopologyRecordMaterialV2(
        original.RecordId,
        original.Revision,
        original.CreatedStep,
        original.RetiredStep,
        original.DetailLevel,
        original.LineageRef,
        new InfrastructureNetworkEdgePayloadV2(
            new PartitionRecordRefV1(
                InfrastructureNetworkTopologyRecordSchemaV2.PartitionId,
                Qa04InfrastructureNetworkMaterializerV1.NetworkId(1)),
            edge.FromNodeRef,
            edge.ToNodeRef,
            edge.EdgeKind,
            edge.Cost,
            edge.CapacityUnits,
            edge.AvailabilityPpm,
            edge.Status));

    try
    {
        InfrastructureNetworkTopologyReferenceClosureV2.Validate(slice);
    }
    catch (InvalidDataException)
    {
        return;
    }

    throw new InvalidOperationException("Edge owned by a different network must fail topology closure.");
}

Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();
Qa04InfrastructureNetworkMaterializerV1.ValidateCanonicalContract();
Console.WriteLine($"Validating full canonical Infrastructure topology ({Qa04InfrastructureNetworkMaterializerV1.CanonicalTopologyRecordCount:N0} records)...");

var records = Qa04InfrastructureNetworkMaterializerV1
    .MaterializeCanonicalTopology(ScopeForTile)
    .ToArray();

Require(records.Length == 120_100,
    "Canonical Infrastructure topology must materialize exactly 120,100 records.");
Require(records.Count(static record => record.Payload is InfrastructureNetworkPayloadV2) == 100,
    "Canonical Infrastructure topology must contain exactly 100 networks.");
Require(records.Count(static record => record.Payload is InfrastructureNetworkNodePayloadV2) == 20_000,
    "Canonical Infrastructure topology must contain exactly 20,000 nodes.");
Require(records.Count(static record => record.Payload is InfrastructureNetworkEdgePayloadV2) == 100_000,
    "Canonical Infrastructure topology must contain exactly 100,000 edges.");
Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
    "Canonical Infrastructure topology record ids must be globally unique.");

InfrastructureNetworkTopologyReferenceClosureV2.Validate(records);

var firstNetwork = Qa04InfrastructureNetworkMaterializerV1.CreateNetwork(0, ScopeForTile, out var networkBinding);
var networkPayload = firstNetwork.Payload as InfrastructureNetworkPayloadV2
    ?? throw new InvalidOperationException("First Infrastructure topology record must be a network.");
Require(networkPayload.NetworkKind.Value == "transport" &&
        networkPayload.NodeRefs.Count == 200 && networkPayload.EdgeRefs.Count == 1_000 &&
        networkPayload.ScopeRefs.Count == 1 && networkPayload.OperatorRefs.Count == 1 &&
        networkPayload.TopologyRevision == 1,
    "Canonical Infrastructure network payload drifted.");
Require(networkBinding.MaterialClass.Value == "network" && networkBinding.LocalOrdinal == 0 &&
        networkBinding.AuthoritativeRecordId == firstNetwork.RecordId,
    "Infrastructure network descriptor mapping evidence drifted.");

var firstNode = Qa04InfrastructureNetworkMaterializerV1.CreateNode(0, ScopeForTile, out var nodeBinding);
var nodePayload = firstNode.Payload as InfrastructureNetworkNodePayloadV2
    ?? throw new InvalidOperationException("First Infrastructure node must use node payload.");
Require(nodePayload.NetworkRef.RecordId == firstNetwork.RecordId &&
        nodePayload.NodeKind.Value == "junction" && nodePayload.CapacityUnits == 1_000 &&
        nodePayload.AvailabilityPpm == 1_000_000 && nodePayload.Status.Value == "active",
    "Canonical Infrastructure node payload drifted.");
Require(nodeBinding.MaterialClass.Value == "node" && nodeBinding.LocalOrdinal == 0,
    "Infrastructure node descriptor mapping evidence drifted.");

var firstEdge = Qa04InfrastructureNetworkMaterializerV1.CreateEdge(0, out var edgeBinding);
var edgePayload = firstEdge.Payload as InfrastructureNetworkEdgePayloadV2
    ?? throw new InvalidOperationException("First Infrastructure edge must use edge payload.");
Require(edgePayload.NetworkRef.RecordId == firstNetwork.RecordId &&
        edgePayload.FromNodeRef != edgePayload.ToNodeRef &&
        edgePayload.EdgeKind.Value == "link" && edgePayload.Cost == 1 &&
        edgePayload.CapacityUnits == 100 && edgePayload.AvailabilityPpm == 1_000_000,
    "Canonical Infrastructure edge payload drifted.");
Require(edgeBinding.MaterialClass.Value == "edge" && edgeBinding.LocalOrdinal == 0,
    "Infrastructure edge descriptor mapping evidence drifted.");

VerifyEveryNodeHasFiveOutgoingEdges(records);
VerifyWrongNetworkEdgeFailsClosed(records);

Console.WriteLine("Validating canonical FacilityService Built/Infrastructure authority...");
var facilityAuthority = Qa04FacilityServiceCanonicalAuthorityV1.MaterializeCanonical();
Require(facilityAuthority.BuiltStructures.ItemCount == Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount,
    "FacilityService proof must materialize exactly 15,000 BuiltStructure records.");
Require(facilityAuthority.FacilityServices.ItemCount == Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount,
    "FacilityService proof must materialize exactly 15,000 FacilityService records.");
Require(facilityAuthority.CanonicalServicePool.Count == checked((int)Qa04InfrastructureCanonicalServicePoolV1.CanonicalCount),
    "Infrastructure canonical service pool must contain exactly 55,000 records.");
Require(facilityAuthority.CanonicalServicePool.SequenceEqual(Qa04InfrastructureCanonicalServicePoolV1.Expected),
    "Infrastructure canonical service pool must exactly match the deterministic descriptor identity set.");

var facilitySnapshot = Qa04FacilityServiceSnapshotRecoveryEvidenceV1.Verify(facilityAuthority);
Require(facilitySnapshot.BuiltStructureCount == Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount &&
        facilitySnapshot.FacilityServiceCount == Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount,
    "FacilityService Built/Infrastructure Snapshot recovery must preserve the exact semantic record counts.");

Console.WriteLine("Validating aggregate 500,000-record Infrastructure/Information production authority...");
var serviceAuthority = facilityAuthority.ServiceAuthority;
var serviceRecovered = Qa04InfrastructureServiceQueueSnapshotRecoveryEvidenceV1.Verify(serviceAuthority);
Require(serviceRecovered == Qa04InfrastructureServiceQueueCanonicalMaterializerV1.CanonicalMaterializedCount,
    "Infrastructure service/queue Snapshot recovery count drifted.");

var dependencyAuthority = Qa04InfrastructureDependencyCanonicalAuthorityV1.MaterializeCanonical(serviceAuthority);
var dependencyRecovered = Qa04InfrastructureDependencySnapshotRecoveryEvidenceV1.Verify(dependencyAuthority);
Require(dependencyRecovered == Qa04InfrastructureDependencyCanonicalAuthorityV1.CanonicalCount,
    "Infrastructure dependency Snapshot recovery count drifted.");

var societyAuthority = Qa04SocietyGovernanceCanonicalMaterializerV1.MaterializeCanonical();
var topologyReferences = new CompositeReferenceResolver(serviceAuthority.References, societyAuthority.References);
var topologyState = new InfrastructureNetworkTopologyPartitionStateV2(records);
var topologyAuthority = InfrastructureNetworkTopologySnapshotAuthorityV2.CreateCanonical(
    topologyState,
    revision: 1,
    basisStep: 0,
    detailLevel: DetailLevelV1.D2RegionalAggregate);
var topologyProvider = new InfrastructureNetworkTopologySnapshotSectionProviderV2();
var topologySection = topologyProvider.Create(topologyAuthority, topologyReferences);
var topologyVerifier = topologyProvider.CreateSemanticVerifier(topologyAuthority.Header, topologyReferences);
var topologyRecovered = topologyVerifier.Verify(topologySection.Fragments);
Require(topologyRecovered.LogicalItemCount == Qa04InfrastructureNetworkMaterializerV1.CanonicalTopologyRecordCount &&
        CryptographicOperations.FixedTimeEquals(topologyRecovered.LogicalContentDigest, topologyAuthority.Header.CanonicalDigest),
    "Infrastructure topology Snapshot/recovery semantic proof drifted.");

var deliveryAuthority = Qa04InformationDeliveryCanonicalAuthorityV1.MaterializeCanonical(societyAuthority, serviceAuthority);
var deliveryRecovered = Qa04InformationDeliverySnapshotRecoveryEvidenceV1.Verify(deliveryAuthority);
Require(deliveryRecovered == Qa04InformationDeliveryCanonicalAuthorityV1.CanonicalCount,
    "InformationDelivery Snapshot recovery count drifted.");

var remainingInformation = Qa04RemainingInformationCanonicalAuthorityV1.MaterializeCanonical(societyAuthority, serviceAuthority);
var mediaRecovered = Qa04RemainingInformationSnapshotRecoveryEvidenceV1.VerifyMediaDistribution(remainingInformation);
var recordStoreRecovered = Qa04RemainingInformationSnapshotRecoveryEvidenceV1.VerifyRecordStore(remainingInformation);
Require(mediaRecovered == Qa04RemainingInformationCanonicalAuthorityV1.MediaDistributionCount &&
        recordStoreRecovered == Qa04RemainingInformationCanonicalAuthorityV1.RecordStoreCount,
    "Remaining Information Snapshot recovery count drifted.");

Require(Qa04InfrastructureTailFullProductionSmoke.VerifiedRecordCount == Qa04InfrastructureTailCanonicalAuthorityV1.CanonicalCount,
    "Infrastructure tail ModuleInitializer must prove all 19,900 tail records before aggregate publication.");
Require(Qa04InfrastructureReferenceDecompositionV1.Slices.Count == 16 &&
        Qa04InfrastructureReferenceDecompositionV1.Slices.Aggregate(0UL, static (sum, slice) => checked(sum + slice.Count)) ==
            Qa04InfrastructureReferenceDecompositionV1.CanonicalCount,
    "Infrastructure decomposition must remain an exact 16-slice / 500,000-record partition.");

var aggregateRecovered = checked(
    topologyRecovered.LogicalItemCount +
    serviceRecovered +
    dependencyRecovered +
    facilitySnapshot.FacilityServiceCount +
    deliveryRecovered +
    mediaRecovered +
    recordStoreRecovered +
    Qa04InfrastructureTailFullProductionSmoke.VerifiedRecordCount);
Require(aggregateRecovered == Qa04InfrastructureReferenceDecompositionV1.CanonicalCount,
    "Aggregate Infrastructure/Information production proof must recover exactly 500,000 canonical records without overlap or gaps.");

var firstBuilt = facilityAuthority.BuiltStructureRecordsByOrdinal[0];
var wrongBuiltPayload = firstBuilt.Payload with { IntegrityPpm = 999_999 };
var wrongBuilt = new DomainRecordEnvelopeV1<BuiltStructurePayloadV1>(
    firstBuilt.RecordId, firstBuilt.RecordSchema, firstBuilt.Revision, firstBuilt.CreatedStep,
    firstBuilt.RetiredStep, firstBuilt.DetailLevel, firstBuilt.LineageRef, wrongBuiltPayload);
RequireInvalid(
    () => Qa04FacilityServiceCanonicalAuthorityV1.ValidateCanonicalBuiltStructureRecord(
        0, wrongBuilt, facilityAuthority.PhysicalAuthority, facilityAuthority.References),
    "BuiltStructure payload drift must fail closed.");
RequireInvalid(
    () => Qa04FacilityServiceCanonicalAuthorityV1.ValidateCanonicalBuiltStructureRecord(
        0, firstBuilt, facilityAuthority.PhysicalAuthority, new MissingReferenceResolver()),
    "Missing BuiltStructure scope/shape authority must fail closed.");
RequireInvalid(
    () => Qa04FacilityServiceCanonicalAuthorityV1.ValidateBuiltStructureIdentityUniqueness(new[] { firstBuilt, firstBuilt }),
    "Duplicate BuiltStructure identity must fail closed.");

var firstFacility = facilityAuthority.FacilityServiceRecordsByOrdinal[0];
var wrongFacilityPayload = firstFacility.Payload with
{
    FacilityRef = Qa04FacilityServiceCanonicalAuthorityV1.BuiltStructureRef(1),
};
var wrongFacility = new DomainRecordEnvelopeV1<InfrastructureFacilityServicePayloadV1>(
    firstFacility.RecordId, firstFacility.RecordSchema, firstFacility.Revision, firstFacility.CreatedStep,
    firstFacility.RetiredStep, firstFacility.DetailLevel, firstFacility.LineageRef, wrongFacilityPayload);
RequireInvalid(
    () => Qa04FacilityServiceCanonicalAuthorityV1.ValidateCanonicalFacilityServiceRecord(
        0, wrongFacility, facilityAuthority.References),
    "FacilityService ordinal/facility relation drift must fail closed.");
RequireInvalid(
    () => Qa04FacilityServiceCanonicalAuthorityV1.ValidateCanonicalFacilityServiceRecord(
        0, firstFacility, new MissingReferenceResolver()),
    "Missing BuiltStructure target authority must fail closed.");
RequireInvalid(
    () => Qa04FacilityServiceCanonicalAuthorityV1.ValidateFacilityServiceIdentityUniqueness(new[] { firstFacility, firstFacility }),
    "Duplicate FacilityService identity must fail closed.");

var infrastructureDescriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
    .First(static descriptor => descriptor.FamilyToken.Value == "infrastructure-service-delivery");
var infrastructureBinding = Qa04CanonicalOperationBindingV1.Bind(infrastructureDescriptor, schedulingPolicyGeneration: 1);
Require(infrastructureBinding.Operation.OperationKind == "infrastructure.service.reserve" &&
        infrastructureBinding.OwnerDomain.Value == "infrastructure_information" &&
        infrastructureBinding.PrimaryTarget == Qa04InfrastructureCanonicalServicePoolV1.Resolve(infrastructureDescriptor.FamilyOrdinal) &&
        infrastructureBinding.BoundDescriptor.PayloadDigest.SequenceEqual(infrastructureBinding.Operation.ImmutablePayloadDigest.ToByteArray()),
    "Infrastructure Operation must bind to the production service reserve surface with canonical immutable payload digest.");
Require(Qa04CanonicalWorkloadDependencyContractV1.Blockers.Count == 0,
    "FacilityService proof must close the final QA-04 canonical workload blocker.");

Console.WriteLine(
    $"infrastructure-aggregate-full-production-pass records={aggregateRecovered} recovered={aggregateRecovered} " +
    $"topology={topologyRecovered.LogicalItemCount} services_queue={serviceRecovered} dependency={dependencyRecovered} " +
    $"facility={facilitySnapshot.FacilityServiceCount} delivery={deliveryRecovered} media={mediaRecovered} " +
    $"record_store={recordStoreRecovered} tail={Qa04InfrastructureTailFullProductionSmoke.VerifiedRecordCount} slices={Qa04InfrastructureReferenceDecompositionV1.Slices.Count}");
Console.WriteLine($"infrastructure-full-production-pass topology_records={records.Length} facility_records={facilityAuthority.FacilityServices.ItemCount} service_pool={facilityAuthority.CanonicalServicePool.Count}");

sealed class CompositeReferenceResolver : IDomainRecordSchemaResolverV1
{
    private readonly IDomainRecordSchemaResolverV1[] _sources;

    public CompositeReferenceResolver(params IDomainRecordSchemaResolverV1[] sources)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        if (_sources.Length == 0 || _sources.Any(static source => source is null))
            throw new ArgumentException("At least one non-null reference source is required.", nameof(sources));
    }

    public bool Exists(PartitionRecordRefV1 reference)
        => TryGetRecordSchema(reference, out _);

    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
    {
        SchemaRefV1? found = null;
        foreach (var source in _sources)
        {
            if (!source.TryGetRecordSchema(reference, out var candidate)) continue;
            if (found is { } existing && existing != candidate)
                throw new InvalidDataException("qa04.infrastructure.aggregate-reference-schema-conflict");
            found = candidate;
        }
        if (found is { } resolved)
        {
            schema = resolved;
            return true;
        }
        schema = default;
        return false;
    }
}

sealed class MissingReferenceResolver : IDomainRecordSchemaResolverV1
{
    public bool Exists(PartitionRecordRefV1 reference) => false;
    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
    {
        schema = default;
        return false;
    }
}
