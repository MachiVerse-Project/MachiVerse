using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ResidentIdentityLifecyclePayloadV1(
    OpaqueId128 ResidentId,
    StableToken Lifecycle,
    ulong? BirthStep,
    ulong? DeathStep,
    IReadOnlyList<PartitionRecordRefV1> ParentRefs,
    uint LineageGeneration,
    StableToken ProfileToken)
{
    public byte[] CanonicalDigest()
        => StandardDomainPayloadCanonicalDigestV1.Compute(
            "resident.identity_lifecycle",
            ToStandardPayload());

    internal IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["resident_id"] = ResidentId,
            ["lifecycle"] = Lifecycle.Value,
            ["parent_refs"] = ParentRefs,
            ["lineage_generation"] = LineageGeneration,
            ["profile_token"] = ProfileToken.Value,
        };
        if (BirthStep is { } birthStep) payload["birth_step"] = birthStep;
        if (DeathStep is { } deathStep) payload["death_step"] = deathStep;
        return payload;
    }
}

public sealed class Qa04ResidentIdentityMaterializationV1
{
    internal Qa04ResidentIdentityMaterializationV1(
        DomainPartitionStateV1<Qa04ResidentIdentityLifecyclePayloadV1> partition,
        PartitionStateHeaderV1 partitionHeader,
        WorldStateV1 worldState,
        ulong materializedRecordCount,
        ulong d0Count,
        ulong d1Count,
        ulong d2Count,
        ulong d3Count)
    {
        Partition = partition;
        PartitionHeader = partitionHeader;
        WorldState = worldState;
        MaterializedRecordCount = materializedRecordCount;
        D0Count = d0Count;
        D1Count = d1Count;
        D2Count = d2Count;
        D3Count = d3Count;
    }

    public DomainPartitionStateV1<Qa04ResidentIdentityLifecyclePayloadV1> Partition { get; }
    public PartitionStateHeaderV1 PartitionHeader { get; }
    public WorldStateV1 WorldState { get; }
    public ulong MaterializedRecordCount { get; }
    public ulong D0Count { get; }
    public ulong D1Count { get; }
    public ulong D2Count { get; }
    public ulong D3Count { get; }
    public bool CanonicalResidentPopulationComplete
        => MaterializedRecordCount == Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount;
}

/// <summary>
/// Materializes the QA-04 Resident identity/lifecycle partition as real authoritative records.
/// The returned WorldState keeps the other standard partitions canonically empty; therefore this
/// slice is not, by itself, a complete QA-04 reference world and must never enable release evidence.
/// </summary>
public static class Qa04ReferenceWorldMaterializerV1
{
    public const ulong CanonicalResidentCount = 1_000_000;
    public const uint InitialResidentLineageGeneration = 1;

    public static readonly StableToken InitialResidentLifecycle = new("alive");
    public static readonly StableToken ResidentProfileToken = new(Qa04ReferenceLoadV1.BenchmarkProfileId);

    private static readonly StableToken ResidentReferenceClass = new("resident.persistent-identity");
    private static readonly EmptyReferenceResolver ReferenceResolver = new();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        var expectedCount = Qa04ReferenceLoadV1.RecordClasses
            .Single(entry => entry.ClassToken == ResidentReferenceClass)
            .Count;
        if (expectedCount != CanonicalResidentCount)
            throw new InvalidDataException("qa04.materialization.resident-count-drift");

