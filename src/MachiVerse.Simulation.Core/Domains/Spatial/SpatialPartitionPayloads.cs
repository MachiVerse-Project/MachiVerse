using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Spatial;

public sealed record SpatialWorldFramePayloadV1(
    StableToken FrameKind,
    PartitionRecordRefV1? ParentFrame,
    global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1 Translation,
    global::MachiVerse.Simulation.Core.WorldState.QuaternionQ30V1 Rotation,
    PartitionRecordRefV1 ValidScope,
    ulong TransformRevision)
{
    public const string PartitionId = "spatial.world_frame";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SpatialPayloadFields.Map(
            ("frame_kind", FrameKind.Value),
            ("translation", Translation),
            ("rotation", Rotation),
            ("valid_scope", ValidScope),
            ("transform_revision", TransformRevision));
        SpatialPayloadFields.AddOptional(values, "parent_frame", ParentFrame);
        return values;
    }

    public byte[] CanonicalDigest() => SpatialPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static SpatialWorldFramePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            new StableToken(SpatialPayloadFields.Required<string>(values, PartitionId, "frame_kind")),
            SpatialPayloadFields.Optional<PartitionRecordRefV1>(values, "parent_frame"),
            SpatialPayloadFields.Required<global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1>(values, PartitionId, "translation"),
            SpatialPayloadFields.Required<global::MachiVerse.Simulation.Core.WorldState.QuaternionQ30V1>(values, PartitionId, "rotation"),
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "valid_scope"),
            SpatialPayloadFields.Required<ulong>(values, PartitionId, "transform_revision"));
}

public sealed record SpatialScopeRegistryPayloadV1(
    StableToken ScopeClass,
    PartitionRecordRefV1 GeometryRef,
    PartitionRecordRefV1? ParentScope,
    ulong ActiveFrom,
    ulong? RetiredAt,
    uint ScopeFlags)
{
    public const string PartitionId = "spatial.scope_registry";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SpatialPayloadFields.Map(
            ("scope_class", ScopeClass.Value),
            ("geometry_ref", GeometryRef),
            ("active_from", ActiveFrom),
            ("scope_flags", ScopeFlags));
        SpatialPayloadFields.AddOptional(values, "parent_scope", ParentScope);
        SpatialPayloadFields.AddOptional(values, "retired_at", RetiredAt);
        return values;
    }

    public byte[] CanonicalDigest() => SpatialPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static SpatialScopeRegistryPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            new StableToken(SpatialPayloadFields.Required<string>(values, PartitionId, "scope_class")),
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "geometry_ref"),
            SpatialPayloadFields.Optional<PartitionRecordRefV1>(values, "parent_scope"),
            SpatialPayloadFields.Required<ulong>(values, PartitionId, "active_from"),
            SpatialPayloadFields.Optional<ulong>(values, "retired_at"),
            SpatialPayloadFields.Required<uint>(values, PartitionId, "scope_flags"));
}

public sealed record SpatialTerrainGeometryPayloadV1(
    PartitionRecordRefV1 ScopeRef,
    PartitionRecordRefV1 RootBrickRef,
    ulong GeometryRevision,
    IReadOnlyList<StableToken> SurfaceClasses,
    IReadOnlyList<PartitionRecordRefV1> ConnectivityRefs,
    byte[]? ArchiveAnchor)
{
    public const string PartitionId = "spatial.terrain_geometry";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SpatialPayloadFields.Map(
            ("scope_ref", ScopeRef),
            ("root_brick_ref", RootBrickRef),
            ("geometry_revision", GeometryRevision),
            ("surface_classes", SpatialPayloadFields.TokenValues(SurfaceClasses)),
            ("connectivity_refs", ConnectivityRefs));
        if (ArchiveAnchor is { } digest) values["archive_anchor"] = digest.ToArray();
        return values;
    }

    public byte[] CanonicalDigest() => SpatialPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static SpatialTerrainGeometryPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "scope_ref"),
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "root_brick_ref"),
            SpatialPayloadFields.Required<ulong>(values, PartitionId, "geometry_revision"),
            SpatialPayloadFields.Tokens(values, PartitionId, "surface_classes"),
            SpatialPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "connectivity_refs"),
            SpatialPayloadFields.OptionalDigest(values, "archive_anchor"));
}

