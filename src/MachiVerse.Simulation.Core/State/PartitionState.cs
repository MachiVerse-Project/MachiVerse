using System.Buffers.Binary;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.State;

public readonly record struct Hash256 : IComparable<Hash256>
{
    private readonly ulong _a;
    private readonly ulong _b;
    private readonly ulong _c;
    private readonly ulong _d;

    public Hash256(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 32) throw new ArgumentException("Hash256 requires exactly 32 bytes.", nameof(bytes));
        _a = BinaryPrimitives.ReadUInt64BigEndian(bytes[0..8]);
        _b = BinaryPrimitives.ReadUInt64BigEndian(bytes[8..16]);
        _c = BinaryPrimitives.ReadUInt64BigEndian(bytes[16..24]);
        _d = BinaryPrimitives.ReadUInt64BigEndian(bytes[24..32]);
    }

    public static Hash256 Zero => new(new byte[32]);

    public byte[] ToBytes()
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(0, 8), _a);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8, 8), _b);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(16, 8), _c);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(24, 8), _d);
        return bytes;
    }

    public int CompareTo(Hash256 other)
    {
        var result = _a.CompareTo(other._a);
        if (result != 0) return result;
        result = _b.CompareTo(other._b);
        if (result != 0) return result;
        result = _c.CompareTo(other._c);
        return result != 0 ? result : _d.CompareTo(other._d);
    }
}

public enum DetailLevelV1 : byte
{
    D0Entity = 0,
    D1LocalAggregate = 1,
    D2RegionalAggregate = 2,
    D3BoundarySummary = 3,
}

public sealed record PartitionStateHeaderV1(
    StableToken PartitionId,
    StableToken OwnerDomain,
    SchemaRefV1 Schema,
    ulong Revision,
    ulong BasisStep,
    DetailLevelV1 DetailLevel,
    ulong ItemCount,
    Hash256 CanonicalDigest);

public sealed class DomainRecordEnvelopeV1
{
    private readonly byte[] _payload;

    public DomainRecordEnvelopeV1(
        OpaqueId128 recordId,
        SchemaRefV1 recordSchema,
        ulong revision,
        ulong createdStep,
        ulong? retiredStep,
        DetailLevelV1 detailLevel,
        OpaqueId128? lineageRef,
        ReadOnlySpan<byte> payload)
    {
        if (recordId.IsZero) throw new ArgumentException("PartitionRecordId ZERO is invalid.", nameof(recordId));
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision), "Record revision starts at 1.");
        if (retiredStep.HasValue && retiredStep.Value < createdStep)
            throw new ArgumentOutOfRangeException(nameof(retiredStep), "retired_step cannot precede created_step.");
        if (lineageRef.HasValue && lineageRef.Value.IsZero)
            throw new ArgumentException("Lineage PartitionRecordId ZERO is invalid when present.", nameof(lineageRef));

        RecordId = recordId;
        RecordSchema = recordSchema;
        Revision = revision;
        CreatedStep = createdStep;
        RetiredStep = retiredStep;
        DetailLevel = detailLevel;
        LineageRef = lineageRef;
        _payload = payload.ToArray();
    }

    public OpaqueId128 RecordId { get; }
    public SchemaRefV1 RecordSchema { get; }
    public ulong Revision { get; }
    public ulong CreatedStep { get; }
    public ulong? RetiredStep { get; }
    public DetailLevelV1 DetailLevel { get; }
    public OpaqueId128? LineageRef { get; }
    public ReadOnlyMemory<byte> Payload => _payload;
}

public sealed class DomainPartitionStateV1
{
    private readonly IReadOnlyList<DomainRecordEnvelopeV1> _records;
    private readonly Dictionary<OpaqueId128, DomainRecordEnvelopeV1> _byId;

    private DomainPartitionStateV1(
        DomainPartitionRegistrationV1 registration,
        PartitionStateHeaderV1 header,
        IReadOnlyList<DomainRecordEnvelopeV1> records)
    {
        Registration = registration;
        Header = header;
        _records = records;
        _byId = records.ToDictionary(static record => record.RecordId);
    }

