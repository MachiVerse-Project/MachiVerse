using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.InfrastructureInformation;

public sealed record InfrastructureNetworkTopologyPayloadV1(
    StableToken NetworkKind,
    IReadOnlyList<PartitionRecordRefV1> NodeRefs,
    IReadOnlyList<PartitionRecordRefV1> EdgeRefs,
    IReadOnlyList<PartitionRecordRefV1> OperatorRefs,
    IReadOnlyList<PartitionRecordRefV1> ScopeRefs,
    StableToken Status,
    ulong TopologyRevision)
{
    public const string PartitionId = "infrastructure.network_topology";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => InfrastructurePayloadFields.Map(
        ("network_kind", NetworkKind.Value),
        ("node_refs", NodeRefs),
        ("edge_refs", EdgeRefs),
        ("operator_refs", OperatorRefs),
        ("scope_refs", ScopeRefs),
        ("status", Status.Value),
        ("topology_revision", TopologyRevision));
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InfrastructureNetworkTopologyPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "network_kind")),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "node_refs"),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "edge_refs"),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "operator_refs"),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "scope_refs"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "status")),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "topology_revision"));
}

public sealed record InfrastructureTransportServicePayloadV1(
    PartitionRecordRefV1 NetworkRef,
    StableToken ServiceKind,
    IReadOnlyList<PartitionRecordRefV1> RouteRefs,
    ulong CapacityPerStep,
    ulong Load,
    PartitionRecordRefV1? ScheduleRef,
    uint AvailabilityPpm,
    StableToken Status)
{
    public const string PartitionId = "infrastructure.transport_service";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = InfrastructurePayloadFields.Map(
            ("network_ref", NetworkRef),
            ("service_kind", ServiceKind.Value),
            ("route_refs", RouteRefs),
            ("capacity_per_step", CapacityPerStep),
            ("load", Load),
            ("availability_ppm", AvailabilityPpm),
            ("status", Status.Value));
        InfrastructurePayloadFields.AddOptional(values, "schedule_ref", ScheduleRef);
        return values;
    }
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InfrastructureTransportServicePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "network_ref"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "service_kind")),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "route_refs"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "capacity_per_step"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "load"),
        InfrastructurePayloadFields.Optional<PartitionRecordRefV1>(values, "schedule_ref"),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "availability_ppm"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record InfrastructureWaterServicePayloadV1(
    PartitionRecordRefV1 NetworkRef,
    PartitionRecordRefV1 ServiceScopeRef,
    long SupplyMlPerStep,
    long DemandMlPerStep,
    long PressureHeadMm,
    uint QualityPpm,
    uint AvailabilityPpm,
    StableToken Status)
{
    public const string PartitionId = "infrastructure.water_service";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => InfrastructurePayloadFields.Map(
        ("network_ref", NetworkRef),
        ("service_scope_ref", ServiceScopeRef),
        ("supply_ml_per_step", SupplyMlPerStep),
        ("demand_ml_per_step", DemandMlPerStep),
        ("pressure_head_mm", PressureHeadMm),
        ("quality_ppm", QualityPpm),
        ("availability_ppm", AvailabilityPpm),
        ("status", Status.Value));
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InfrastructureWaterServicePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "network_ref"),
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "service_scope_ref"),
        InfrastructurePayloadFields.Required<long>(values, PartitionId, "supply_ml_per_step"),
        InfrastructurePayloadFields.Required<long>(values, PartitionId, "demand_ml_per_step"),
        InfrastructurePayloadFields.Required<long>(values, PartitionId, "pressure_head_mm"),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "quality_ppm"),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "availability_ppm"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record InfrastructurePowerServicePayloadV1(
    PartitionRecordRefV1 NetworkRef,
    PartitionRecordRefV1 ServiceScopeRef,
    long GenerationMw,
    long DemandMw,
    long DeliveredMw,
    uint AvailabilityPpm,
    StableToken Status)
{
    public const string PartitionId = "infrastructure.power_service";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => InfrastructurePayloadFields.Map(
        ("network_ref", NetworkRef),
        ("service_scope_ref", ServiceScopeRef),
        ("generation_mw", GenerationMw),
        ("demand_mw", DemandMw),
        ("delivered_mw", DeliveredMw),
        ("availability_ppm", AvailabilityPpm),
        ("status", Status.Value));
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InfrastructurePowerServicePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "network_ref"),
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "service_scope_ref"),
        InfrastructurePayloadFields.Required<long>(values, PartitionId, "generation_mw"),
        InfrastructurePayloadFields.Required<long>(values, PartitionId, "demand_mw"),
        InfrastructurePayloadFields.Required<long>(values, PartitionId, "delivered_mw"),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "availability_ppm"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record InfrastructureCommunicationServicePayloadV1(
    PartitionRecordRefV1 NetworkRef,
    PartitionRecordRefV1 ServiceScopeRef,
    ulong CapacityUnitsPerStep,
    ulong QueuedUnits,
    uint LatencySteps,
    uint AvailabilityPpm,
    StableToken Status)
{
    public const string PartitionId = "infrastructure.communication_service";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => InfrastructurePayloadFields.Map(
        ("network_ref", NetworkRef),
        ("service_scope_ref", ServiceScopeRef),
        ("capacity_units_per_step", CapacityUnitsPerStep),
        ("queued_units", QueuedUnits),
        ("latency_steps", LatencySteps),
        ("availability_ppm", AvailabilityPpm),
        ("status", Status.Value));
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InfrastructureCommunicationServicePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "network_ref"),
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "service_scope_ref"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "capacity_units_per_step"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "queued_units"),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "latency_steps"),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "availability_ppm"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record InfrastructureDependencyPayloadV1(
    PartitionRecordRefV1 ConsumerRef,
    PartitionRecordRefV1 ProviderRef,
    StableToken DependencyKind,
    uint MinimumServicePpm,
    PartitionRecordRefV1? DegradationCurveRef,
    IReadOnlyList<PartitionRecordRefV1> FallbackRefs,
    StableToken Status)
{
    public const string PartitionId = "infrastructure.dependency";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = InfrastructurePayloadFields.Map(
            ("consumer_ref", ConsumerRef),
            ("provider_ref", ProviderRef),
            ("dependency_kind", DependencyKind.Value),
            ("minimum_service_ppm", MinimumServicePpm),
            ("fallback_refs", FallbackRefs),
            ("status", Status.Value));
        InfrastructurePayloadFields.AddOptional(values, "degradation_curve_ref", DegradationCurveRef);
        return values;
    }
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InfrastructureDependencyPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "consumer_ref"),
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "provider_ref"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "dependency_kind")),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "minimum_service_ppm"),
        InfrastructurePayloadFields.Optional<PartitionRecordRefV1>(values, "degradation_curve_ref"),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "fallback_refs"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record InfrastructureFacilityServicePayloadV1(
    PartitionRecordRefV1 FacilityRef,
    StableToken ServiceKind,
    uint CapacityPerStep,
    uint ActiveLoad,
    IReadOnlyList<PartitionRecordRefV1> RequiredResourceRefs,
    uint AvailabilityPpm,
    StableToken Status)
{
    public const string PartitionId = "infrastructure.facility_service";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => InfrastructurePayloadFields.Map(
        ("facility_ref", FacilityRef),
        ("service_kind", ServiceKind.Value),
        ("capacity_per_step", CapacityPerStep),
        ("active_load", ActiveLoad),
        ("required_resource_refs", RequiredResourceRefs),
        ("availability_ppm", AvailabilityPpm),
        ("status", Status.Value));
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InfrastructureFacilityServicePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "facility_ref"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "service_kind")),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "capacity_per_step"),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "active_load"),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "required_resource_refs"),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "availability_ppm"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record InfrastructureServiceQueuePayloadV1(
    PartitionRecordRefV1 ServiceRef,
    PartitionRecordRefV1 RequesterRef,
    ulong EligibleStep,
    int SemanticPriority,
    ulong RequestedUnits,
    ulong AllocatedUnits,
    StableToken Status)
{
    public const string PartitionId = "infrastructure.service_queue";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => InfrastructurePayloadFields.Map(
        ("service_ref", ServiceRef),
        ("requester_ref", RequesterRef),
        ("eligible_step", EligibleStep),
        ("semantic_priority", SemanticPriority),
        ("requested_units", RequestedUnits),
        ("allocated_units", AllocatedUnits),
        ("status", Status.Value));
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InfrastructureServiceQueuePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "service_ref"),
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "requester_ref"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "eligible_step"),
        InfrastructurePayloadFields.Required<int>(values, PartitionId, "semantic_priority"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "requested_units"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "allocated_units"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record InformationDeliveryPayloadV1(
    PartitionRecordRefV1 ContentRef,
    PartitionRecordRefV1 SenderRef,
    IReadOnlyList<PartitionRecordRefV1> RecipientRefs,
    PartitionRecordRefV1 ChannelRef,
    ulong EligibleStep,
    ulong? DeliveredStep,
    int Priority,
    StableToken Status,
    byte[] ContentDigest)
{
    public const string PartitionId = "information.delivery";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = InfrastructurePayloadFields.Map(
            ("content_ref", ContentRef),
            ("sender_ref", SenderRef),
            ("recipient_refs", RecipientRefs),
            ("channel_ref", ChannelRef),
            ("eligible_step", EligibleStep),
            ("priority", Priority),
            ("status", Status.Value),
            ("content_digest", ContentDigest));
        InfrastructurePayloadFields.AddOptional(values, "delivered_step", DeliveredStep);
        return values;
    }
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InformationDeliveryPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "content_ref"),
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "sender_ref"),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "recipient_refs"),
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "channel_ref"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "eligible_step"),
        InfrastructurePayloadFields.Optional<ulong>(values, "delivered_step"),
        InfrastructurePayloadFields.Required<int>(values, PartitionId, "priority"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "status")),
        InfrastructurePayloadFields.Required<byte[]>(values, PartitionId, "content_digest").ToArray());
}

