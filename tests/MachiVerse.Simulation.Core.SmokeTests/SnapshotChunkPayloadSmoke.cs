using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class SnapshotChunkPayloadSmoke
{
    internal static void Run()
    {
        var sections = BuildSections(largeFirstTwo: false);
        var chunks = SnapshotChunkPackerV1.PackStandard(sections);
        Require(chunks.Count == 1, "Small 103-section fixture should fit in one snapshot chunk payload.");
        Require(chunks[0].Fragments.Count == 103, "Packed chunk must retain all 103 section fragments.");

        var encoded = SnapshotChunkPayloadWireCodecV1.Encode(chunks[0]);
        var decoded = SnapshotChunkPayloadWireCodecV1.Decode(encoded);
        Require(decoded.Fragments.Count == 103, "SnapshotChunkPayloadV1 fragment count round-trip mismatch.");
        Require(SnapshotChunkPayloadWireCodecV1.Encode(decoded).SequenceEqual(encoded),
            "SnapshotChunkPayloadV1 deterministic re-encode mismatch.");
        Require(decoded.Fragments.Select(static fragment => fragment.SectionId).SequenceEqual(
                StandardSnapshotSectionSetV1.SectionIds),
            "SnapshotChunkPayloadV1 must preserve canonical section ordering.");

        var reorderedRejected = false;
        try
        {
            SnapshotChunkPayloadWireCodecV1.Encode(new SnapshotChunkFragmentPayloadV1(
                [decoded.Fragments[1], decoded.Fragments[0]]));
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot-fragment-invalid:section-order")
        {
            reorderedRejected = true;
        }
        Require(reorderedRejected, "Reordered snapshot chunk fragments must fail closed.");

        var gapRejected = false;
        try
        {
            SnapshotChunkPayloadWireCodecV1.Encode(new SnapshotChunkFragmentPayloadV1(
            [
                new SnapshotSectionFragmentMaterialV1("resident.identity_lifecycle", 0, 3, null, null, 1, [1]),
                new SnapshotSectionFragmentMaterialV1("resident.identity_lifecycle", 2, 3, null, null, 1, [2]),
            ]));
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot-fragment-invalid:fragment-order")
        {
            gapRejected = true;
        }
        Require(gapRejected, "Snapshot chunk fragment index gaps must fail closed.");

        var largeSections = BuildSections(largeFirstTwo: true);
        var split = SnapshotChunkPackerV1.PackStandard(largeSections);
        Require(split.Count >= 2, "32 MiB target must split two 17 MiB fragment fields across chunks.");
        foreach (var chunk in split)
        {
            var bytes = SnapshotChunkPayloadWireCodecV1.Encode(chunk);
            Require(bytes.Length <= CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes,
                "Packed snapshot chunk exceeded 64 MiB hard maximum.");
            _ = SnapshotChunkPayloadWireCodecV1.Decode(bytes);
        }
    }

    private static IReadOnlyList<CanonicalSnapshotSectionMaterialV1> BuildSections(bool largeFirstTwo)
    {
        var result = new List<CanonicalSnapshotSectionMaterialV1>(103);
        for (var i = 0; i < StandardSnapshotSectionSetV1.SectionIds.Count; i++)
        {
            var sectionId = StandardSnapshotSectionSetV1.SectionIds[i];
            var schema = StandardDomainPartitionRegistry.TryGet(sectionId, out var identity) && identity is not null
                ? identity.PartitionSchema
                : new SchemaRefV1("fixture.snapshot-core-section");
            var payloadLength = largeFirstTwo && i < 2 ? 17 * 1024 * 1024 : 1;
            var payload = new byte[payloadLength];
            if (payload.Length > 0) payload[0] = checked((byte)(i + 1));
            result.Add(new CanonicalSnapshotSectionMaterialV1(
                sectionId,
                schema,
                LogicalItemCount: 1,
                LogicalContentDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes("chunk-fixture:" + sectionId)),
                Fragments:
                [
                    new SnapshotSectionFragmentMaterialV1(
                        sectionId,
                        FragmentIndex: 0,
                        FragmentCount: 1,
                        FirstRecordId: null,
                        LastRecordId: null,
                        ItemCount: 1,
                        FragmentPayload: payload),
                ]));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
