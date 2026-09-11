using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class CanonicalSnapshotStreamingChunkSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var worldId = OpaqueId128.Parse("00000000000000000000000000000091");
        var configDigest = SHA256.HashData("snapshot-streaming-chunk-config"u8);
        var state = CreateState(worldId, configDigest);
        var materialized = BuildSections(state);
        var streaming = materialized
            .Select(CanonicalSnapshotStreamingSectionV1.FromMaterialized)
            .ToArray();

        var oldChunks = SnapshotChunkPackerV1.PackStandard(materialized, state);
        var newChunks = SnapshotChunkStreamingPackerV1.PackStandard(streaming, state).ToArray();

        Require(oldChunks.Count == newChunks.Length,
            "Streaming exact-103 packer must preserve the materialized chunk count.");
        for (var chunkIndex = 0; chunkIndex < oldChunks.Count; chunkIndex++)
        {
            var expected = oldChunks[chunkIndex];
            var actual = newChunks[chunkIndex];
            Require(expected.Fragments.Count == actual.Fragments.Count,
                "Streaming chunk must preserve the exact fragment count.");
            for (var fragmentIndex = 0; fragmentIndex < expected.Fragments.Count; fragmentIndex++)
                RequireSameFragment(expected.Fragments[fragmentIndex], actual.Fragments[fragmentIndex]);

            var expectedWire = SnapshotChunkPayloadWireCodecV1.Encode(expected);
            var actualWire = SnapshotChunkPayloadWireCodecV1.Encode(actual);
            Require(expectedWire.AsSpan().SequenceEqual(actualWire),
                "Streaming exact-103 chunk wire must be byte-for-byte identical to the materialized path.");
        }

        var logicalExpected = CanonicalSnapshotSectionValidationV1.ToLogicalSections(materialized, state);
        var logicalActual = CanonicalSnapshotStreamingSectionValidationV1.ToLogicalSections(streaming, state);
        Require(logicalExpected.Count == logicalActual.Count,
            "Streaming logical section metadata must preserve exact-103 cardinality.");
        for (var index = 0; index < logicalExpected.Count; index++)
            RequireSameLogicalSection(logicalExpected[index], logicalActual[index]);
    }

    private static List<CanonicalSnapshotSectionMaterialV1> BuildSections(WorldStateV1 state)
    {
        var result = new List<CanonicalSnapshotSectionMaterialV1>(103);
        foreach (var sectionId in StandardSnapshotSectionSetV1.SectionIds)
        {
            if (StandardDomainPartitionRegistry.TryGet(sectionId, out var identity) && identity is not null)
            {
                var header = state.Partitions.Get(sectionId).Header;
                result.Add(new CanonicalSnapshotSectionMaterialV1(
                    sectionId,
                    identity.PartitionSchema,
                    header.ItemCount,
                    header.CanonicalDigest.ToArray(),
                    [new SnapshotSectionFragmentMaterialV1(
                        sectionId,
                        0,
                        1,
                        null,
                        null,
                        header.ItemCount,
                        [0x01])]));
            }
            else
            {
                var digest = SHA256.HashData(Encoding.ASCII.GetBytes("streaming-fixture:" + sectionId));
                result.Add(new CanonicalSnapshotSectionMaterialV1(
                    sectionId,
                    new SchemaRefV1("fixture.snapshot-core-section"),
                    1,
                    digest,
                    [new SnapshotSectionFragmentMaterialV1(sectionId, 0, 1, null, null, 1, [0x01])]));
            }
        }
        return result;
    }

    private static WorldStateV1 CreateState(OpaqueId128 worldId, byte[] configDigest)
    {
        var partitions = StandardDomainPartitionRegistry.Entries.Select(identity => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                identity,
                revision: 1,
                basisStep: 7,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(Encoding.ASCII.GetBytes("streaming-partition:" + identity.PartitionId.Value)))));
        return new WorldStateV1(
            new WorldStateHeaderV1(
                worldId,
                step: 7,
                worldSeedDigest: SHA256.HashData("snapshot-streaming-seed"u8),
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

    private static void RequireSameFragment(
        SnapshotSectionFragmentMaterialV1 expected,
        SnapshotSectionFragmentMaterialV1 actual)
    {
        Require(string.Equals(expected.SectionId, actual.SectionId, StringComparison.Ordinal) &&
                expected.FragmentIndex == actual.FragmentIndex &&
                expected.FragmentCount == actual.FragmentCount &&
                expected.ItemCount == actual.ItemCount,
            "Streaming fragment metadata must match the materialized path.");
        Require(OptionalBytesEqual(expected.FirstRecordId, actual.FirstRecordId) &&
                OptionalBytesEqual(expected.LastRecordId, actual.LastRecordId) &&
                expected.FragmentPayload.AsSpan().SequenceEqual(actual.FragmentPayload),
            "Streaming fragment ranges and payload must match the materialized path.");
    }

    private static void RequireSameLogicalSection(
        LogicalSnapshotSection expected,
        LogicalSnapshotSection actual)
    {
        Require(string.Equals(expected.SectionId, actual.SectionId, StringComparison.Ordinal) &&
                string.Equals(expected.SchemaId, actual.SchemaId, StringComparison.Ordinal) &&
                expected.SchemaMajor == actual.SchemaMajor &&
                expected.SchemaMinor == actual.SchemaMinor &&
                expected.LogicalItemCount == actual.LogicalItemCount &&
                expected.Required == actual.Required &&
                expected.LogicalContentDigest.AsSpan().SequenceEqual(actual.LogicalContentDigest),
            "Streaming logical section metadata must match the materialized path.");
    }

    private static bool OptionalBytesEqual(byte[]? left, byte[]? right)
        => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
