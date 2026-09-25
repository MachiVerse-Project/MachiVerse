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

internal sealed class CanonicalRecordChunkV1
{
    public CanonicalRecordChunkV1(ulong recordCount, byte[] encodedRecords)
    {
        if (recordCount == 0) throw new ArgumentOutOfRangeException(nameof(recordCount));
        ArgumentNullException.ThrowIfNull(encodedRecords);
        if (encodedRecords.Length == 0)
            throw new ArgumentException("Canonical record chunk bytes cannot be empty.", nameof(encodedRecords));

        RecordCount = recordCount;
        EncodedRecords = encodedRecords;
    }

    public ulong RecordCount { get; }
    public byte[] EncodedRecords { get; }
}

public sealed class PartitionStateHeaderV1
{
    public PartitionStateHeaderV1(
        DomainPartitionIdentityV1 identity,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        ulong itemCount,
        byte[] canonicalDigest,
        PartitionCanonicalDigestAlgorithmV1 digestAlgorithm = PartitionCanonicalDigestAlgorithmV1.LegacyFlatV1)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision), "Partition revision starts at 1.");
        if (!Enum.IsDefined(detailLevel)) throw new ArgumentOutOfRangeException(nameof(detailLevel));
        if (!Enum.IsDefined(digestAlgorithm)) throw new ArgumentOutOfRangeException(nameof(digestAlgorithm));
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
        DigestAlgorithm = digestAlgorithm;
    }

    public StableToken PartitionId { get; }
    public StableToken OwnerDomain { get; }
    public SchemaRefV1 Schema { get; }
    public ulong Revision { get; }
    public ulong BasisStep { get; }
    public DetailLevelV1 DetailLevel { get; }
    public ulong ItemCount { get; }
    public byte[] CanonicalDigest { get; }
    public PartitionCanonicalDigestAlgorithmV1 DigestAlgorithm { get; }

    public static PartitionStateHeaderV1 CreateCanonical<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        Func<TPayload, byte[]> canonicalPayloadDigest)
        => CreateCanonicalCore(
            partition,
            revision,
            basisStep,
            detailLevel,
            canonicalPayloadDigest,
            canonicalRecordEncoding: null);

    internal static PartitionStateHeaderV1 CreateCanonicalCached<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        Func<TPayload, byte[]> canonicalPayloadDigest,
        Func<DomainRecordEnvelopeV1<TPayload>, byte[]> canonicalRecordEncoding)
    {
        ArgumentNullException.ThrowIfNull(canonicalRecordEncoding);
        return CreateCanonicalCore(
            partition,
            revision,
            basisStep,
            detailLevel,
            canonicalPayloadDigest,
            canonicalRecordEncoding);
    }

    internal static PartitionStateHeaderV1 CreateCanonicalPrevalidatedChunks(
        DomainPartitionIdentityV1 identity,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        ulong itemCount,
        IReadOnlyList<CanonicalRecordChunkV1> canonicalRecordChunks)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(canonicalRecordChunks);
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(detailLevel)) throw new ArgumentOutOfRangeException(nameof(detailLevel));
        if (itemCount == 0 && canonicalRecordChunks.Count != 0)
            throw new InvalidDataException("domain.record-chunk-count-mismatch");
        if (itemCount != 0 && canonicalRecordChunks.Count == 0)
            throw new InvalidDataException("domain.record-chunk-count-mismatch");

        ulong actualCount = 0;
        using var session = HashSuite.BeginDomainHashStreaming("mv.state-diagnostic.v1");
        var writer = session.Writer;
        writer.WriteMapStart(8);
        writer.WriteUnsigned(0); writer.WriteAsciiText(identity.PartitionId.Value);
        writer.WriteUnsigned(1); writer.WriteAsciiText(identity.OwnerDomain.Value);
        writer.WriteUnsigned(2); writer.WriteAsciiText(identity.PartitionSchema.SchemaId.Value);
        writer.WriteUnsigned(3); writer.WriteUnsigned(revision);
        writer.WriteUnsigned(4); writer.WriteUnsigned(basisStep);
        writer.WriteUnsigned(5); writer.WriteUnsigned((byte)detailLevel);
        writer.WriteUnsigned(6); writer.WriteUnsigned(itemCount);
        writer.WriteUnsigned(7);
        writer.WriteArrayStart(itemCount);

        foreach (var chunk in canonicalRecordChunks)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            actualCount = checked(actualCount + chunk.RecordCount);
            if (actualCount > itemCount)
                throw new InvalidDataException("domain.record-chunk-count-mismatch");
            session.AppendCanonicalBytes(chunk.EncodedRecords);
        }

        if (actualCount != itemCount)
            throw new InvalidDataException("domain.record-chunk-count-mismatch");
        var digest = session.Complete();

        return new PartitionStateHeaderV1(
            identity,
            revision,
            basisStep,
            detailLevel,
            itemCount,
            digest);
    }

    private static PartitionStateHeaderV1 CreateCanonicalCore<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        Func<TPayload, byte[]> canonicalPayloadDigest,
        Func<DomainRecordEnvelopeV1<TPayload>, byte[]>? canonicalRecordEncoding)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(canonicalPayloadDigest);
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(detailLevel)) throw new ArgumentOutOfRangeException(nameof(detailLevel));

        var digest = HashSuite.DomainHashStreaming("mv.state-diagnostic.v1", writer =>
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
                if (record.CreatedStep > basisStep)
                    throw new InvalidDataException("domain.record-created-after-partition-basis");
                if (record.RetiredStep is { } retiredAfterBasis && retiredAfterBasis > basisStep)
                    throw new InvalidDataException("domain.record-retired-after-partition-basis");

                if (canonicalRecordEncoding is not null)
                {
                    var encoded = canonicalRecordEncoding(record)
                        ?? throw new InvalidDataException("domain.record-canonical-encoding-null");
                    if (encoded.Length == 0)
                        throw new InvalidDataException("domain.record-canonical-encoding-empty");
                    writer.WriteCanonicalValue(encoded);
                    continue;
                }

                var payloadDigest = canonicalPayloadDigest(record.Payload)
                    ?? throw new InvalidDataException("domain.payload-digest-null");
                if (payloadDigest.Length != 32)
                    throw new InvalidDataException("domain.payload-digest-invalid-length");
                WriteCanonicalRecord(writer, record, payloadDigest);
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

    internal static byte[] EncodeCanonicalRecord<TPayload>(
        DomainRecordEnvelopeV1<TPayload> record,
        byte[] payloadDigest)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(payloadDigest);
        if (payloadDigest.Length != 32)
            throw new InvalidDataException("domain.payload-digest-invalid-length");

        var writer = new MvDcborWriter();
        WriteCanonicalRecord(writer, record, payloadDigest);
        return writer.ToArray();
    }

    private static void WriteCanonicalRecord<TPayload>(
        MvDcborWriter writer,
        DomainRecordEnvelopeV1<TPayload> record,
        ReadOnlySpan<byte> payloadDigest)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);
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

