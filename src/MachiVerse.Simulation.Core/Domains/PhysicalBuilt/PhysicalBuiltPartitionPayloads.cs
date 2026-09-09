using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

/// <summary>
/// Exact P4-05 authoritative payloads for the Physical/Built owner. They intentionally remain
/// separate from earlier simulation convenience states when those states do not contain the full
/// lossless P4-05 field set.
/// </summary>
public sealed record PhysicalPresencePayloadV1(
    PartitionRecordRefV1 SubjectRef,
    PartitionRecordRefV1 FrameRef,
    Vec3Int64V1 Position,
    QuaternionQ30V1 Orientation,
    Vec3Int64V1 LinearVelocity,
    Vec3Int64V1 AngularRateUradPerSecond,
    PartitionRecordRefV1 ShapeRef,
    PartitionRecordRefV1? ContainmentRef,
    StableToken PresenceMode)
{
    public const string PartitionId = "physical.presence";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = PhysicalBuiltPayloadFields.Map(
            ("subject_ref", SubjectRef),
            ("frame_ref", FrameRef),
            ("position", Position),
            ("orientation", Orientation),
            ("linear_velocity", LinearVelocity),
            ("angular_rate_urad_s", AngularRateUradPerSecond),
            ("shape_ref", ShapeRef),
            ("presence_mode", PresenceMode.Value));
        PhysicalBuiltPayloadFields.AddOptional(values, "containment_ref", ContainmentRef);
        return values;
    }

    public byte[] CanonicalDigest() => PhysicalBuiltPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static PhysicalPresencePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "frame_ref"),
            PhysicalBuiltPayloadFields.Required<Vec3Int64V1>(values, PartitionId, "position"),
            PhysicalBuiltPayloadFields.Required<QuaternionQ30V1>(values, PartitionId, "orientation"),
            PhysicalBuiltPayloadFields.Required<Vec3Int64V1>(values, PartitionId, "linear_velocity"),
            PhysicalBuiltPayloadFields.Required<Vec3Int64V1>(values, PartitionId, "angular_rate_urad_s"),
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "shape_ref"),
            PhysicalBuiltPayloadFields.Optional<PartitionRecordRefV1>(values, "containment_ref"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "presence_mode")));
}

public sealed record PhysicalOccupancyPayloadV1(
    PartitionRecordRefV1 PresenceRef,
    Vec3Int64V1 AabbMin,
    Vec3Int64V1 AabbMax,
    IReadOnlyList<PartitionRecordRefV1> ContactRefs,
    uint OccupancyFlags,
    uint CollisionLayer)
{
    public const string PartitionId = "physical.occupancy";

    public IReadOnlyDictionary<string, object?> ToStandardPayload() => PhysicalBuiltPayloadFields.Map(
        ("presence_ref", PresenceRef),
        ("aabb_min", AabbMin),
        ("aabb_max", AabbMax),
        ("contact_refs", ContactRefs),
        ("occupancy_flags", OccupancyFlags),
        ("collision_layer", CollisionLayer));

    public byte[] CanonicalDigest() => PhysicalBuiltPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static PhysicalOccupancyPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "presence_ref"),
            PhysicalBuiltPayloadFields.Required<Vec3Int64V1>(values, PartitionId, "aabb_min"),
            PhysicalBuiltPayloadFields.Required<Vec3Int64V1>(values, PartitionId, "aabb_max"),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "contact_refs"),
            PhysicalBuiltPayloadFields.Required<uint>(values, PartitionId, "occupancy_flags"),
            PhysicalBuiltPayloadFields.Required<uint>(values, PartitionId, "collision_layer"));
}

public sealed record BuiltStructurePayloadV1(
    PartitionRecordRefV1 SpatialScope,
    StableToken StructureClass,
    IReadOnlyList<PartitionRecordRefV1> GeometryParts,
    IReadOnlyList<PartitionRecordRefV1> MaterialRefs,
    uint IntegrityPpm,
    IReadOnlyList<PartitionRecordRefV1> SupportRefs,
    StableToken Lifecycle)
{
    public const string PartitionId = "built.structure";

    public IReadOnlyDictionary<string, object?> ToStandardPayload() => PhysicalBuiltPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("structure_class", StructureClass.Value),
        ("geometry_parts", GeometryParts),
        ("material_refs", MaterialRefs),
        ("integrity_ppm", IntegrityPpm),
        ("support_refs", SupportRefs),
        ("lifecycle", Lifecycle.Value));

    public byte[] CanonicalDigest() => PhysicalBuiltPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static BuiltStructurePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "structure_class")),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "geometry_parts"),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "material_refs"),
            PhysicalBuiltPayloadFields.Required<uint>(values, PartitionId, "integrity_ppm"),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "support_refs"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "lifecycle")));
}

