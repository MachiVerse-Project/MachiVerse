using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

/// <summary>
/// Exact P4-05 authoritative payload for physical.presence. This is intentionally separate from
/// the earlier simulation convenience state, whose field set is not lossless for the P4-05 record.
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
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["subject_ref"] = SubjectRef,
            ["frame_ref"] = FrameRef,
            ["position"] = Position,
            ["orientation"] = Orientation,
            ["linear_velocity"] = LinearVelocity,
            ["angular_rate_urad_s"] = AngularRateUradPerSecond,
            ["shape_ref"] = ShapeRef,
            ["presence_mode"] = PresenceMode.Value,
        };
        if (ContainmentRef is { } containment)
            values["containment_ref"] = containment;
        return values;
    }

    public byte[] CanonicalDigest()
        => StandardDomainPayloadCanonicalDigestV1.Compute(PartitionId, ToStandardPayload());

    public static PhysicalPresencePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            (PartitionRecordRefV1)Require(values, "subject_ref"),
            (PartitionRecordRefV1)Require(values, "frame_ref"),
            (Vec3Int64V1)Require(values, "position"),
            (QuaternionQ30V1)Require(values, "orientation"),
            (Vec3Int64V1)Require(values, "linear_velocity"),
            (Vec3Int64V1)Require(values, "angular_rate_urad_s"),
            (PartitionRecordRefV1)Require(values, "shape_ref"),
            OptionalRef(values, "containment_ref"),
            new StableToken((string)Require(values, "presence_mode")));

    private static object Require(IReadOnlyDictionary<string, object?> values, string field)
        => values.TryGetValue(field, out var value) && value is not null
            ? value
            : throw new InvalidDataException($"physical-built.snapshot-payload.required:{PartitionId}:{field}");

    private static PartitionRecordRefV1? OptionalRef(IReadOnlyDictionary<string, object?> values, string field)
        => values.TryGetValue(field, out var value) && value is not null
            ? (PartitionRecordRefV1)value
            : null;
}

public static class PhysicalBuiltDomainSnapshotProviderV1
{
    public static IDomainPartitionSnapshotSectionProviderV1 CreatePresence()
        => new DomainPartitionSnapshotSectionProviderV1<PhysicalPresencePayloadV1>(
            PhysicalPresencePayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            static values => PhysicalPresencePayloadV1.FromStandardPayload(values),
            static payload => payload.CanonicalDigest(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
}
