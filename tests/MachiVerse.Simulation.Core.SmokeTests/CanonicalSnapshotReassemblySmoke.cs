using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class CanonicalSnapshotReassemblySmoke
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-snapshot-reassembly-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fixture = BuildFixture(OpaqueId128.Parse("00000000000000000000000000000091"));
            var paths = PersistenceLayout.Resolve(root, fixture.State.Header.WorldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await VerifyCrossChunkReassemblyAsync(paths, fixture);
            VerifyMissingVerifierRejected(fixture);
            await VerifySemanticTamperRejectedAsync(paths, fixture);
            await VerifyStoredTamperRejectedAsync(paths, fixture);
            await VerifyLogicalDigestTamperRejectedAsync(paths, fixture);
            await VerifyMissingCompressionDecoderRejectedAsync(paths, fixture);
            await VerifyPhysicalManifestDigestTamperRejectedAsync(paths, fixture);
            await VerifyManifestChunkHeaderMismatchRejectedAsync(paths, fixture);
            await VerifyManifestChunkSectionRangeMismatchRejectedAsync(paths, fixture);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task VerifyCrossChunkReassemblyAsync(WorldPersistencePaths paths, Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(paths, Id(0x92));
        var all = fixture.Materials.SelectMany(static section => section.Fragments).ToArray();
        Require(fixture.Materials[0].Fragments.Count == 2, "Fixture must split the first section.");

        var payload0 = new SnapshotChunkFragmentPayloadV1(new[] { all[0] });
        var header0 = await CanonicalSnapshotChunkFileV1.WriteUncompressedAsync(
            Path.Combine(physical.StagingChunksDirectory, "00000000.mvchunk"),
            payload0);
        var payload1 = new SnapshotChunkFragmentPayloadV1(Array.AsReadOnly(all[1..]));
        var header1 = await CanonicalSnapshotChunkFileV1.WriteUncompressedAsync(
            Path.Combine(physical.StagingChunksDirectory, "00000001.mvchunk"),
            payload1);
        var descriptors = new[]
        {
            Descriptor(0, header0, payload0),
            Descriptor(1, header1, payload1),
        };
        await WriteManifestAsync(physical, fixture, descriptors);

        await CanonicalSnapshotStagingValidatorV1.ValidateAsync(
            physical, fixture.Materials, fixture.Verifiers, fixture.State);
    }

    private static void VerifyMissingVerifierRejected(Fixture fixture)
    {
        var rejected = false;
        try { _ = new CanonicalSnapshotSemanticVerifierRegistryV1(fixture.VerifierEntries.Skip(1)); }
        catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.semantic-verifier-count-mismatch") { rejected = true; }
        Require(rejected, "Missing owner verifier must fail closed.");
    }

    private static async Task VerifySemanticTamperRejectedAsync(WorldPersistencePaths paths, Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(paths, Id(0x93));
        var tampered = fixture.Materials.Select((section, index) => index == 0
            ? section with
            {
                Fragments = section.Fragments.Select((fragment, fragmentIndex) => fragmentIndex == 0
                    ? fragment with { FragmentPayload = fragment.FragmentPayload.Concat(new byte[] { 0xff }).ToArray() }
                    : fragment).ToArray()
            }
            : section).ToArray();
        var descriptors = await CanonicalSnapshotPhysicalDrainV1.StageUncompressedAsync(physical, tampered, fixture.State);
        await WriteManifestAsync(physical, fixture, descriptors);
        await RequireRejectedAsync(
            () => CanonicalSnapshotStagingValidatorV1.ValidateAsync(physical, fixture.Materials, fixture.Verifiers, fixture.State),
            "persistence.snapshot.section-semantic-digest-mismatch:",
            "Schema-owner semantic tamper must be rejected.");
    }

    private static async Task VerifyStoredTamperRejectedAsync(WorldPersistencePaths paths, Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(paths, Id(0x94));
        var descriptors = await CanonicalSnapshotPhysicalDrainV1.StageUncompressedAsync(physical, fixture.Materials, fixture.State);
        await WriteManifestAsync(physical, fixture, descriptors);
        var path = Path.Combine(physical.StagingChunksDirectory, "00000000.mvchunk");
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = SnapshotChunkFile.HeaderLength;
            var original = stream.ReadByte();
            if (original < 0) throw new InvalidOperationException("Snapshot chunk is empty.");
            stream.Position = SnapshotChunkFile.HeaderLength;
            stream.WriteByte((byte)(original ^ 1));
            stream.Flush(flushToDisk: true);
        }
        await RequireRejectedAsync(
            () => CanonicalSnapshotStagingValidatorV1.ValidateAsync(physical, fixture.Materials, fixture.Verifiers, fixture.State),
            "persistence.snapshot.stored-digest-mismatch",
            "Stored byte tamper must fail before reassembly.");
    }

    private static async Task VerifyLogicalDigestTamperRejectedAsync(WorldPersistencePaths paths, Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(paths, Id(0x95));
        var chunk = SnapshotChunkPackerV1.PackStandard(fixture.Materials, fixture.State)[0];
        var encoded = SnapshotChunkPayloadWireCodecV1.Encode(chunk);
        var rawHash = SHA256.HashData(encoded);
        Require(!rawHash.SequenceEqual(SnapshotChunkLogicalPayloadDigestV1.Compute(chunk)),
            "Raw protobuf hash must differ from semantic chunk digest fixture.");
        var header = await SnapshotChunkFile.WriteAsync(
            Path.Combine(physical.StagingChunksDirectory, "00000000.mvchunk"),
            encoded, (ulong)encoded.Length, rawHash, SnapshotCompression.None);
        await WriteManifestAsync(physical, fixture, new[] { Descriptor(0, header, chunk) });
        await RequireRejectedAsync(
            () => CanonicalSnapshotStagingValidatorV1.ValidateAsync(physical, fixture.Materials, fixture.Verifiers, fixture.State),
            "persistence.snapshot.logical-payload-digest-mismatch",
            "Raw protobuf hash must not satisfy semantic chunk digest.");
    }

    private static async Task VerifyMissingCompressionDecoderRejectedAsync(WorldPersistencePaths paths, Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(paths, Id(0x96));
        var chunk = SnapshotChunkPackerV1.PackStandard(fixture.Materials, fixture.State)[0];
        var encoded = SnapshotChunkPayloadWireCodecV1.Encode(chunk);
        var header = await SnapshotChunkFile.WriteAsync(
            Path.Combine(physical.StagingChunksDirectory, "00000000.mvchunk"),
            encoded, checked((ulong)encoded.Length + 1), SnapshotChunkLogicalPayloadDigestV1.Compute(chunk), SnapshotCompression.Zstd);
        await WriteManifestAsync(physical, fixture, new[] { Descriptor(0, header, chunk) });
        await RequireRejectedAsync(
            () => CanonicalSnapshotStagingValidatorV1.ValidateAsync(physical, fixture.Materials, fixture.Verifiers, fixture.State),
            "persistence.snapshot.compression-codec-unavailable:zstd",
            "Missing Zstd decoder must fail closed.");
    }

    private static async Task VerifyPhysicalManifestDigestTamperRejectedAsync(WorldPersistencePaths paths, Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(paths, Id(0x97));
        var descriptors = await CanonicalSnapshotPhysicalDrainV1.StageUncompressedAsync(physical, fixture.Materials, fixture.State);
        await WriteManifestAsync(physical, fixture, descriptors);
        var bytes = await File.ReadAllBytesAsync(physical.StagingManifestPath);
        bytes[^1] ^= 0x01;
        await File.WriteAllBytesAsync(physical.StagingManifestPath, bytes);
        await RequireRejectedAsync(
            () => CanonicalSnapshotStagingValidatorV1.ValidateAsync(physical, fixture.Materials, fixture.Verifiers, fixture.State),
            "persistence.snapshot.physical-manifest-digest-mismatch",
            "Physical manifest self-digest tamper must be rejected before chunk reassembly.");
    }

    private static async Task VerifyManifestChunkHeaderMismatchRejectedAsync(WorldPersistencePaths paths, Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(paths, Id(0x98));
        var descriptors = (await CanonicalSnapshotPhysicalDrainV1.StageUncompressedAsync(physical, fixture.Materials, fixture.State)).ToArray();
        var alteredDigest = descriptors[0].StoredPayloadDigest.ToArray();
        alteredDigest[0] ^= 0x80;
        descriptors[0] = descriptors[0] with { StoredPayloadDigest = alteredDigest };
        await WriteManifestAsync(physical, fixture, descriptors);
        await RequireRejectedAsync(
            () => CanonicalSnapshotStagingValidatorV1.ValidateAsync(physical, fixture.Materials, fixture.Verifiers, fixture.State),
            "persistence.snapshot.manifest-chunk-header-mismatch:",
            "Manifest chunk descriptor must match the actual MVCHNK01 header.");
    }

    private static async Task VerifyManifestChunkSectionRangeMismatchRejectedAsync(WorldPersistencePaths paths, Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(paths, Id(0x99));
        var firstOnly = new SnapshotChunkFragmentPayloadV1(new[] { fixture.Materials[0].Fragments[0] });
        var header = await CanonicalSnapshotChunkFileV1.WriteUncompressedAsync(
            Path.Combine(physical.StagingChunksDirectory, "00000000.mvchunk"),
            firstOnly);
        var descriptor = new PhysicalSnapshotChunkDescriptor(
            0,
            StandardSnapshotSectionSetV1.SectionIds[0],
            StandardSnapshotSectionSetV1.SectionIds[^1],
            header.UncompressedLength,
            header.StoredLength,
            header.Compression,
            header.LogicalPayloadDigest.ToArray(),
            header.StoredPayloadDigest.ToArray(),
            SnapshotChunkFile.RelativePath(0));
        await WriteManifestAsync(physical, fixture, new[] { descriptor });
        await RequireRejectedAsync(
            () => CanonicalSnapshotStagingValidatorV1.ValidateAsync(physical, fixture.Materials, fixture.Verifiers, fixture.State),
            "persistence.snapshot.manifest-chunk-section-range-mismatch:",
            "Manifest section range must match decoded chunk fragment range.");
    }

    private static async Task<PhysicalSnapshotManifestMaterialV1> WriteManifestAsync(
        SnapshotPhysicalPaths physical,
        Fixture fixture,
        IReadOnlyList<PhysicalSnapshotChunkDescriptor> descriptors)
    {
        var logical = BuildLogicalManifest(physical, fixture);
        var manifest = PhysicalSnapshotManifestWireCodecV1.WithComputedPhysicalDigest(logical, descriptors);
        await SnapshotPhysicalStaging.WriteManifestDurablyAsync(
            physical,
            PhysicalSnapshotManifestWireCodecV1.Encode(manifest));
        return manifest;
    }

    private static LogicalSnapshotManifest BuildLogicalManifest(SnapshotPhysicalPaths physical, Fixture fixture)
    {
        var requiredDomains = StandardDomainPartitionRegistry.Entries
            .Select(static entry => entry.OwnerDomain.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var draft = new LogicalSnapshotManifest(
            PersistenceSchemaMajor: 1,
            PersistenceSchemaMinor: 0,
            WorldId: fixture.State.Header.WorldId,
            SnapshotId: physical.SnapshotId,
            SnapshotStep: fixture.State.Header.Step,
            HistoryAnchorSequence: 1,
            HistoryAnchorDigest: SHA256.HashData("snapshot-reassembly-history"u8),
            StateContinuityToken: SHA256.HashData("snapshot-reassembly-continuity"u8),
            WorldSeed: SHA256.HashData("snapshot-reassembly-world-seed-material"u8),
            SimulationConfigGeneration: fixture.State.Header.ConfigGeneration,
            SimulationConfigDigest: fixture.State.Diagnostic.ConfigDigest.ToArray(),
            MasterGeneration: fixture.State.Header.MasterGeneration,
            RequiredDomains: Array.AsReadOnly(requiredDomains),
            Sections: CanonicalSnapshotSectionValidationV1.ToLogicalSections(fixture.Materials, fixture.State),
            SnapshotDigest: new byte[32]);
        return LogicalSnapshotManifestWireCodecV1.WithComputedSnapshotDigest(draft);
    }

    private static PhysicalSnapshotChunkDescriptor Descriptor(
        uint index,
        SnapshotChunkHeader header,
        SnapshotChunkFragmentPayloadV1 payload)
    {
        if (payload.Fragments.Count == 0) throw new InvalidOperationException("Fixture chunk must contain fragments.");
        return new PhysicalSnapshotChunkDescriptor(
            index,
            payload.Fragments[0].SectionId,
            payload.Fragments[^1].SectionId,
            header.UncompressedLength,
            header.StoredLength,
            header.Compression,
            header.LogicalPayloadDigest.ToArray(),
            header.StoredPayloadDigest.ToArray(),
            SnapshotChunkFile.RelativePath(index));
    }

    private static Fixture BuildFixture(OpaqueId128 worldId)
    {
        var plans = new Dictionary<string, Plan>(StringComparer.Ordinal);
        for (var i = 0; i < StandardSnapshotSectionSetV1.SectionIds.Count; i++)
        {
            var id = StandardSnapshotSectionSetV1.SectionIds[i];
            var isDomain = StandardDomainPartitionRegistry.TryGet(id, out var identity) && identity is not null;
            var schema = isDomain ? identity!.PartitionSchema : new SchemaRefV1("fixture.snapshot-core-section");
            var count = isDomain ? 0UL : 1UL;
            SnapshotSectionFragmentMaterialV1[] fragments = i == 0
                ? new[]
                {
                    new SnapshotSectionFragmentMaterialV1(id, 0, 2, null, null, 0, Encoding.ASCII.GetBytes("a:" + id)),
                    new SnapshotSectionFragmentMaterialV1(id, 1, 2, null, null, count, Encoding.ASCII.GetBytes("b:" + id)),
                }
                : new[] { new SnapshotSectionFragmentMaterialV1(id, 0, 1, null, null, count, Encoding.ASCII.GetBytes("v:" + id)) };
            plans.Add(id, new Plan(schema, count, OwnerDigest(fragments), fragments));
        }

        var configDigest = SHA256.HashData("snapshot-reassembly-config"u8);
        var partitions = StandardDomainPartitionRegistry.Entries.Select(identity =>
        {
            var plan = plans[identity.PartitionId.Value];
            return new PartitionStateRefV1(new PartitionStateHeaderV1(
                identity, 1, 11, DetailLevelV1.D0Entity, plan.Count, plan.Digest));
        });
        var state = new WorldStateV1(
            new WorldStateHeaderV1(worldId, 11, SHA256.HashData("snapshot-reassembly-seed"u8), 1, 1, 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            configDigest);
        var materials = StandardSnapshotSectionSetV1.SectionIds.Select(id =>
        {
            var plan = plans[id];
            return new CanonicalSnapshotSectionMaterialV1(id, plan.Schema, plan.Count, plan.Digest.ToArray(), Array.AsReadOnly(plan.Fragments));
        }).ToArray();
        var entries = materials.Select(section => new SnapshotSectionSemanticVerifierV1(
            section.SectionId,
            section.SectionSchema,
            fragments => new SnapshotSectionSemanticVerificationV1(
                fragments.Aggregate(0UL, static (sum, fragment) => checked(sum + fragment.ItemCount)),
                OwnerDigest(fragments)))).ToArray();
        return new Fixture(state, Array.AsReadOnly(materials), Array.AsReadOnly(entries), new CanonicalSnapshotSemanticVerifierRegistryV1(entries));
    }

    private static byte[] OwnerDigest(IEnumerable<SnapshotSectionFragmentMaterialV1> fragments)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var fragment in fragments) hash.AppendData(fragment.FragmentPayload);
        return hash.GetHashAndReset();
    }

    private static OpaqueId128 Id(byte suffix)
    {
        var bytes = new byte[16];
        bytes[^1] = suffix;
        return OpaqueId128.FromBytes(bytes);
    }

    private static async Task RequireRejectedAsync(Func<Task> action, string expectedPrefix, string message)
    {
        var rejected = false;
        try { await action(); }
        catch (InvalidDataException ex) when (ex.Message.StartsWith(expectedPrefix, StringComparison.Ordinal)) { rejected = true; }
        Require(rejected, message);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed record Plan(SchemaRefV1 Schema, ulong Count, byte[] Digest, SnapshotSectionFragmentMaterialV1[] Fragments);
    private sealed record Fixture(
        WorldStateV1 State,
        IReadOnlyList<CanonicalSnapshotSectionMaterialV1> Materials,
        IReadOnlyList<SnapshotSectionSemanticVerifierV1> VerifierEntries,
        CanonicalSnapshotSemanticVerifierRegistryV1 Verifiers);
}
