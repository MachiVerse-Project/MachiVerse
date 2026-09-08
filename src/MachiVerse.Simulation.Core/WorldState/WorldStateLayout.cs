using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

public sealed class WorldStateHeaderV1
{
    public WorldStateHeaderV1(
        OpaqueId128 worldId,
        ulong step,
        byte[] worldSeedDigest,
        ulong configGeneration,
        ulong masterGeneration,
        uint rateGeneration,
        byte[]? previousStateDigest = null)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        RequireHash(worldSeedDigest, nameof(worldSeedDigest));
        if (configGeneration == 0) throw new ArgumentOutOfRangeException(nameof(configGeneration), "ConfigGeneration starts at 1 for initialized WorldState.");
        if (masterGeneration == 0) throw new ArgumentOutOfRangeException(nameof(masterGeneration), "MasterGeneration starts at 1.");
        if (previousStateDigest is not null) RequireHash(previousStateDigest, nameof(previousStateDigest));

        Schema = new SchemaRefV1("core.world-state");
        WorldId = worldId;
        Step = step;
        WorldSeedDigest = worldSeedDigest.ToArray();
        ConfigGeneration = configGeneration;
        MasterGeneration = masterGeneration;
        RateGeneration = rateGeneration;
        PreviousStateDigest = previousStateDigest?.ToArray();
    }

    public SchemaRefV1 Schema { get; }
    public OpaqueId128 WorldId { get; }
    public ulong Step { get; }
    public byte[] WorldSeedDigest { get; }
    public ulong ConfigGeneration { get; }
    public ulong MasterGeneration { get; }
    public uint RateGeneration { get; }
    public byte[]? PreviousStateDigest { get; }

    private static void RequireHash(byte[] value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length != 32) throw new ArgumentException($"{name} must be exactly 32 bytes.", name);
    }
}