public sealed record InformationMediaDistributionPayloadV1(
    PartitionRecordRefV1 ClaimRef,
    PartitionRecordRefV1 PublisherRef,
    IReadOnlyList<PartitionRecordRefV1> ChannelRefs,
    IReadOnlyList<PartitionRecordRefV1> AudienceScopeRefs,
    ulong PublishedStep,
    ulong ReachCount,
    StableToken Status)
{
    public const string PartitionId = "information.media_distribution";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => InfrastructurePayloadFields.Map(
        ("claim_ref", ClaimRef),
        ("publisher_ref", PublisherRef),
        ("channel_refs", ChannelRefs),
        ("audience_scope_refs", AudienceScopeRefs),
        ("published_step", PublishedStep),
        ("reach_count", ReachCount),
        ("status", Status.Value));
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InformationMediaDistributionPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "claim_ref"),
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "publisher_ref"),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "channel_refs"),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "audience_scope_refs"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "published_step"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "reach_count"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record InformationRecordStorePayloadV1(
    StableToken RecordKind,
    PartitionRecordRefV1? AuthorityRef,
    IReadOnlyList<PartitionRecordRefV1> SubjectRefs,
    byte[] ContentDigest,
    uint Version,
    ulong CreatedStep,
    bool Available,
    PartitionRecordRefV1? SupersedesRef)
{
    public const string PartitionId = "information.record_store";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = InfrastructurePayloadFields.Map(
            ("record_kind", RecordKind.Value),
            ("subject_refs", SubjectRefs),
            ("content_digest", ContentDigest),
            ("version", Version),
            ("created_step", CreatedStep),
            ("available", Available));
        InfrastructurePayloadFields.AddOptional(values, "authority_ref", AuthorityRef);
        InfrastructurePayloadFields.AddOptional(values, "supersedes_ref", SupersedesRef);
        return values;
    }
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InformationRecordStorePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "record_kind")),
        InfrastructurePayloadFields.Optional<PartitionRecordRefV1>(values, "authority_ref"),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "subject_refs"),
        InfrastructurePayloadFields.Required<byte[]>(values, PartitionId, "content_digest").ToArray(),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "version"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "created_step"),
        InfrastructurePayloadFields.Required<bool>(values, PartitionId, "available"),
        InfrastructurePayloadFields.Optional<PartitionRecordRefV1>(values, "supersedes_ref"));
}

