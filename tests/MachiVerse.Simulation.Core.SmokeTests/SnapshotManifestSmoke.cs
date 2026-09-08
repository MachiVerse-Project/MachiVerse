using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;

internal static class SnapshotManifestSmoke
{
    internal static void Run()
    {
        var sectionIds = new List<string>
        {
            "core.world-state-header",
            "core.scheduler-state",
            "core.operation-state",
            "core.detail-directory",
            "core.domain-registry",
            "core.config-state",
        };
        for (var i = 0; i < 97; i++) sectionIds.Add($"partition.{i:000}");
        sectionIds.Sort(StringComparer.Ordinal);

        var sections = sectionIds.Select((id, index) => new LogicalSnapshotSection(
            id,
            $"snapshot.section.{index:000}",
            1,
            0,
            1,
            Hash((byte)(index + 1)),
            Required: true)).ToArray();

        var manifest = new LogicalSnapshotManifest(
            PersistenceSchemaMajor: 1,
            PersistenceSchemaMinor: 0,
            WorldId: OpaqueId128.Parse("00000000000000000000000000000041"),
            SnapshotId: OpaqueId128.Parse("00000000000000000000000000000042"),
            SnapshotStep: 1,
            HistoryAnchorSequence: 4,
            HistoryAnchorDigest: Hash(10),
            StateContinuityToken: Hash(11),
            WorldSeed: Hash(12),
            SimulationConfigGeneration: 1,
            SimulationConfigDigest: Hash(13),
            MasterGeneration: 1,
            RequiredDomains: ["sim.core", "sim.world"],
            Sections: sections,
            SnapshotDigest: Hash(14));

        SnapshotManifestValidation.ValidateLogical(manifest, sectionIds);
        SnapshotManifestValidation.ValidatePhysicalMapping(manifest,
        [
            new PhysicalSnapshotChunkDescriptor(
                0,
                sectionIds[0],
                sectionIds[^1],
                UncompressedLength: 4096,
                StoredLength: 4096,
                SnapshotCompression.None,
                Hash(20),
                Hash(21),
                SnapshotChunkFile.RelativePath(0))
        ]);

        var optionalSections = sections.ToArray();
        optionalSections[0] = optionalSections[0] with { Required = false };
        var optionalRejected = false;
        try
        {
            SnapshotManifestValidation.ValidateLogical(manifest with { Sections = optionalSections }, sectionIds);
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.required-section-marked-optional")
        {
            optionalRejected = true;
        }
        if (!optionalRejected)
            throw new InvalidOperationException("All 103 standard snapshot sections must remain required.");

        var gapRejected = false;
        try
        {
            SnapshotManifestValidation.ValidatePhysicalMapping(manifest,
            [
                new PhysicalSnapshotChunkDescriptor(
                    0,
                    sectionIds[1],
                    sectionIds[^1],
                    4096,
                    4096,
                    SnapshotCompression.None,
                    Hash(20),
                    Hash(21),
                    SnapshotChunkFile.RelativePath(0))
            ]);
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.chunk-section-coverage-gap")
        {
            gapRejected = true;
        }
        if (!gapRejected)
            throw new InvalidOperationException("Physical chunk mapping must cover logical sections without gaps.");
    }

    private static byte[] Hash(byte value) => Enumerable.Repeat(value, 32).ToArray();
}
