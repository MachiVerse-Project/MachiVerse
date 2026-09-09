using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class CanonicalSnapshotStage2Smoke
{
    internal static async Task RunAsync()
    {
        VerifyActualPartitionAuthorityBinding();
        await VerifyProductionZstdChunkPathAsync();
    }

    private static void VerifyActualPartitionAuthorityBinding()
    {
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var authority = new DomainPartitionSnapshotAuthorityV1<Qa04ResidentIdentityLifecyclePayloadV1>(
            resident.Partition,
            resident.PartitionHeader,
            static payload => payload.CanonicalDigest());
        Require(authority.ActualItemCount == 1, "Stage 2 authority must expose actual record count.");
        authority.VerifyBoundAuthority();

        var badCount = new PartitionStateHeaderV1(
            resident.Partition.Identity,
            resident.PartitionHeader.Revision,
            resident.PartitionHeader.BasisStep,
            resident.PartitionHeader.DetailLevel,
            itemCount: 2,
            resident.PartitionHeader.CanonicalDigest);
        ExpectInvalid(
            "partition header item count vs actual records",
            () => _ = new DomainPartitionSnapshotAuthorityV1<Qa04ResidentIdentityLifecyclePayloadV1>(
                resident.Partition,
                badCount,
                static payload => payload.CanonicalDigest()));

        var badDigestBytes = resident.PartitionHeader.CanonicalDigest.ToArray();
        badDigestBytes[0] ^= 0x80;
        var badDigest = new PartitionStateHeaderV1(
            resident.Partition.Identity,
            resident.PartitionHeader.Revision,
            resident.PartitionHeader.BasisStep,
            resident.PartitionHeader.DetailLevel,
            resident.PartitionHeader.ItemCount,
            badDigestBytes);
        ExpectInvalid(
            "partition header digest vs actual records",
            () => _ = new DomainPartitionSnapshotAuthorityV1<Qa04ResidentIdentityLifecyclePayloadV1>(
                resident.Partition,
                badDigest,
                static payload => payload.CanonicalDigest()));

        ExpectInvalid(
            "header-only 103-section substitution",
            () => _ = new DomainPartitionSnapshotAuthoritySetV1(
                resident.WorldState,
                new IDomainPartitionSnapshotAuthorityV1[] { authority }));
    }

    private static async Task VerifyProductionZstdChunkPathAsync()
    {
        var config = new CoreConfigCoordinator().LoadStartup(
            """
            [meta]
            format = "machiverse-config"
            schema_version = "1.0"
            component = "simulation-core"
            """);
        var policy = SnapshotCanonicalCompressionPolicyV1.FromConfig(config);
        Require(policy.Compression == SnapshotCompression.Zstd && policy.ZstdLevel == 3,
            "Canonical Core Config must select Zstd level 3.");

        var fragment = new SnapshotSectionFragmentMaterialV1(
            "resident.identity_lifecycle",
            FragmentIndex: 0,
            FragmentCount: 1,
            FirstRecordId: Enumerable.Repeat((byte)0x01, 16).ToArray(),
            LastRecordId: Enumerable.Repeat((byte)0x01, 16).ToArray(),
            ItemCount: 1,
            FragmentPayload: Enumerable.Range(0, 8192).Select(static value => (byte)(value % 17)).ToArray());
        var payload = new SnapshotChunkFragmentPayloadV1(new[] { fragment });
        var codec = new ZstdSnapshotChunkCompressionCodecV1();
        var root = Path.Combine(Path.GetTempPath(), "machiverse-stage2-zstd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "valid.mvchunk");
            var written = await CanonicalSnapshotProductionChunkFileV1.WriteAsync(path, payload, config, codec);
            Require(written.Compression == SnapshotCompression.Zstd,
                "Production Snapshot chunk writer must honor canonical Zstd policy.");
            Require(written.StoredLength < written.UncompressedLength,
                "Compressible Stage 2 probe should be physically Zstd-compressed.");

            var read = await CanonicalSnapshotChunkFileV1.ReadValidatedAsync(
                path,
                CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(codec));
            Require(read.Header.Compression == SnapshotCompression.Zstd,
                "Validated readback must preserve Zstd framing.");
            Require(read.Payload.Fragments.Count == 1 &&
                    read.Payload.Fragments[0].FragmentPayload.SequenceEqual(fragment.FragmentPayload),
                "Zstd readback must reconstruct exact fragment material.");

            var noCodecRejected = false;
            try
            {
                _ = await CanonicalSnapshotChunkFileV1.ReadValidatedAsync(
                    path,
                    Array.Empty<ISnapshotChunkCompressionDecoderV1>());
            }
            catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.compression-codec-unavailable:zstd")
            {
                noCodecRejected = true;
            }
            Require(noCodecRejected, "Zstd Snapshot readback must fail closed when the production codec is unavailable.");

            var tampered = Path.Combine(root, "tampered.mvchunk");
            File.Copy(path, tampered);
            var bytes = await File.ReadAllBytesAsync(tampered);
            bytes[^1] ^= 0x01;
            await File.WriteAllBytesAsync(tampered, bytes);
            var tamperRejected = false;
            try
            {
                _ = await CanonicalSnapshotChunkFileV1.ReadValidatedAsync(
                    tampered,
                    CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(codec));
            }
            catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.stored-digest-mismatch")
            {
                tamperRejected = true;
            }
            Require(tamperRejected, "Physical chunk tamper must be rejected before decompression authority is used.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ExpectInvalid(string name, Action action)
    {
        var rejected = false;
        try { action(); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, $"Negative test must reject: {name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