public sealed record SpatialVoidGeometryPayloadV1(
    PartitionRecordRefV1 GeometryRef,
    IReadOnlyList<PartitionRecordRefV1> Connectivity,
    IReadOnlyList<PartitionRecordRefV1> Entrances,
    StableToken OriginClass,
    StableToken Lifecycle,
    ulong GeometryRevision)
{
    public const string PartitionId = "spatial.void_geometry";

    public IReadOnlyDictionary<string, object?> ToStandardPayload() => SpatialPayloadFields.Map(
        ("geometry_ref", GeometryRef),
        ("connectivity", Connectivity),
        ("entrances", Entrances),
        ("origin_class", OriginClass.Value),
        ("lifecycle", Lifecycle.Value),
        ("geometry_revision", GeometryRevision));

    public byte[] CanonicalDigest() => SpatialPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static SpatialVoidGeometryPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "geometry_ref"),
            SpatialPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "connectivity"),
            SpatialPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "entrances"),
            new StableToken(SpatialPayloadFields.Required<string>(values, PartitionId, "origin_class")),
            new StableToken(SpatialPayloadFields.Required<string>(values, PartitionId, "lifecycle")),
            SpatialPayloadFields.Required<ulong>(values, PartitionId, "geometry_revision"));
}

public sealed record SpatialContainmentTopologyPayloadV1(
    PartitionRecordRefV1 SubjectRef,
    PartitionRecordRefV1 ContainerScope,
    StableToken RelationClass,
    ulong BasisGeometryRevision)
{
    public const string PartitionId = "spatial.containment_topology";

    public IReadOnlyDictionary<string, object?> ToStandardPayload() => SpatialPayloadFields.Map(
        ("subject_ref", SubjectRef),
        ("container_scope", ContainerScope),
        ("relation_class", RelationClass.Value),
        ("basis_geometry_revision", BasisGeometryRevision));

    public byte[] CanonicalDigest() => SpatialPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static SpatialContainmentTopologyPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "container_scope"),
            new StableToken(SpatialPayloadFields.Required<string>(values, PartitionId, "relation_class")),
            SpatialPayloadFields.Required<ulong>(values, PartitionId, "basis_geometry_revision"));
}

public sealed record SpatialBoundaryTopologyPayloadV1(
    PartitionRecordRefV1 ScopeA,
    PartitionRecordRefV1 ScopeB,
    PartitionRecordRefV1 InterfaceGeometryRef,
    IReadOnlyList<StableToken> PermeabilityClasses,
    PartitionRecordRefV1? DetailPolicyRef,
    ulong Revision)
{
    public const string PartitionId = "spatial.boundary_topology";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SpatialPayloadFields.Map(
            ("scope_a", ScopeA),
            ("scope_b", ScopeB),
            ("interface_geometry_ref", InterfaceGeometryRef),
            ("permeability_classes", SpatialPayloadFields.TokenValues(PermeabilityClasses)),
            ("revision", Revision));
        SpatialPayloadFields.AddOptional(values, "detail_policy_ref", DetailPolicyRef);
        return values;
    }

    public byte[] CanonicalDigest() => SpatialPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static SpatialBoundaryTopologyPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "scope_a"),
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "scope_b"),
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "interface_geometry_ref"),
            SpatialPayloadFields.Tokens(values, PartitionId, "permeability_classes"),
            SpatialPayloadFields.Optional<PartitionRecordRefV1>(values, "detail_policy_ref"),
            SpatialPayloadFields.Required<ulong>(values, PartitionId, "revision"));
}

public sealed record SpatialDetailRegionsPayloadV1(
    PartitionRecordRefV1 ScopeRef,
    IReadOnlyList<KeyValuePair<string, byte>> LevelByDomain,
    uint LineageGeneration,
    ulong LastTransitionStep,
    IReadOnlyList<StableToken> ActiveGuards)
{
    public const string PartitionId = "spatial.detail_regions";

    public IReadOnlyDictionary<string, object?> ToStandardPayload() => SpatialPayloadFields.Map(
        ("scope_ref", ScopeRef),
        ("level_by_domain", LevelByDomain),
        ("lineage_generation", LineageGeneration),
        ("last_transition_step", LastTransitionStep),
        ("active_guards", SpatialPayloadFields.TokenValues(ActiveGuards)));

    public byte[] CanonicalDigest() => SpatialPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static SpatialDetailRegionsPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "scope_ref"),
            SpatialPayloadFields.Required<IReadOnlyList<KeyValuePair<string, byte>>>(values, PartitionId, "level_by_domain"),
            SpatialPayloadFields.Required<uint>(values, PartitionId, "lineage_generation"),
            SpatialPayloadFields.Required<ulong>(values, PartitionId, "last_transition_step"),
            SpatialPayloadFields.Tokens(values, PartitionId, "active_guards"));
}