public enum StateDiagnosticAlgorithmV1 : byte
{
    LegacyFlatV1 = 0,
    HierarchyRootV1 = 1,
}

public sealed class StateDiagnosticV1
{
    public StateDiagnosticV1(
        byte[] stateDigest,
        IEnumerable<KeyValuePair<string, byte[]>> partitionDigests,
        byte[] schemaRegistryDigest,
        byte[] configDigest,
        StateDiagnosticAlgorithmV1 algorithm = StateDiagnosticAlgorithmV1.LegacyFlatV1,
        StateDiagnosticRootV1? hierarchyRoot = null)
    {
        RequireHash(stateDigest, nameof(stateDigest));
        RequireHash(schemaRegistryDigest, nameof(schemaRegistryDigest));
        RequireHash(configDigest, nameof(configDigest));
        if (!Enum.IsDefined(algorithm)) throw new ArgumentOutOfRangeException(nameof(algorithm));
        ArgumentNullException.ThrowIfNull(partitionDigests);

        var ordered = partitionDigests
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .Select(static item => new KeyValuePair<string, byte[]>(item.Key, item.Value.ToArray()))
            .ToArray();
        if (ordered.Select(static item => item.Key).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidDataException("world-state.duplicate-partition-diagnostic");
        foreach (var item in ordered) RequireHash(item.Value, item.Key);

        if (algorithm == StateDiagnosticAlgorithmV1.LegacyFlatV1 && hierarchyRoot is not null)
            throw new InvalidDataException("world-state.legacy-diagnostic-hierarchy-root-present");
        if (algorithm == StateDiagnosticAlgorithmV1.HierarchyRootV1)
        {
            if (hierarchyRoot is null)
                throw new InvalidDataException("world-state.hierarchy-diagnostic-root-missing");
            if (!hierarchyRoot.Hash.AsSpan().SequenceEqual(stateDigest))
                throw new InvalidDataException("world-state.hierarchy-diagnostic-root-drift");
        }

        StateDigest = stateDigest.ToArray();
        PartitionDigests = Array.AsReadOnly(ordered);
        SchemaRegistryDigest = schemaRegistryDigest.ToArray();
        ConfigDigest = configDigest.ToArray();
        Algorithm = algorithm;
        HierarchyRoot = hierarchyRoot;
    }

    public byte[] StateDigest { get; }
    public IReadOnlyList<KeyValuePair<string, byte[]>> PartitionDigests { get; }
    public byte[] SchemaRegistryDigest { get; }
    public byte[] ConfigDigest { get; }
    public StateDiagnosticAlgorithmV1 Algorithm { get; }
    public StateDiagnosticRootV1? HierarchyRoot { get; }

    private static void RequireHash(byte[] value, string field)
    {
        ArgumentNullException.ThrowIfNull(value, field);
        if (value.Length != 32) throw new InvalidDataException($"world-state.invalid-diagnostic-hash:{field}");
    }
}

public sealed class WorldStateV1
{
    private static readonly byte[] CachedSchemaRegistryDigest = ComputeSchemaRegistryDigestCore();

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
        if (string.Equals(schemaId, "core.domain-registry-state", StringComparison.Ordinal))
            return MachiVerse.Simulation.Core.Runtime.StandardDomainRegistryAuthorityV1.Generation1SubstateRef();

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
        var partitionEntries = partitions.CanonicalEntries.ToArray();
        var partitionDigests = partitionEntries
            .Select(static item => new KeyValuePair<string, byte[]>(
                item.Header.PartitionId.Value,
                item.Header.CanonicalDigest.ToArray()))
            .ToArray();
        var schemaRegistryDigest = ComputeSchemaRegistryDigest();