public sealed class PartitionStateHeaderV1
{
    public PartitionStateHeaderV1(
        DomainPartitionIdentityV1 identity,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        ulong itemCount,
        byte[] canonicalDigest)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision), "Partition revision starts at 1.");
        if (!Enum.IsDefined(detailLevel)) throw new ArgumentOutOfRangeException(nameof(detailLevel));
        ArgumentNullException.ThrowIfNull(canonicalDigest);
        if (canonicalDigest.Length != 32) throw new ArgumentException("canonical_digest must be exactly 32 bytes.", nameof(canonicalDigest));

        PartitionId = identity.PartitionId;
        OwnerDomain = identity.OwnerDomain;
        Schema = identity.PartitionSchema;
        Revision = revision;
        BasisStep = basisStep;
        DetailLevel = detailLevel;
        ItemCount = itemCount;
        CanonicalDigest = canonicalDigest.ToArray();
    }

    public StableToken PartitionId { get; }
    public StableToken OwnerDomain { get; }
    public SchemaRefV1 Schema { get; }
    public ulong Revision { get; }
    public ulong BasisStep { get; }
    public DetailLevelV1 DetailLevel { get; }
    public ulong ItemCount { get; }
    public byte[] CanonicalDigest { get; }

    public static PartitionStateHeaderV1 CreateCanonical<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        Func<TPayload, byte[]> canonicalPayloadDigest)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(canonicalPayloadDigest);
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(detailLevel)) throw new ArgumentOutOfRangeException(nameof(detailLevel));

        foreach (var record in partition.RecordsCanonical)
        {
            if (record.CreatedStep > basisStep)
                throw new InvalidDataException("domain.record-created-after-partition-basis");
            if (record.RetiredStep is { } retired && retired > basisStep)
                throw new InvalidDataException("domain.record-retired-after-partition-basis");
        }

        var digest = HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
        {
            writer.WriteMapStart(8);
            writer.WriteUnsigned(0); writer.WriteAsciiText(partition.Identity.PartitionId.Value);
            writer.WriteUnsigned(1); writer.WriteAsciiText(partition.Identity.OwnerDomain.Value);
            writer.WriteUnsigned(2); writer.WriteAsciiText(partition.Identity.PartitionSchema.SchemaId.Value);
            writer.WriteUnsigned(3); writer.WriteUnsigned(revision);
            writer.WriteUnsigned(4); writer.WriteUnsigned(basisStep);
            writer.WriteUnsigned(5); writer.WriteUnsigned((byte)detailLevel);
            writer.WriteUnsigned(6); writer.WriteUnsigned(partition.ItemCount);
            writer.WriteUnsigned(7);
            writer.WriteArrayStart(partition.ItemCount);
            foreach (var record in partition.RecordsCanonical)
            {
                var payloadDigest = canonicalPayloadDigest(record.Payload)
                    ?? throw new InvalidDataException("domain.payload-digest-null");
                if (payloadDigest.Length != 32)
                    throw new InvalidDataException("domain.payload-digest-invalid-length");

                writer.WriteMapStart(10);
                writer.WriteUnsigned(0); writer.WriteBytes(record.RecordId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteAsciiText(record.RecordSchema.SchemaId.Value);
                writer.WriteUnsigned(2); writer.WriteUnsigned(record.RecordSchema.Version.Major);
                writer.WriteUnsigned(3); writer.WriteUnsigned(record.RecordSchema.Version.Minor);
                writer.WriteUnsigned(4); writer.WriteUnsigned(record.Revision);
                writer.WriteUnsigned(5); writer.WriteUnsigned(record.CreatedStep);
                writer.WriteUnsigned(6);
                if (record.RetiredStep is { } retired)
                {
                    writer.WriteArrayStart(1);
                    writer.WriteUnsigned(retired);
                }
                else writer.WriteArrayStart(0);
                writer.WriteUnsigned(7); writer.WriteUnsigned((byte)record.DetailLevel);
                writer.WriteUnsigned(8);
                if (record.LineageRef is { } lineage)
                {
                    writer.WriteArrayStart(1);
                    writer.WriteBytes(lineage.ToBytes());
                }
                else writer.WriteArrayStart(0);
                writer.WriteUnsigned(9); writer.WriteBytes(payloadDigest);
            }
        });

        return new PartitionStateHeaderV1(
            partition.Identity,
            revision,
            basisStep,
            detailLevel,
            partition.ItemCount,
            digest);
    }
}

public sealed record PartitionStateRefV1(PartitionStateHeaderV1 Header);

public sealed class OrderedPartitionDirectoryV1
{
    private readonly SortedDictionary<string, PartitionStateRefV1> _entries = new(StringComparer.Ordinal);

    public OrderedPartitionDirectoryV1(IEnumerable<PartitionStateRefV1> partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        foreach (var partition in partitions)
        {
            ArgumentNullException.ThrowIfNull(partition);
            if (!_entries.TryAdd(partition.Header.PartitionId.Value, partition))
                throw new InvalidDataException("world-state.duplicate-partition-id");
        }
    }

    public int Count => _entries.Count;
    public IEnumerable<PartitionStateRefV1> CanonicalEntries => _entries.Values;

    public PartitionStateRefV1 Get(string partitionId)
        => _entries.TryGetValue(partitionId, out var value)
            ? value
            : throw new KeyNotFoundException($"Partition not present in WorldState: {partitionId}");