    public DomainPartitionRegistrationV1 Registration { get; }
    public PartitionStateHeaderV1 Header { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1> Records => _records;

    public bool TryGetRecord(OpaqueId128 recordId, out DomainRecordEnvelopeV1? record)
        => _byId.TryGetValue(recordId, out record);

    public static DomainPartitionStateV1 CreateOwned(
        DomainPartitionRegistrationV1 registration,
        StableToken actorDomain,
        PartitionStateHeaderV1 header,
        IEnumerable<DomainRecordEnvelopeV1> records)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(records);
        if (actorDomain != registration.OwnerDomain)
            throw new InvalidOperationException("state.partition.foreign-owner-mutation-rejected");
        return CreateValidated(registration, header, records);
    }

    public static DomainPartitionStateV1 RestoreValidated(
        DomainPartitionRegistrationV1 registration,
        PartitionStateHeaderV1 header,
        IEnumerable<DomainRecordEnvelopeV1> records)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(records);
        return CreateValidated(registration, header, records);
    }

    private static DomainPartitionStateV1 CreateValidated(
        DomainPartitionRegistrationV1 registration,
        PartitionStateHeaderV1 header,
        IEnumerable<DomainRecordEnvelopeV1> records)
    {
        if (header.PartitionId != registration.PartitionId || header.OwnerDomain != registration.OwnerDomain)
            throw new InvalidDataException("state.partition.header-owner-or-id-mismatch");
        if (header.Schema != registration.PartitionSchema)
            throw new InvalidDataException("state.partition.header-schema-mismatch");

        var ordered = records.OrderBy(static record => record.RecordId).ToArray();
        if (ordered.Select(static record => record.RecordId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("state.partition.duplicate-record-id");
        if (header.ItemCount != (ulong)ordered.Length)
            throw new InvalidDataException("state.partition.item-count-mismatch");

        foreach (var record in ordered)
        {
            if (record.RecordSchema != registration.RecordSchema)
                throw new InvalidDataException("state.partition.record-schema-mismatch");
            if (record.CreatedStep > header.BasisStep)
                throw new InvalidDataException("state.partition.record-created-after-basis-step");
            if (record.RetiredStep.HasValue && record.RetiredStep.Value > header.BasisStep)
                throw new InvalidDataException("state.partition.record-retired-after-basis-step");
        }

        return new DomainPartitionStateV1(registration, header, Array.AsReadOnly(ordered));
    }
}

public sealed class OrderedPartitionDirectoryV1
{
    private readonly IReadOnlyList<DomainPartitionStateV1> _partitions;
    private readonly Dictionary<string, DomainPartitionStateV1> _byId;

    public OrderedPartitionDirectoryV1(IEnumerable<DomainPartitionStateV1> partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        var ordered = partitions.OrderBy(static partition => partition.Header.PartitionId.Value, StringComparer.Ordinal).ToArray();
        if (ordered.Select(static partition => partition.Header.PartitionId.Value).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidDataException("state.world.duplicate-partition-id");
        _partitions = Array.AsReadOnly(ordered);
        _byId = ordered.ToDictionary(static partition => partition.Header.PartitionId.Value, StringComparer.Ordinal);
    }

    public IReadOnlyList<DomainPartitionStateV1> Partitions => _partitions;

    public DomainPartitionStateV1 GetRequired(StableToken partitionId)
        => _byId.TryGetValue(partitionId.Value, out var partition)
            ? partition
            : throw new KeyNotFoundException($"WorldState missing partition '{partitionId.Value}'.");

    public void ValidateStandardCoverage(DomainPartitionRegistryV1 registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (_partitions.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("state.world.standard-partition-count-mismatch");
        foreach (var registration in registry.Entries)
        {
            var partition = GetRequired(registration.PartitionId);
            if (partition.Registration != registration)
                throw new InvalidDataException("state.world.partition-registration-mismatch");
        }
    }
}
