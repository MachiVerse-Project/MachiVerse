using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record CanonicalSnapshotProductionStageResultV1(
    PhysicalSnapshotManifestMaterialV1 Manifest,
    IReadOnlyList<PhysicalSnapshotChunkDescriptor> Chunks)
{
    public byte[] SnapshotDigest => Manifest.Logical.SnapshotDigest.ToArray();
    public byte[] PhysicalManifestDigest => Manifest.PhysicalManifestDigest.ToArray();
}

/// <summary>
/// Production Stage 2 drain that writes both canonical chunk files and the authoritative manifest.pb
/// for one frozen RunningSnapshotCutV1. The manifest is re-read and verified before the staged result
/// is returned to the commit path.
/// </summary>
public static class CanonicalSnapshotProductionManifestDrainV1
{
    public const ushort PersistenceSchemaMajor = 1;
    public const ushort PersistenceSchemaMinor = 0;

    public static async Task<CanonicalSnapshotProductionStageResultV1> StageRunningCutAsync(
        RunningSnapshotCutV1 cut,
        SnapshotPhysicalPaths physical,
        IEnumerable<CanonicalSnapshotSectionMaterialV1> sections,
        EffectiveCoreConfig frozenConfig,
        WorldSeed256 worldSeed,
        IEnumerable<RequiredAddonSnapshotMetadataV1>? requiredAddons = null,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs = null,
        ISnapshotChunkCompressionCodecV1? zstdCodec = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRunningCut(cut, physical, frozenConfig, worldSeed, out var worldSeedBytes);
        ArgumentNullException.ThrowIfNull(sections);

        var expectedSections = CanonicalSnapshotSectionValidationV1.ValidateStandard(sections, cut.FrozenState);
        var chunks = await CanonicalSnapshotProductionPhysicalDrainV1.StageRunningCutAsync(
            cut,
            physical,
            expectedSections,
            frozenConfig,
            zstdCodec,
            cancellationToken).ConfigureAwait(false);

        var logical = BuildLogicalManifest(
            cut,
            worldSeedBytes,
            CanonicalSnapshotSectionValidationV1.ToLogicalSections(expectedSections, cut.FrozenState),
            requiredAddons);
        var manifest = await WriteAndValidateManifestAsync(
            physical,
            logical,
            chunks,
            addonCodecs,
            cancellationToken).ConfigureAwait(false);
        SnapshotPhysicalManifestStagingValidationV1.RequireExpectedAuthority(
            manifest,
            expectedSections,
            cut.FrozenState,
            cut,
            logical.SnapshotDigest,
            manifest.PhysicalManifestDigest);

        return Result(manifest, chunks);
    }

    /// <summary>
    /// Bounded-memory production manifest drain. The exact-103 section metadata is retained, while
    /// section fragments and physical chunk payloads flow through the streaming packer one chunk at
    /// a time. Manifest wire/schema/digest semantics are identical to <see cref="StageRunningCutAsync"/>.
    /// </summary>
    public static async Task<CanonicalSnapshotProductionStageResultV1> StageRunningCutStreamingAsync(
        RunningSnapshotCutV1 cut,
        SnapshotPhysicalPaths physical,
        IEnumerable<CanonicalSnapshotStreamingSectionV1> sections,
        EffectiveCoreConfig frozenConfig,
        WorldSeed256 worldSeed,
        IEnumerable<RequiredAddonSnapshotMetadataV1>? requiredAddons = null,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs = null,
        ISnapshotChunkCompressionCodecV1? zstdCodec = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRunningCut(cut, physical, frozenConfig, worldSeed, out var worldSeedBytes);
        ArgumentNullException.ThrowIfNull(sections);

        var expectedSections = CanonicalSnapshotStreamingSectionValidationV1.ValidateStandardMetadata(
            sections,
            cut.FrozenState);
        var chunks = await CanonicalSnapshotProductionPhysicalDrainV1.StageRunningCutStreamingAsync(
            cut,
            physical,
            expectedSections,
            frozenConfig,
            zstdCodec,
            cancellationToken).ConfigureAwait(false);

        var logical = BuildLogicalManifest(
            cut,
            worldSeedBytes,
            CanonicalSnapshotStreamingSectionValidationV1.ToLogicalSections(expectedSections, cut.FrozenState),
            requiredAddons);
        var manifest = await WriteAndValidateManifestAsync(
            physical,
            logical,
            chunks,
            addonCodecs,
            cancellationToken).ConfigureAwait(false);
        SnapshotPhysicalManifestStreamingAuthorityValidationV1.RequireExpectedAuthority(
            manifest,
            expectedSections,
            cut.FrozenState,
            cut,
            logical.SnapshotDigest,
            manifest.PhysicalManifestDigest);

        return Result(manifest, chunks);
    }