public sealed record BuiltSpacePayloadV1(
    PartitionRecordRefV1 StructureRef,
    PartitionRecordRefV1 SpatialScope,
    StableToken SpaceClass,
    IReadOnlyList<PartitionRecordRefV1> OpeningRefs,
    IReadOnlyList<PartitionRecordRefV1> AdjacentSpaceRefs,
    uint CapacityCount)
{
    public const string PartitionId = "built.space";

    public IReadOnlyDictionary<string, object?> ToStandardPayload() => PhysicalBuiltPayloadFields.Map(
        ("structure_ref", StructureRef),
        ("spatial_scope", SpatialScope),
        ("space_class", SpaceClass.Value),
        ("opening_refs", OpeningRefs),
        ("adjacent_space_refs", AdjacentSpaceRefs),
        ("capacity_count", CapacityCount));

    public byte[] CanonicalDigest() => PhysicalBuiltPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static BuiltSpacePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "structure_ref"),
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "space_class")),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "opening_refs"),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "adjacent_space_refs"),
            PhysicalBuiltPayloadFields.Required<uint>(values, PartitionId, "capacity_count"));
}

public sealed record BuiltOpeningPayloadV1(
    PartitionRecordRefV1 StructureRef,
    IReadOnlyList<PartitionRecordRefV1> SpaceRefs,
    StableToken OpeningClass,
    StableToken MechanismState,
    bool Locked,
    uint AperturePpm,
    PartitionRecordRefV1 GeometryRef)
{
    public const string PartitionId = "built.opening";

    public IReadOnlyDictionary<string, object?> ToStandardPayload() => PhysicalBuiltPayloadFields.Map(
        ("structure_ref", StructureRef),
        ("space_refs", SpaceRefs),
        ("opening_class", OpeningClass.Value),
        ("mechanism_state", MechanismState.Value),
        ("locked", Locked),
        ("aperture_ppm", AperturePpm),
        ("geometry_ref", GeometryRef));

    public byte[] CanonicalDigest() => PhysicalBuiltPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static BuiltOpeningPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "structure_ref"),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "space_refs"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "opening_class")),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "mechanism_state")),
            PhysicalBuiltPayloadFields.Required<bool>(values, PartitionId, "locked"),
            PhysicalBuiltPayloadFields.Required<uint>(values, PartitionId, "aperture_ppm"),
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "geometry_ref"));
}

public sealed record PhysicalContainerLocationPayloadV1(
    PartitionRecordRefV1 SubjectRef,
    PartitionRecordRefV1 ContainerRef,
    StableToken? SlotToken,
    StableToken ContainmentMode,
    long Quantity,
    long MassGram)
{
    public const string PartitionId = "physical.container_location";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = PhysicalBuiltPayloadFields.Map(
            ("subject_ref", SubjectRef),
            ("container_ref", ContainerRef),
            ("containment_mode", ContainmentMode.Value),
            ("quantity", Quantity),
            ("mass_g", MassGram));
        if (SlotToken is { } slot) values["slot_token"] = slot.Value;
        return values;
    }

    public byte[] CanonicalDigest() => PhysicalBuiltPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static PhysicalContainerLocationPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "container_ref"),
            PhysicalBuiltPayloadFields.OptionalToken(values, "slot_token"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "containment_mode")),
            PhysicalBuiltPayloadFields.Required<long>(values, PartitionId, "quantity"),
            PhysicalBuiltPayloadFields.Required<long>(values, PartitionId, "mass_g"));
}