    public void ValidateStandardCompleteness()
    {
        if (_entries.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("world-state.standard-partition-count-mismatch");

        foreach (var standard in StandardDomainPartitionRegistry.Entries)
        {
            if (!_entries.TryGetValue(standard.PartitionId.Value, out var state))
                throw new InvalidDataException($"world-state.required-partition-missing:{standard.PartitionId.Value}");
            if (state.Header.OwnerDomain != standard.OwnerDomain || state.Header.Schema != standard.PartitionSchema)
                throw new InvalidDataException($"world-state.partition-identity-mismatch:{standard.PartitionId.Value}");
        }
    }
}

public sealed record WorldSubstateRefV1(
    SchemaRefV1 Schema,
    byte[] CanonicalDigest)
{
    public void Validate(string field)
    {
        ArgumentNullException.ThrowIfNull(CanonicalDigest);
        if (CanonicalDigest.Length != 32)
            throw new InvalidDataException($"world-state.invalid-substate-digest:{field}");
    }
}

public sealed class StateDiagnosticV1
{
    public StateDiagnosticV1(
        byte[] stateDigest,
        IEnumerable<KeyValuePair<string, byte[]>> partitionDigests,
        byte[] schemaRegistryDigest,
        byte[] configDigest)
    {
        RequireHash(stateDigest, nameof(stateDigest));
        RequireHash(schemaRegistryDigest, nameof(schemaRegistryDigest));
        RequireHash(configDigest, nameof(configDigest));
        ArgumentNullException.ThrowIfNull(partitionDigests);

        var ordered = partitionDigests
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .Select(static item => new KeyValuePair<string, byte[]>(item.Key, item.Value.ToArray()))
            .ToArray();
        if (ordered.Select(static item => item.Key).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidDataException("world-state.duplicate-partition-diagnostic");
        foreach (var item in ordered) RequireHash(item.Value, item.Key);

        StateDigest = stateDigest.ToArray();
        PartitionDigests = Array.AsReadOnly(ordered);
        SchemaRegistryDigest = schemaRegistryDigest.ToArray();
        ConfigDigest = configDigest.ToArray();
    }

    public byte[] StateDigest { get; }
    public IReadOnlyList<KeyValuePair<string, byte[]>> PartitionDigests { get; }
    public byte[] SchemaRegistryDigest { get; }
    public byte[] ConfigDigest { get; }

    private static void RequireHash(byte[] value, string field)
    {
        ArgumentNullException.ThrowIfNull(value, field);
        if (value.Length != 32) throw new InvalidDataException($"world-state.invalid-diagnostic-hash:{field}");
    }
}

public sealed class WorldStateV1
{
    public WorldStateV1(
        WorldStateHeaderV1 header,
        OrderedPartitionDirectoryV1 partitions,
        WorldSubstateRefV1 schedulerState,
        WorldSubstateRefV1 operationState,
        WorldSubstateRefV1 detailState,
        WorldSubstateRefV1 domainRegistryState,
        byte[] configDigest)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(partitions);
        ArgumentNullException.ThrowIfNull(schedulerState);
        ArgumentNullException.ThrowIfNull(operationState);
        ArgumentNullException.ThrowIfNull(detailState);
        ArgumentNullException.ThrowIfNull(domainRegistryState);
        if (configDigest is null || configDigest.Length != 32)
            throw new ArgumentException("configDigest must be 32 bytes.", nameof(configDigest));

        partitions.ValidateStandardCompleteness();
        schedulerState.Validate("scheduler");
        operationState.Validate("operation");
        detailState.Validate("detail");
        domainRegistryState.Validate("domain-registry");
        foreach (var partition in partitions.CanonicalEntries)
        {
            if (partition.Header.BasisStep > header.Step)
                throw new InvalidDataException($"world-state.partition-basis-ahead:{partition.Header.PartitionId.Value}");
        }

        Header = header;
        Partitions = partitions;
        SchedulerState = schedulerState;
        OperationState = operationState;
        DetailState = detailState;
        DomainRegistryState = domainRegistryState;
        Diagnostic = ComputeDiagnostic(header, partitions, schedulerState, operationState, detailState, domainRegistryState, configDigest);
    }

    public WorldStateHeaderV1 Header { get; }
    public OrderedPartitionDirectoryV1 Partitions { get; }
    public WorldSubstateRefV1 SchedulerState { get; }
    public WorldSubstateRefV1 OperationState { get; }
    public WorldSubstateRefV1 DetailState { get; }
    public WorldSubstateRefV1 DomainRegistryState { get; }
    public StateDiagnosticV1 Diagnostic { get; }