    private static void ValidateRunningCut(
        RunningSnapshotCutV1 cut,
        SnapshotPhysicalPaths physical,
        EffectiveCoreConfig frozenConfig,
        WorldSeed256 worldSeed,
        out byte[] worldSeedBytes)
    {
        ArgumentNullException.ThrowIfNull(cut);
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(frozenConfig);
        if (physical.SnapshotId != cut.SnapshotId)
            throw new InvalidDataException("snapshot-running.physical-id-mismatch");
        if (frozenConfig.Generation != cut.FrozenState.Header.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(frozenConfig.Digest, cut.FrozenState.Diagnostic.ConfigDigest))
            throw new InvalidDataException("snapshot-running.manifest-config-authority-mismatch");

        worldSeedBytes = worldSeed.ToBytes();
        var worldSeedDigest = SHA256.HashData(worldSeedBytes);
        if (!CryptographicOperations.FixedTimeEquals(worldSeedDigest, cut.FrozenState.Header.WorldSeedDigest))
            throw new InvalidDataException("snapshot-running.manifest-world-seed-authority-mismatch");
    }

    private static LogicalSnapshotManifest BuildLogicalManifest(
        RunningSnapshotCutV1 cut,
        byte[] worldSeedBytes,
        IReadOnlyList<LogicalSnapshotSection> sections,
        IEnumerable<RequiredAddonSnapshotMetadataV1>? requiredAddons)
    {
        var requiredDomains = StandardDomainPartitionRegistry.Entries
            .Select(static entry => entry.OwnerDomain.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var addons = (requiredAddons ?? Array.Empty<RequiredAddonSnapshotMetadataV1>())
            .OrderBy(static addon => addon.AddonId, StringComparer.Ordinal)
            .ToArray();

        var logicalDraft = new LogicalSnapshotManifest(
            PersistenceSchemaMajor,
            PersistenceSchemaMinor,
            cut.FrozenState.Header.WorldId,
            cut.SnapshotId,
            cut.SnapshotStep,
            cut.HistoryAnchor.Sequence,
            cut.HistoryAnchor.Digest.ToArray(),
            cut.StateContinuityToken.ToArray(),
            worldSeedBytes.ToArray(),
            cut.FrozenState.Header.ConfigGeneration,
            cut.FrozenState.Diagnostic.ConfigDigest.ToArray(),
            cut.FrozenState.Header.MasterGeneration,
            Array.AsReadOnly(requiredDomains),
            sections,
            new byte[32])
        {
            RequiredAddons = Array.AsReadOnly(addons),
        };
        return LogicalSnapshotManifestWireCodecV1.WithComputedSnapshotDigest(logicalDraft);
    }

    private static async Task<PhysicalSnapshotManifestMaterialV1> WriteAndValidateManifestAsync(
        SnapshotPhysicalPaths physical,
        LogicalSnapshotManifest logical,
        IReadOnlyList<PhysicalSnapshotChunkDescriptor> chunks,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs,
        CancellationToken cancellationToken)
    {
        var manifest = PhysicalSnapshotManifestWireCodecV1.WithComputedPhysicalDigest(logical, chunks, addonCodecs);
        var manifestBytes = PhysicalSnapshotManifestWireCodecV1.Encode(manifest, addonCodecs);
        await SnapshotPhysicalStaging.WriteManifestDurablyAsync(
            physical,
            manifestBytes,
            cancellationToken).ConfigureAwait(false);

        var verified = await SnapshotPhysicalManifestStagingValidationV1.ValidateAsync(
            physical,
            addonCodecs,
            cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(
                verified.Logical.SnapshotDigest,
                logical.SnapshotDigest) ||
            !CryptographicOperations.FixedTimeEquals(
                verified.PhysicalManifestDigest,
                manifest.PhysicalManifestDigest))
        {
            throw new InvalidDataException("persistence.snapshot.manifest-staged-digest-mismatch");
        }
        return verified;
    }

    private static CanonicalSnapshotProductionStageResultV1 Result(
        PhysicalSnapshotManifestMaterialV1 manifest,
        IReadOnlyList<PhysicalSnapshotChunkDescriptor> chunks)
        => new(
            manifest,
            Array.AsReadOnly(chunks.Select(static chunk => chunk with
            {
                LogicalPayloadDigest = chunk.LogicalPayloadDigest.ToArray(),
                StoredPayloadDigest = chunk.StoredPayloadDigest.ToArray(),
            }).ToArray()));
}