public sealed record BuiltWorksitePayloadV1(
    PartitionRecordRefV1 SpatialScope,
    StableToken WorkKind,
    IReadOnlyList<PartitionRecordRefV1> TargetRefs,
    uint ProgressPpm,
    IReadOnlyList<PartitionRecordRefV1> RequiredMaterialRefs,
    IReadOnlyList<PartitionRecordRefV1> ConsumedMaterialRefs,
    IReadOnlyList<PartitionRecordRefV1> WorkerRefs,
    StableToken Status)
{
    public const string PartitionId = "built.worksite";

    public IReadOnlyDictionary<string, object?> ToStandardPayload() => PhysicalBuiltPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("work_kind", WorkKind.Value),
        ("target_refs", TargetRefs),
        ("progress_ppm", ProgressPpm),
        ("required_material_refs", RequiredMaterialRefs),
        ("consumed_material_refs", ConsumedMaterialRefs),
        ("worker_refs", WorkerRefs),
        ("status", Status.Value));

    public byte[] CanonicalDigest() => PhysicalBuiltPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static BuiltWorksitePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "work_kind")),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "target_refs"),
            PhysicalBuiltPayloadFields.Required<uint>(values, PartitionId, "progress_ppm"),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "required_material_refs"),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "consumed_material_refs"),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "worker_refs"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record PhysicalConditionPayloadV1(
    PartitionRecordRefV1 SubjectRef,
    StableToken ConditionClass,
    uint IntegrityPpm,
    uint WearPpm,
    int? TemperatureMilliKelvin,
    IReadOnlyList<PartitionRecordRefV1> DamageRefs,
    ulong? MaintenanceDueStep)
{
    public const string PartitionId = "physical.condition";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = PhysicalBuiltPayloadFields.Map(
            ("subject_ref", SubjectRef),
            ("condition_class", ConditionClass.Value),
            ("integrity_ppm", IntegrityPpm),
            ("wear_ppm", WearPpm),
            ("damage_refs", DamageRefs));
        PhysicalBuiltPayloadFields.AddOptional(values, "temperature_mk", TemperatureMilliKelvin);
        PhysicalBuiltPayloadFields.AddOptional(values, "maintenance_due_step", MaintenanceDueStep);
        return values;
    }

    public byte[] CanonicalDigest() => PhysicalBuiltPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static PhysicalConditionPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "condition_class")),
            PhysicalBuiltPayloadFields.Required<uint>(values, PartitionId, "integrity_ppm"),
            PhysicalBuiltPayloadFields.Required<uint>(values, PartitionId, "wear_ppm"),
            PhysicalBuiltPayloadFields.Optional<int>(values, "temperature_mk"),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "damage_refs"),
            PhysicalBuiltPayloadFields.Optional<ulong>(values, "maintenance_due_step"));
}

public sealed record PhysicalCombustionPayloadV1(
    PartitionRecordRefV1 SubjectRef,
    StableToken CombustionState,
    long FuelMassGram,
    int TemperatureMilliKelvin,
    long HeatOutputMilliwatt,
    long SmokeMassGram,
    PartitionRecordRefV1? IgnitionRef)
{
    public const string PartitionId = "physical.combustion";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = PhysicalBuiltPayloadFields.Map(
            ("subject_ref", SubjectRef),
            ("combustion_state", CombustionState.Value),
            ("fuel_mass_g", FuelMassGram),
            ("temperature_mk", TemperatureMilliKelvin),
            ("heat_output_mw", HeatOutputMilliwatt),
            ("smoke_mass_g", SmokeMassGram));
        PhysicalBuiltPayloadFields.AddOptional(values, "ignition_ref", IgnitionRef);
        return values;
    }

    public byte[] CanonicalDigest() => PhysicalBuiltPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static PhysicalCombustionPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "combustion_state")),
            PhysicalBuiltPayloadFields.Required<long>(values, PartitionId, "fuel_mass_g"),
            PhysicalBuiltPayloadFields.Required<int>(values, PartitionId, "temperature_mk"),
            PhysicalBuiltPayloadFields.Required<long>(values, PartitionId, "heat_output_mw"),
            PhysicalBuiltPayloadFields.Required<long>(values, PartitionId, "smoke_mass_g"),
            PhysicalBuiltPayloadFields.Optional<PartitionRecordRefV1>(values, "ignition_ref"));
}

public sealed record PhysicalMaterialHandoffPayloadV1(
    OpaqueId128 TransactionRef,
    StableToken MaterialKind,
    PartitionRecordRefV1 SourceRef,
    PartitionRecordRefV1 TargetRef,
    long MassGram,
    StableToken HandoffState,
    ulong PreparedStep,
    ulong? CommittedStep)
{
    public const string PartitionId = "physical.material_handoff";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = PhysicalBuiltPayloadFields.Map(
            ("transaction_ref", TransactionRef),
            ("material_kind", MaterialKind.Value),
            ("source_ref", SourceRef),
            ("target_ref", TargetRef),
            ("mass_g", MassGram),
            ("handoff_state", HandoffState.Value),
            ("prepared_step", PreparedStep));
        PhysicalBuiltPayloadFields.AddOptional(values, "committed_step", CommittedStep);
        return values;
    }

    public byte[] CanonicalDigest() => PhysicalBuiltPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static PhysicalMaterialHandoffPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            PhysicalBuiltPayloadFields.Required<OpaqueId128>(values, PartitionId, "transaction_ref"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "material_kind")),
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "source_ref"),
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "target_ref"),
            PhysicalBuiltPayloadFields.Required<long>(values, PartitionId, "mass_g"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "handoff_state")),
            PhysicalBuiltPayloadFields.Required<ulong>(values, PartitionId, "prepared_step"),
            PhysicalBuiltPayloadFields.Optional<ulong>(values, "committed_step"));
}

