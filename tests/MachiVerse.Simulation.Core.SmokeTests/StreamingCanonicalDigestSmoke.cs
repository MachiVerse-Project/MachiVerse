using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

internal static class StreamingCanonicalDigestSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var materializedHash = HashSuite.DomainHash("mv.smoke.streaming-digest.v1", WriteFixture);
        var streamingHash = HashSuite.DomainHashStreaming("mv.smoke.streaming-digest.v1", WriteFixture);
        Require(CryptographicOperations.FixedTimeEquals(materializedHash, streamingHash),
            "Streaming MV-DCBOR domain hashing must be byte-for-byte equivalent to the materialized path.");

        var identity = StandardDomainPartitionRegistry.Get("resident.identity_lifecycle");
        var records = new[]
        {
            new DomainRecordEnvelopeV1<byte[]>(
                OpaqueId128.Parse("00000000000000000000000000000001"),
                identity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                detailLevel: DetailLevelV1.D0Entity,
                lineageRef: null,
                payload: new byte[] { 1, 2, 3 }),
            new DomainRecordEnvelopeV1<byte[]>(
                OpaqueId128.Parse("00000000000000000000000000000002"),
                identity.RecordSchema,
                revision: 2,
                createdStep: 0,
                retiredStep: 0,
                detailLevel: DetailLevelV1.D1LocalAggregate,
                lineageRef: OpaqueId128.Parse("00000000000000000000000000000003"),
                payload: new byte[] { 4, 5, 6, 7 }),
        };
        var partition = new DomainPartitionStateV1<byte[]>(identity, records);
        var materializedHeader = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 7,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            static payload => HashSuite.Hash256(payload));
        var streamingHeader = PartitionStateHeaderStreamingV1.CreateCanonical(
            identity,
            revision: 7,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            itemCount: partition.ItemCount,
            partition.RecordsCanonical,
            static payload => HashSuite.Hash256(payload));

        Require(materializedHeader.PartitionId == streamingHeader.PartitionId &&
                materializedHeader.OwnerDomain == streamingHeader.OwnerDomain &&
                materializedHeader.Schema == streamingHeader.Schema &&
                materializedHeader.Revision == streamingHeader.Revision &&
                materializedHeader.BasisStep == streamingHeader.BasisStep &&
                materializedHeader.DetailLevel == streamingHeader.DetailLevel &&
                materializedHeader.ItemCount == streamingHeader.ItemCount &&
                CryptographicOperations.FixedTimeEquals(
                    materializedHeader.CanonicalDigest,
                    streamingHeader.CanonicalDigest),
            "Streaming partition header digest must exactly match the existing materialized canonical digest.");
    }

    private static void WriteFixture(MvDcborWriter writer)
    {
        writer.WriteMapStart(6);
        writer.WriteUnsigned(0); writer.WriteUnsigned(ulong.MaxValue);
        writer.WriteUnsigned(1); writer.WriteInt64(-123_456_789);
        writer.WriteUnsigned(2); writer.WriteBytes(new byte[] { 0, 1, 2, 3, 255 });
        writer.WriteUnsigned(3); writer.WriteAsciiText("streaming.fixture");
        writer.WriteUnsigned(4);
        writer.WriteArrayStart(2);
        writer.WriteBoolean(false);
        writer.WriteBoolean(true);
        writer.WriteUnsigned(5);
        var nested = new MvDcborWriter();
        nested.WriteArrayStart(1);
        nested.WriteUnsigned(42);
        writer.WriteCanonicalValue(nested.ToArray());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