    public static WorldSubstateRefV1 EmptySubstate(string schemaId)
    {
        var schema = new SchemaRefV1(schemaId);
        var digest = HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
        {
            writer.WriteMapStart(2);
            writer.WriteUnsigned(0); writer.WriteAsciiText(schema.SchemaId.Value);
            writer.WriteUnsigned(1); writer.WriteArrayStart(0);
        });
        return new WorldSubstateRefV1(schema, digest);
    }

    private static StateDiagnosticV1 ComputeDiagnostic(
        WorldStateHeaderV1 header,
        OrderedPartitionDirectoryV1 partitions,
        WorldSubstateRefV1 scheduler,
        WorldSubstateRefV1 operation,
        WorldSubstateRefV1 detail,
        WorldSubstateRefV1 domainRegistry,
        byte[] configDigest)
    {
        var partitionDigests = partitions.CanonicalEntries
            .Select(static item => new KeyValuePair<string, byte[]>(
                item.Header.PartitionId.Value,
                item.Header.CanonicalDigest.ToArray()))
            .ToArray();
        var schemaRegistryDigest = ComputeSchemaRegistryDigest();
        var stateDigest = HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
        {
            writer.WriteMapStart(13);
            writer.WriteUnsigned(0); writer.WriteBytes(header.WorldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(header.Step);
            writer.WriteUnsigned(2); writer.WriteBytes(header.WorldSeedDigest);
            writer.WriteUnsigned(3); writer.WriteUnsigned(header.ConfigGeneration);
            writer.WriteUnsigned(4); writer.WriteUnsigned(header.MasterGeneration);
            writer.WriteUnsigned(5); writer.WriteUnsigned(header.RateGeneration);
            writer.WriteUnsigned(6); writer.WriteBytes(configDigest);
            writer.WriteUnsigned(7); writer.WriteBytes(schemaRegistryDigest);
            writer.WriteUnsigned(8); writer.WriteBytes(scheduler.CanonicalDigest);
            writer.WriteUnsigned(9); writer.WriteBytes(operation.CanonicalDigest);
            writer.WriteUnsigned(10); writer.WriteBytes(detail.CanonicalDigest);
            writer.WriteUnsigned(11); writer.WriteBytes(domainRegistry.CanonicalDigest);
            writer.WriteUnsigned(12);
            writer.WriteArrayStart((ulong)partitionDigests.Length);
            foreach (var partition in partitionDigests)
            {
                writer.WriteArrayStart(2);
                writer.WriteAsciiText(partition.Key);
                writer.WriteBytes(partition.Value);
            }
        });

        return new StateDiagnosticV1(stateDigest, partitionDigests, schemaRegistryDigest, configDigest);
    }

    private static byte[] ComputeSchemaRegistryDigest()
        => HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
        {
            writer.WriteArrayStart((ulong)StandardDomainPartitionRegistry.Entries.Count);
            foreach (var entry in StandardDomainPartitionRegistry.Entries)
            {
                writer.WriteMapStart(8);
                writer.WriteUnsigned(0); writer.WriteAsciiText(entry.PartitionId.Value);
                writer.WriteUnsigned(1); writer.WriteAsciiText(entry.OwnerDomain.Value);
                writer.WriteUnsigned(2); writer.WriteUnsigned(entry.OwnerDomainRank);
                writer.WriteUnsigned(3); writer.WriteAsciiText(entry.PartitionSchema.SchemaId.Value);
                writer.WriteUnsigned(4); writer.WriteAsciiText(entry.RecordSchema.SchemaId.Value);
                writer.WriteUnsigned(5); writer.WriteUnsigned((uint)entry.PrimaryKeyKind);
                writer.WriteUnsigned(6); writer.WriteUnsigned((uint)entry.PersistenceClass);
                writer.WriteUnsigned(7); writer.WriteUnsigned((uint)entry.CanonicalOrder);
            }
        });
}