public sealed record PhysicalLineagePayloadV1(
    PartitionRecordRefV1 SubjectRef,
    IReadOnlyList<PartitionRecordRefV1> ParentRefs,
    IReadOnlyList<PartitionRecordRefV1> MaterialSourceRefs,
    StableToken CreationKind,
    uint Generation,
    byte[] SourceDigest)
{
    public const string PartitionId = "physical.lineage";

    public IReadOnlyDictionary<string, object?> ToStandardPayload() => PhysicalBuiltPayloadFields.Map(
        ("subject_ref", SubjectRef),
        ("parent_refs", ParentRefs),
        ("material_source_refs", MaterialSourceRefs),
        ("creation_kind", CreationKind.Value),
        ("generation", Generation),
        ("source_digest", SourceDigest));

    public byte[] CanonicalDigest() => PhysicalBuiltPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static PhysicalLineagePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            PhysicalBuiltPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "parent_refs"),
            PhysicalBuiltPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "material_source_refs"),
            new StableToken(PhysicalBuiltPayloadFields.Required<string>(values, PartitionId, "creation_kind")),
            PhysicalBuiltPayloadFields.Required<uint>(values, PartitionId, "generation"),
            PhysicalBuiltPayloadFields.Required<byte[]>(values, PartitionId, "source_digest").ToArray());
}

public static class PhysicalBuiltDomainSnapshotProviderV1
{
    public static IReadOnlyList<IDomainPartitionSnapshotSectionProviderV1> CreateAll()
    {
        IDomainPartitionSnapshotSectionProviderV1[] providers =
        {
            Provider<PhysicalPresencePayloadV1>(PhysicalPresencePayloadV1.PartitionId, static value => value.ToStandardPayload(), PhysicalPresencePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<PhysicalOccupancyPayloadV1>(PhysicalOccupancyPayloadV1.PartitionId, static value => value.ToStandardPayload(), PhysicalOccupancyPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<BuiltStructurePayloadV1>(BuiltStructurePayloadV1.PartitionId, static value => value.ToStandardPayload(), BuiltStructurePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<BuiltSpacePayloadV1>(BuiltSpacePayloadV1.PartitionId, static value => value.ToStandardPayload(), BuiltSpacePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<BuiltOpeningPayloadV1>(BuiltOpeningPayloadV1.PartitionId, static value => value.ToStandardPayload(), BuiltOpeningPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<PhysicalContainerLocationPayloadV1>(PhysicalContainerLocationPayloadV1.PartitionId, static value => value.ToStandardPayload(), PhysicalContainerLocationPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<BuiltWorksitePayloadV1>(BuiltWorksitePayloadV1.PartitionId, static value => value.ToStandardPayload(), BuiltWorksitePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<PhysicalConditionPayloadV1>(PhysicalConditionPayloadV1.PartitionId, static value => value.ToStandardPayload(), PhysicalConditionPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<PhysicalCombustionPayloadV1>(PhysicalCombustionPayloadV1.PartitionId, static value => value.ToStandardPayload(), PhysicalCombustionPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<PhysicalMaterialHandoffPayloadV1>(PhysicalMaterialHandoffPayloadV1.PartitionId, static value => value.ToStandardPayload(), PhysicalMaterialHandoffPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<PhysicalLineagePayloadV1>(PhysicalLineagePayloadV1.PartitionId, static value => value.ToStandardPayload(), PhysicalLineagePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
        };
        return Array.AsReadOnly(providers.OrderBy(static provider => provider.SectionId, StringComparer.Ordinal).ToArray());
    }

    public static IDomainPartitionSnapshotSectionProviderV1 CreatePresence()
        => CreateAll().Single(static provider => provider.SectionId == PhysicalPresencePayloadV1.PartitionId);

    private static IDomainPartitionSnapshotSectionProviderV1 Provider<TPayload>(
        string partitionId,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandard,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandard,
        Func<TPayload, byte[]> digest)
        => new DomainPartitionSnapshotSectionProviderV1<TPayload>(
            partitionId,
            toStandard,
            fromStandard,
            digest,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
}

internal static class PhysicalBuiltPayloadFields
{
    public static Dictionary<string, object?> Map(params (string Name, object? Value)[] values)
        => values.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal);

    public static void AddOptional<T>(Dictionary<string, object?> values, string field, T? value)
        where T : struct
    {
        if (value is { } present) values[field] = present;
    }

    public static T Required<T>(IReadOnlyDictionary<string, object?> values, string partitionId, string field)
        => values.TryGetValue(field, out var value) && value is T typed
            ? typed
            : throw new InvalidDataException($"physical-built.snapshot-payload.required:{partitionId}:{field}");

    public static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string field)
        where T : struct
        => values.TryGetValue(field, out var value) && value is not null ? (T)value : null;

    public static StableToken? OptionalToken(IReadOnlyDictionary<string, object?> values, string field)
        => values.TryGetValue(field, out var value) && value is string token
            ? new StableToken(token)
            : null;

    public static byte[] Digest(string partitionId, IReadOnlyDictionary<string, object?> payload)
        => StandardDomainPayloadCanonicalDigestV1.Compute(partitionId, payload);
}