        var sample = CreatePayload(Qa04ReferenceLoadV1.Record(ResidentReferenceClass, 0).RecordId);
        ValidatePayload(sample);
        if (sample.Lifecycle != InitialResidentLifecycle ||
            sample.BirthStep is not null || sample.DeathStep is not null ||
            sample.ParentRefs.Count != 0 ||
            sample.LineageGeneration != InitialResidentLineageGeneration ||
            sample.ProfileToken != ResidentProfileToken)
        {
            throw new InvalidDataException("qa04.materialization.resident-genesis-contract-drift");
        }
    }

    public static Qa04ResidentIdentityMaterializationV1 MaterializeCanonicalResidentIdentityLifecycle()
        => MaterializeResidentIdentityLifecycle(CanonicalResidentCount);

    public static Qa04ResidentIdentityMaterializationV1 MaterializeResidentIdentityLifecycle(ulong recordCount)
    {
        ValidateCanonicalContract();
        if (recordCount is 0 or > CanonicalResidentCount)
            throw new ArgumentOutOfRangeException(nameof(recordCount));

        var identity = StandardDomainPartitionRegistry.Get("resident.identity_lifecycle");
        var partition = new DomainPartitionStateV1<Qa04ResidentIdentityLifecyclePayloadV1>(
            identity,
            CreateRecords(identity, recordCount));
        if (partition.ItemCount != recordCount)
            throw new InvalidDataException("qa04.materialization.resident-partition-count-mismatch");

        var partitionHeader = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D0Entity,
            static payload => payload.CanonicalDigest());
        var worldState = CreateResidentSliceWorldState(partitionHeader);
        var (d0, d1, d2, d3) = DetailCounts(recordCount);

        if (partitionHeader.ItemCount != recordCount ||
            worldState.Partitions.Get("resident.identity_lifecycle").Header.ItemCount != recordCount)
            throw new InvalidDataException("qa04.materialization.world-header-count-mismatch");

        return new Qa04ResidentIdentityMaterializationV1(
            partition,
            partitionHeader,
            worldState,
            recordCount,
            d0,
            d1,
            d2,
            d3);
    }

    public static DomainRecordEnvelopeV1<Qa04ResidentIdentityLifecyclePayloadV1> CreateResidentRecord(ulong ordinal)
    {
        var identity = StandardDomainPartitionRegistry.Get("resident.identity_lifecycle");
        var descriptor = Qa04ReferenceLoadV1.Record(ResidentReferenceClass, ordinal);
        var payload = CreatePayload(descriptor.RecordId);
        return new DomainRecordEnvelopeV1<Qa04ResidentIdentityLifecyclePayloadV1>(
            descriptor.RecordId,
            identity.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: descriptor.DetailLevel,
            lineageRef: null,
            payload);
    }

    private static IEnumerable<DomainRecordEnvelopeV1<Qa04ResidentIdentityLifecyclePayloadV1>> CreateRecords(
        DomainPartitionIdentityV1 identity,
        ulong count)
    {
        for (ulong ordinal = 0; ordinal < count; ordinal++)
        {
            var descriptor = Qa04ReferenceLoadV1.Record(ResidentReferenceClass, ordinal);
            var payload = CreatePayload(descriptor.RecordId);
            yield return new DomainRecordEnvelopeV1<Qa04ResidentIdentityLifecyclePayloadV1>(
                descriptor.RecordId,
                identity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                detailLevel: descriptor.DetailLevel,
                lineageRef: null,
                payload);
        }
    }

    private static Qa04ResidentIdentityLifecyclePayloadV1 CreatePayload(OpaqueId128 residentId)
    {
        var lifecycle = new ResidentLifecycleStateV1(
            residentId,
            ResidentLifecycleKindV1.Alive,
            BirthStep: null,
            DeathStep: null);
        lifecycle.Validate();
        return new Qa04ResidentIdentityLifecyclePayloadV1(
            residentId,
            InitialResidentLifecycle,
            BirthStep: null,
            DeathStep: null,
            Array.Empty<PartitionRecordRefV1>(),
            InitialResidentLineageGeneration,
            ResidentProfileToken);
    }

    private static void ValidatePayload(Qa04ResidentIdentityLifecyclePayloadV1 payload)
    {
        var validator = new StandardDomainPayloadCodecValidatorV1();
        validator.Validate("resident.identity_lifecycle", payload.ToStandardPayload(), ReferenceResolver);
    }

    private static WorldStateV1 CreateResidentSliceWorldState(PartitionStateHeaderV1 residentHeader)
    {
        var seedDigest = SHA256.HashData(Qa04ReferenceLoadV1.WorldSeed.ToBytes());
        var configDigest = HashSuite.DomainHash("mv.qa04-reference-world-config.v1", writer =>
        {
            writer.WriteMapStart(1);
            writer.WriteUnsigned(0); writer.WriteAsciiText(Qa04ReferenceLoadV1.BenchmarkProfileId);
        });
        var partitions = StandardDomainPartitionRegistry.Entries.Select(identity => new PartitionStateRefV1(
            identity.PartitionId.Value == "resident.identity_lifecycle"
                ? residentHeader
                : CreateCanonicalEmptyHeader(identity)));
        return new WorldStateV1(
            new WorldStateHeaderV1(
                Qa04ReferenceLoadV1.WorldId,
                step: 0,
                worldSeedDigest: seedDigest,
                configGeneration: 1,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            configDigest);
    }

    private static PartitionStateHeaderV1 CreateCanonicalEmptyHeader(DomainPartitionIdentityV1 identity)
    {
        var empty = new DomainPartitionStateV1<byte[]>(
            identity,
            Array.Empty<DomainRecordEnvelopeV1<byte[]>>());
        return PartitionStateHeaderV1.CreateCanonical(
            empty,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D0Entity,
            static _ => throw new InvalidOperationException("Empty partition payload digester must not be invoked."));
    }

    private static (ulong D0, ulong D1, ulong D2, ulong D3) DetailCounts(ulong count)
    {
        var d0 = Math.Min(count, 100_000UL);
        var d1 = Math.Min(count > 100_000UL ? count - 100_000UL : 0UL, 300_000UL);
        var d2 = Math.Min(count > 400_000UL ? count - 400_000UL : 0UL, 400_000UL);
        var d3 = Math.Min(count > 800_000UL ? count - 800_000UL : 0UL, 200_000UL);
        return (d0, d1, d2, d3);
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
}