public sealed record SpatialGeometryLineagePayloadV1(
    PartitionRecordRefV1 SubjectRef,
    IReadOnlyList<PartitionRecordRefV1> ParentRefs,
    StableToken CreationKind,
    PartitionRecordRefV1? CreationRef,
    uint Generation,
    byte[] SourceDigest)
{
    public const string PartitionId = "spatial.geometry_lineage";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = SpatialPayloadFields.Map(
            ("subject_ref", SubjectRef),
            ("parent_refs", ParentRefs),
            ("creation_kind", CreationKind.Value),
            ("generation", Generation),
            ("source_digest", SourceDigest));
        SpatialPayloadFields.AddOptional(values, "creation_ref", CreationRef);
        return values;
    }

    public byte[] CanonicalDigest() => SpatialPayloadFields.Digest(PartitionId, ToStandardPayload());

    public static SpatialGeometryLineagePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            SpatialPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
            SpatialPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "parent_refs"),
            new StableToken(SpatialPayloadFields.Required<string>(values, PartitionId, "creation_kind")),
            SpatialPayloadFields.Optional<PartitionRecordRefV1>(values, "creation_ref"),
            SpatialPayloadFields.Required<uint>(values, PartitionId, "generation"),
            SpatialPayloadFields.Required<byte[]>(values, PartitionId, "source_digest").ToArray());
}

public static class SpatialDomainSnapshotProviderV1
{
    public static IReadOnlyList<IDomainPartitionSnapshotSectionProviderV1> CreateAll()
    {
        IDomainPartitionSnapshotSectionProviderV1[] providers =
        {
            Provider<SpatialWorldFramePayloadV1>(SpatialWorldFramePayloadV1.PartitionId, static value => value.ToStandardPayload(), SpatialWorldFramePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SpatialScopeRegistryPayloadV1>(SpatialScopeRegistryPayloadV1.PartitionId, static value => value.ToStandardPayload(), SpatialScopeRegistryPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SpatialTerrainGeometryPayloadV1>(SpatialTerrainGeometryPayloadV1.PartitionId, static value => value.ToStandardPayload(), SpatialTerrainGeometryPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SpatialVoidGeometryPayloadV1>(SpatialVoidGeometryPayloadV1.PartitionId, static value => value.ToStandardPayload(), SpatialVoidGeometryPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SpatialContainmentTopologyPayloadV1>(SpatialContainmentTopologyPayloadV1.PartitionId, static value => value.ToStandardPayload(), SpatialContainmentTopologyPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SpatialBoundaryTopologyPayloadV1>(SpatialBoundaryTopologyPayloadV1.PartitionId, static value => value.ToStandardPayload(), SpatialBoundaryTopologyPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SpatialDetailRegionsPayloadV1>(SpatialDetailRegionsPayloadV1.PartitionId, static value => value.ToStandardPayload(), SpatialDetailRegionsPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<SpatialGeometryLineagePayloadV1>(SpatialGeometryLineagePayloadV1.PartitionId, static value => value.ToStandardPayload(), SpatialGeometryLineagePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
        };
        return Array.AsReadOnly(providers.OrderBy(static provider => provider.SectionId, StringComparer.Ordinal).ToArray());
    }

    public static IDomainPartitionSnapshotSectionProviderV1 CreateWorldFrame()
        => CreateAll().Single(static provider => provider.SectionId == SpatialWorldFramePayloadV1.PartitionId);

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

internal static class SpatialPayloadFields
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
            : throw new InvalidDataException($"spatial.snapshot-payload.required:{partitionId}:{field}");

    public static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string field)
        where T : struct
        => values.TryGetValue(field, out var value) && value is not null ? (T)value : null;

    public static byte[]? OptionalDigest(IReadOnlyDictionary<string, object?> values, string field)
        => values.TryGetValue(field, out var value) && value is byte[] digest ? digest.ToArray() : null;

    public static IReadOnlyList<string> TokenValues(IReadOnlyList<StableToken> tokens)
        => Array.AsReadOnly(tokens.Select(static token => token.Value).ToArray());

    public static IReadOnlyList<StableToken> Tokens(
        IReadOnlyDictionary<string, object?> values,
        string partitionId,
        string field)
    {
        var source = Required<IReadOnlyList<string>>(values, partitionId, field);
        return Array.AsReadOnly(source.Select(static token => new StableToken(token)).ToArray());
    }

    public static byte[] Digest(string partitionId, IReadOnlyDictionary<string, object?> payload)
        => StandardDomainPayloadCanonicalDigestV1.Compute(partitionId, payload);
}