public sealed record InformationAddressPlaceIndexPayloadV1(
    PartitionRecordRefV1 PlaceRef,
    StableToken AddressToken,
    PartitionRecordRefV1 ScopeRef,
    ulong ValidFrom,
    ulong? ValidUntil,
    IReadOnlyList<StableToken> Aliases)
{
    public const string PartitionId = "information.address_place_index";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = InfrastructurePayloadFields.Map(
            ("place_ref", PlaceRef),
            ("address_token", AddressToken.Value),
            ("scope_ref", ScopeRef),
            ("valid_from", ValidFrom),
            ("aliases", InfrastructurePayloadFields.TokenValues(Aliases)));
        InfrastructurePayloadFields.AddOptional(values, "valid_until", ValidUntil);
        return values;
    }
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InformationAddressPlaceIndexPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "place_ref"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "address_token")),
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "scope_ref"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "valid_from"),
        InfrastructurePayloadFields.Optional<ulong>(values, "valid_until"),
        InfrastructurePayloadFields.Tokens(values, PartitionId, "aliases"));
}

public sealed record InfrastructureFailureRecoveryPayloadV1(
    PartitionRecordRefV1 SubjectRef,
    StableToken FailureKind,
    uint SeverityPpm,
    ulong StartedStep,
    uint RecoveryProgressPpm,
    ulong? ExpectedRestoreStep,
    IReadOnlyList<PartitionRecordRefV1> DependencyRefs,
    StableToken Status)
{
    public const string PartitionId = "infrastructure.failure_recovery";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = InfrastructurePayloadFields.Map(
            ("subject_ref", SubjectRef),
            ("failure_kind", FailureKind.Value),
            ("severity_ppm", SeverityPpm),
            ("started_step", StartedStep),
            ("recovery_progress_ppm", RecoveryProgressPpm),
            ("dependency_refs", DependencyRefs),
            ("status", Status.Value));
        InfrastructurePayloadFields.AddOptional(values, "expected_restore_step", ExpectedRestoreStep);
        return values;
    }
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InfrastructureFailureRecoveryPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "failure_kind")),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "severity_ppm"),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "started_step"),
        InfrastructurePayloadFields.Required<uint>(values, PartitionId, "recovery_progress_ppm"),
        InfrastructurePayloadFields.Optional<ulong>(values, "expected_restore_step"),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "dependency_refs"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record InfrastructureLineagePayloadV1(
    PartitionRecordRefV1 SubjectRef,
    IReadOnlyList<PartitionRecordRefV1> PredecessorRefs,
    StableToken ChangeKind,
    ulong EffectiveStep,
    byte[] SourceDigest)
{
    public const string PartitionId = "infrastructure.lineage";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => InfrastructurePayloadFields.Map(
        ("subject_ref", SubjectRef),
        ("predecessor_refs", PredecessorRefs),
        ("change_kind", ChangeKind.Value),
        ("effective_step", EffectiveStep),
        ("source_digest", SourceDigest));
    public byte[] CanonicalDigest() => InfrastructurePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static InfrastructureLineagePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        InfrastructurePayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
        InfrastructurePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "predecessor_refs"),
        new StableToken(InfrastructurePayloadFields.Required<string>(values, PartitionId, "change_kind")),
        InfrastructurePayloadFields.Required<ulong>(values, PartitionId, "effective_step"),
        InfrastructurePayloadFields.Required<byte[]>(values, PartitionId, "source_digest").ToArray());
}