        if (partitionEntries.All(static item =>
                item.Header.DigestAlgorithm == PartitionCanonicalDigestAlgorithmV1.LegacyFlatV1))
        {
            var legacyStateDigest = HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
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

            return new StateDiagnosticV1(
                legacyStateDigest,
                partitionDigests,
                schemaRegistryDigest,
                configDigest);
        }

        var hierarchyRoot = ComputeHierarchyDiagnostic(
            header,
            partitionEntries,
            scheduler,
            operation,
            detail,
            domainRegistry,
            schemaRegistryDigest,
            configDigest);
        return new StateDiagnosticV1(
            hierarchyRoot.Hash,
            partitionDigests,
            schemaRegistryDigest,
            configDigest,
            StateDiagnosticAlgorithmV1.HierarchyRootV1,
            hierarchyRoot);
    }

    private static StateDiagnosticRootV1 ComputeHierarchyDiagnostic(
        WorldStateHeaderV1 header,
        IReadOnlyList<PartitionStateRefV1> partitions,
        WorldSubstateRefV1 scheduler,
        WorldSubstateRefV1 operation,
        WorldSubstateRefV1 detail,
        WorldSubstateRefV1 domainRegistry,
        byte[] schemaRegistryDigest,
        byte[] configDigest)
    {
        const uint diagnosticPartitionVersion = 2;
        var domains = new List<DomainDiagnosticHashV1>();

        var coreDomain = new StableToken("core");
        var coreWriter = new MvDcborWriter();
        coreWriter.WriteMapStart(10);
        coreWriter.WriteUnsigned(0); coreWriter.WriteBytes(header.WorldSeedDigest);
        coreWriter.WriteUnsigned(1); coreWriter.WriteUnsigned(header.ConfigGeneration);
        coreWriter.WriteUnsigned(2); coreWriter.WriteUnsigned(header.MasterGeneration);
        coreWriter.WriteUnsigned(3); coreWriter.WriteUnsigned(header.RateGeneration);
        coreWriter.WriteUnsigned(4); coreWriter.WriteBytes(configDigest);
        coreWriter.WriteUnsigned(5); coreWriter.WriteBytes(schemaRegistryDigest);
        coreWriter.WriteUnsigned(6); coreWriter.WriteBytes(scheduler.CanonicalDigest);
        coreWriter.WriteUnsigned(7); coreWriter.WriteBytes(operation.CanonicalDigest);
        coreWriter.WriteUnsigned(8); coreWriter.WriteBytes(detail.CanonicalDigest);
        coreWriter.WriteUnsigned(9); coreWriter.WriteBytes(domainRegistry.CanonicalDigest);
        var coreSlice = StateDiagnosticHierarchyV1.CreateSliceHash(
            header.WorldId,
            header.Step,
            coreDomain,
            diagnosticPartitionVersion,
            new StableToken("core.state"),
            coreWriter.ToArray());
        domains.Add(StateDiagnosticHierarchyV1.CreateDomainHash(
            header.WorldId,
            header.Step,
            coreDomain,
            diagnosticPartitionVersion,
            [coreSlice]));

        foreach (var group in partitions.GroupBy(static item => item.Header.OwnerDomain))
        {
            var domainToken = group.Key;
            var slices = group
                .Select(partition =>
                {
                    var partitionHeader = partition.Header;
                    var writer = new MvDcborWriter();
                    writer.WriteMapStart(9);
                    writer.WriteUnsigned(0); writer.WriteUnsigned((byte)partitionHeader.DigestAlgorithm);
                    writer.WriteUnsigned(1); writer.WriteAsciiText(partitionHeader.Schema.SchemaId.Value);
                    writer.WriteUnsigned(2); writer.WriteUnsigned(partitionHeader.Schema.Version.Major);
                    writer.WriteUnsigned(3); writer.WriteUnsigned(partitionHeader.Schema.Version.Minor);
                    writer.WriteUnsigned(4); writer.WriteUnsigned(partitionHeader.Revision);
                    writer.WriteUnsigned(5); writer.WriteUnsigned(partitionHeader.BasisStep);
                    writer.WriteUnsigned(6); writer.WriteUnsigned((byte)partitionHeader.DetailLevel);
                    writer.WriteUnsigned(7); writer.WriteUnsigned(partitionHeader.ItemCount);
                    writer.WriteUnsigned(8); writer.WriteBytes(partitionHeader.CanonicalDigest);
                    return StateDiagnosticHierarchyV1.CreateSliceHash(
                        header.WorldId,
                        header.Step,
                        domainToken,
                        diagnosticPartitionVersion,
                        partitionHeader.PartitionId,
                        writer.ToArray());
                })
                .ToArray();
            domains.Add(StateDiagnosticHierarchyV1.CreateDomainHash(
                header.WorldId,
                header.Step,
                domainToken,
                diagnosticPartitionVersion,
                slices));
        }

        return StateDiagnosticHierarchyV1.CreateRootHash(
            header.WorldId,
            header.Step,
            domains);
    }

    private static byte[] ComputeSchemaRegistryDigest()
        => CachedSchemaRegistryDigest;

    private static byte[] ComputeSchemaRegistryDigestCore()
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
