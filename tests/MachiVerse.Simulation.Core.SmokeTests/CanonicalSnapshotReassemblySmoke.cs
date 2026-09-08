using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class CanonicalSnapshotReassemblySmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-snapshot-reassembly-" + Guid.NewGuid().ToString("N"));
        try
        {
            var worldId = OpaqueId128.Parse("00000000000000000000000000000091");
            var fixture = BuildFixture(worldId);
            var paths = PersistenceLayout.Resolve(root, worldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);

            await VerifyCrossChunkReassemblyAsync(paths, fixture);
            VerifyMissingVerifierRejected(fixture);
            await VerifySemanticTamperRejectedAsync(paths, fixture);
            await VerifyStoredTamperRejectedAsync(paths, fixture);
            await VerifyLogicalDigestTamperRejectedAsync(paths, fixture);
            await VerifyMissingCompressionDecoderRejectedAsync(paths, fixture);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task VerifyCrossChunkReassemblyAsync(
        WorldPersistencePaths paths,
        Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(
            paths,
            OpaqueId128.Parse("00000000000000000000000000000092"));
        var all = fixture.Materials.SelectMany(static section => section.Fragments).ToArray();
        Require(fixture.Materials[0].Fragments.Count == 2,
            "Reassembly fixture must split its first section across chunks.");
        await CanonicalSnapshotChunkFileV1.WriteUncompressedAsync(
            Path.Combine(physical.StagingChunksDirectory, "00000000.mvchunk"),
            new SnapshotChunkFragmentPayloadV1([all[0]]));
        await CanonicalSnapshotChunkFileV1.WriteUncompressedAsync(
            Path.Combine(physical.StagingChunksDirectory, "00000001.mvchunk"),
            new SnapshotChunkFragmentPayloadV1(Array.AsReadOnly(all[1..])));
        await SnapshotPhysicalStaging.WriteManifestDurablyAsync(physical, [0x01]);

        await CanonicalSnapshotStagingValidatorV1.ValidateAsync(
            physical,
            fixture.Materials,
            fixture.Verifiers,
            fixture.State);
    }

    private static void VerifyMissingVerifierRejected(Fixture fixture)
    {
        var rejected = false;
        try
        {
            _ = new CanonicalSnapshotSemanticVerifierRegistryV1(
                fixture.VerifierEntries.Skip(1));
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.semantic-verifier-count-mismatch")
        {
            rejected = true;
        }
        Require(rejected, "Missing standard snapshot owner verifier must fail closed.");
    }

    private static async Task VerifySemanticTamperRejectedAsync(
        WorldPersistencePaths paths,
        Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(
            paths,
            OpaqueId128.Parse("00000000000000000000000000000093"));
        var tampered = fixture.Materials
            .Select((section, index) => index == 0
                ? section with
                {
                    Fragments = section.Fragments
                        .Select((fragment, fragmentIndex) => fragmentIndex == 0
                            ? fragment with { FragmentPayload = fragment.FragmentPayload.Concat(new byte[] { 0xff }).ToArray() }
                            : fragment)
                        .ToArray()
                }
                : section)
            .ToArray();
        await CanonicalSnapshotPhysicalDrainV1.StageUncompressedAsync(
            physical,
            tampered,
            fixture.State);
        await SnapshotPhysicalStaging.WriteManifestDurablyAsync(physical, [0x02]);

        var rejected = false;
        try
        {
            await CanonicalSnapshotStagingValidatorV1.ValidateAsync(
                physical,
                fixture.Materials,
                fixture.Verifiers,
                fixture.State);
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("persistence.snapshot.section-semantic-digest-mismatch:", StringComparison.Ordinal))
        {
            rejected = true;
        }
        Require(rejected,
            "A physically valid chunk with schema-owner semantic tamper must be rejected after reassembly.");
    }

    private static async Task VerifyStoredTamperRejectedAsync(
        WorldPersistencePaths paths,
        Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(
            paths,
            OpaqueId128.Parse("00000000000000000000000000000094"));
        await CanonicalSnapshotPhysicalDrainV1.StageUncompressedAsync(
            physical,
            fixture.Materials,
            fixture.State);
        await SnapshotPhysicalStaging.WriteManifestDurablyAsync(physical, [0x03]);
        var chunkPath = Path.Combine(physical.StagingChunksDirectory, "00000000.mvchunk");
        await using (var stream = new FileStream(chunkPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = SnapshotChunkFile.HeaderLength;
            var original = stream.ReadByte();
            if (original < 0) throw new InvalidOperationException("Canonical snapshot chunk fixture is empty.");
            stream.Position = SnapshotChunkFile.HeaderLength;
            stream.WriteByte((byte)(original ^ 0x01));
            stream.Flush(flushToDisk: true);
        }

        var rejected = false;
        try
        {
            await CanonicalSnapshotStagingValidatorV1.ValidateAsync(
                physical,
                fixture.Materials,
                fixture.Verifiers,
                fixture.State);
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.stored-digest-mismatch")
        {
            rejected = true;
        }
        Require(rejected, "Stored snapshot chunk tamper must fail before semantic reassembly.");
    }

    private static async Task VerifyLogicalDigestTamperRejectedAsync(
        WorldPersistencePaths paths,
        Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(
            paths,
            OpaqueId128.Parse("00000000000000000000000000000095"));
        var chunks = SnapshotChunkPackerV1.PackStandard(fixture.Materials, fixture.State);
        var encoded = SnapshotChunkPayloadWireCodecV1.Encode(chunks[0]);
        var wrongLogicalDigest = SHA256.HashData(encoded);
        var canonicalLogicalDigest = SnapshotChunkLogicalPayloadDigestV1.Compute(chunks[0]);
        Require(!wrongLogicalDigest.SequenceEqual(canonicalLogicalDigest),
            "Raw protobuf SHA-256 unexpectedly matched the canonical chunk semantic digest.");
        await SnapshotChunkFile.WriteAsync(
            Path.Combine(physical.StagingChunksDirectory, "00000000.mvchunk"),
            encoded,
            (ulong)encoded.Length,
            wrongLogicalDigest,
            SnapshotCompression.None);
        await SnapshotPhysicalStaging.WriteManifestDurablyAsync(physical, [0x04]);

        var rejected = false;
        try
        {
            await CanonicalSnapshotStagingValidatorV1.ValidateAsync(
                physical,
                fixture.Materials,
                fixture.Verifiers,
                fixture.State);
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.logical-payload-digest-mismatch")
        {
            rejected = true;
        }
        Require(rejected, "Raw protobuf byte hash must not satisfy the canonical chunk semantic digest.");
    }

    private static async Task VerifyMissingCompressionDecoderRejectedAsync(
        WorldPersistencePaths paths,
        Fixture fixture)
    {
        var physical = SnapshotPhysicalStaging.Prepare(
            paths,
            OpaqueId128.Parse("00000000000000000000000000000096"));
        var chunks = SnapshotChunkPackerV1.PackStandard(fixture.Materials, fixture.State);
        var encoded = SnapshotChunkPayloadWireCodecV1.Encode(chunks[0]);
        await SnapshotChunkFile.WriteAsync(
            Path.Combine(physical.StagingChunksDirectory, "00000000.mvchunk"),
            encoded,
            checked((ulong)encoded.Length + 1),
            SnapshotChunkLogicalPayloadDigestV1.Compute(chunks[0]),
            SnapshotCompression.Zstd);
        await SnapshotPhysicalStaging.WriteManifestDurablyAsync(physical, [0x05]);

        var rejected = false;
        try
        {
            await CanonicalSnapshotStagingValidatorV1.ValidateAsync(
                physical,
                fixture.Materials,
                fixture.Verifiers,
                fixture.State);
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.compression-codec-unavailable:zstd")
        {
            rejected = true;
        }
        Require(rejected, "Zstd chunk without a registered decoder must fail closed.");
    }

    private static Fixture BuildFixture(OpaqueId128 worldId)
    {
        var plans = new Dictionary<string, SectionPlan>(StringComparer.Ordinal);
        for (var i = 0; i < StandardSnapshotSectionSetV1.SectionIds.Count; i++)
        {
            var sectionId = StandardSnapshotSectionSetV1.SectionIds[i];
            var isDomain = StandardDomainPartitionRegistry.TryGet(sectionId, out var identity) && identity is not null;
            var schema = isDomain
                ? identity!.PartitionSchema
                : new SchemaRefV1("fixture.snapshot-core-section");
            var logicalItemCount = isDomain ? 0UL : 1UL;
            var fragments = i == 0
                ? new[]
                {
                    new SnapshotSectionFragmentMaterialV1(
                        sectionId, 0, 2, null, null, 0,
                        Encoding.ASCII.GetBytes("fixture-a:" + sectionId)),
                    new SnapshotSectionFragmentMaterialV1(
                        sectionId, 1, 2, null, null, logicalItemCount,
                        Encoding.ASCII.GetBytes("fixture-b:" + sectionId)),
                }
                : new[]
                {
                    new SnapshotSectionFragmentMaterialV1(
                        sectionId, 0, 1, null, null, logicalItemCount,
                        Encoding.ASCII.GetBytes("fixture:" + sectionId)),
                };
            var digest = OwnerDigest(fragments);
            plans.Add(sectionId, new SectionPlan(schema, logicalItemCount, digest, fragments));
        }

        var configDigest = SHA256.HashData("snapshot-reassembly-config"u8);
        var partitions = StandardDomainPartitionRegistry.Entries.Select(identity =>
        {
            var plan = plans[identity.PartitionId.Value];
            return new PartitionStateRefV1(new PartitionStateHeaderV1(
                identity,
                revision: 1,
                basisStep: 11,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: plan.LogicalItemCount,
                canonicalDigest: plan.LogicalDigest));
        });
        var state = new WorldStateV1(
            new WorldStateHeaderV1(
                worldId,
                step: 11,
                worldSeedDigest: SHA256.HashData("snapshot-reassembly-seed"u8),
                configGeneration: 1,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            configDigest);

        var materials = StandardSnapshotSectionSetV1.SectionIds
            .Select(sectionId =>
            {
                var plan = plans[sectionId];
                return new CanonicalSnapshotSectionMaterialV1(
                    sectionId,
                    plan.Schema,
                    plan.LogicalItemCount,
                    plan.LogicalDigest.ToArray(),
                    Array.AsReadOnly(plan.Fragments.ToArray()));
            })
            .ToArray();
        var verifierEntries = materials.Select(section => new SnapshotSectionSemanticVerifierV1(
            section.SectionId,
            section.SectionSchema,
            fragments => new SnapshotSectionSemanticVerificationV1(
                fragments.Aggregate(0UL, static (total, fragment) => checked(total + fragment.ItemCount)),
                OwnerDigest(fragments)))).ToArray();
        var verifiers = new CanonicalSnapshotSemanticVerifierRegistryV1(verifierEntries);
        return new Fixture(state, Array.AsReadOnly(materials), Array.AsReadOnly(verifierEntries), verifiers);
    }

    private static byte[] OwnerDigest(IEnumerable<SnapshotSectionFragmentMaterialV1> fragments)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var fragment in fragments)
            hash.AppendData(fragment.FragmentPayload);
        return hash.GetHashAndReset();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record SectionPlan(
        SchemaRefV1 Schema,
        ulong LogicalItemCount,
        byte[] LogicalDigest,
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> Fragments);

    private sealed record Fixture(
        WorldStateV1 State,
        IReadOnlyList<CanonicalSnapshotSectionMaterialV1> Materials,
        IReadOnlyList<SnapshotSectionSemanticVerifierV1> VerifierEntries,
        CanonicalSnapshotSemanticVerifierRegistryV1 Verifiers);
}