public static class InfrastructureInformationDomainSnapshotProviderV1
{
    public static IReadOnlyList<IDomainPartitionSnapshotSectionProviderV1> CreateAll()
    {
        IDomainPartitionSnapshotSectionProviderV1[] providers =
        {
            Provider<InfrastructureNetworkTopologyPayloadV1>(InfrastructureNetworkTopologyPayloadV1.PartitionId, static value => value.ToStandardPayload(), InfrastructureNetworkTopologyPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InfrastructureTransportServicePayloadV1>(InfrastructureTransportServicePayloadV1.PartitionId, static value => value.ToStandardPayload(), InfrastructureTransportServicePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InfrastructureWaterServicePayloadV1>(InfrastructureWaterServicePayloadV1.PartitionId, static value => value.ToStandardPayload(), InfrastructureWaterServicePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InfrastructurePowerServicePayloadV1>(InfrastructurePowerServicePayloadV1.PartitionId, static value => value.ToStandardPayload(), InfrastructurePowerServicePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InfrastructureCommunicationServicePayloadV1>(InfrastructureCommunicationServicePayloadV1.PartitionId, static value => value.ToStandardPayload(), InfrastructureCommunicationServicePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InfrastructureDependencyPayloadV1>(InfrastructureDependencyPayloadV1.PartitionId, static value => value.ToStandardPayload(), InfrastructureDependencyPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InfrastructureFacilityServicePayloadV1>(InfrastructureFacilityServicePayloadV1.PartitionId, static value => value.ToStandardPayload(), InfrastructureFacilityServicePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InfrastructureServiceQueuePayloadV1>(InfrastructureServiceQueuePayloadV1.PartitionId, static value => value.ToStandardPayload(), InfrastructureServiceQueuePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InformationDeliveryPayloadV1>(InformationDeliveryPayloadV1.PartitionId, static value => value.ToStandardPayload(), InformationDeliveryPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InformationMediaDistributionPayloadV1>(InformationMediaDistributionPayloadV1.PartitionId, static value => value.ToStandardPayload(), InformationMediaDistributionPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InformationRecordStorePayloadV1>(InformationRecordStorePayloadV1.PartitionId, static value => value.ToStandardPayload(), InformationRecordStorePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InformationAddressPlaceIndexPayloadV1>(InformationAddressPlaceIndexPayloadV1.PartitionId, static value => value.ToStandardPayload(), InformationAddressPlaceIndexPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InfrastructureFailureRecoveryPayloadV1>(InfrastructureFailureRecoveryPayloadV1.PartitionId, static value => value.ToStandardPayload(), InfrastructureFailureRecoveryPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<InfrastructureLineagePayloadV1>(InfrastructureLineagePayloadV1.PartitionId, static value => value.ToStandardPayload(), InfrastructureLineagePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
        };
        return Array.AsReadOnly(providers.OrderBy(static provider => provider.SectionId, StringComparer.Ordinal).ToArray());
    }

    private static IDomainPartitionSnapshotSectionProviderV1 Provider<TPayload>(
        string partitionId,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandard,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandard,
        Func<TPayload, byte[]> digest)
        => new DomainPartitionSnapshotSectionProviderV1<TPayload>(partitionId, toStandard, fromStandard, digest, StandardDomainNestedSnapshotCodecRegistryV1.Default);
}

internal static class InfrastructurePayloadFields
{
    public static Dictionary<string, object?> Map(params (string Name, object? Value)[] values)
        => values.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal);
    public static void AddOptional<T>(Dictionary<string, object?> values, string field, T? value) where T : struct
    {
        if (value is { } present) values[field] = present;
    }
    public static T Required<T>(IReadOnlyDictionary<string, object?> values, string partitionId, string field)
        => values.TryGetValue(field, out var value) && value is T typed
            ? typed
            : throw new InvalidDataException($"infrastructure.snapshot-payload.required:{partitionId}:{field}");
    public static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string field) where T : struct
        => values.TryGetValue(field, out var value) && value is not null ? (T)value : null;
    public static IReadOnlyList<string> TokenValues(IReadOnlyList<StableToken> tokens)
        => Array.AsReadOnly(tokens.Select(static token => token.Value).ToArray());
    public static IReadOnlyList<StableToken> Tokens(IReadOnlyDictionary<string, object?> values, string partitionId, string field)
    {
        var source = Required<IReadOnlyList<string>>(values, partitionId, field);
        return Array.AsReadOnly(source.Select(static token => new StableToken(token)).ToArray());
    }
    public static byte[] Digest(string partitionId, IReadOnlyDictionary<string, object?> payload)
        => StandardDomainPayloadCanonicalDigestV1.Compute(partitionId, payload);
}
