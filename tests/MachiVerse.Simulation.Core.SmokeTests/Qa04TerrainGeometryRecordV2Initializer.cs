using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainGeometryRecordV2Initializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SpatialTerrainGeometryRecordSchemaV2.ValidateCanonicalContract();
        VerifyRootMigrationAndWire();
        VerifyBrickRoundTrip();
        VerifyFailClosedWire();
        VerifyProductionGateRemainsClosed();
    }

    private static void VerifyRootMigrationAndWire()
    {
        var identity = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        var rootId = Id("00000000000000000000000000024001");
        var brickId = Id("00000000000000000000000000024002");
        var lineageId = Id("00000000000000000000000000024003");
        var archive = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        var payload = new SpatialTerrainGeometryPayloadV1(
            new PartitionRecordRefV1("spatial.scope_registry", Id("00000000000000000000000000024010")),
            new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, brickId),
            17,
            [new StableToken("rock"), new StableToken("soil")],
            [new PartitionRecordRefV1("spatial.containment_topology", Id("00000000000000000000000000024011"))],
            archive);
        var v1 = new DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV1>(
            rootId,
            identity.RecordSchema,
            3,
            7,
            null,
            DetailLevelV1.D0Entity,
            lineageId,
            payload);

        var migrated = SpatialTerrainGeometryRecordMaterialV2.MigrateRoot(v1);
        Require(migrated.RecordId == rootId, "terrain v1->v2 migration must preserve record_id.");
        Require(migrated.Revision == 3 && migrated.CreatedStep == 7 && migrated.LineageRef == lineageId,
            "terrain v1->v2 migration must preserve common envelope metadata.");
        Require(migrated.RecordSchema.SchemaId == identity.RecordSchema.SchemaId &&
                migrated.RecordSchema.Version == new SchemaVersionV1(2, 0),
            "terrain v2 migration must retain stable schema id and advance only the major version.");

        var encoded = SpatialTerrainGeometryRecordWireCodecV2.Encode(migrated);
        var decoded = SpatialTerrainGeometryRecordWireCodecV2.Decode(encoded);
        var root = decoded.Payload as SpatialTerrainRootPayloadV2
            ?? throw new InvalidOperationException("terrain root wire round-trip changed record arm.");
        Require(root.RootBrickRef.PartitionId.Value == SpatialTerrainGeometryRecordSchemaV2.PartitionId &&
                root.RootBrickRef.RecordId == brickId && root.GeometryRevision == 17,
            "terrain root wire round-trip changed authoritative root linkage.");
        Require(root.SurfaceClasses.Select(static token => token.Value).SequenceEqual(["rock", "soil"]),
            "terrain root wire round-trip changed surface class order.");
        Require(root.ArchiveAnchor is not null && root.ArchiveAnchor.SequenceEqual(archive),
            "terrain root wire round-trip changed archive digest.");
        Require(SpatialTerrainGeometryRecordWireCodecV2.Encode(decoded).SequenceEqual(encoded),
            "terrain root v2 wire must be canonical after decode/re-encode.");
    }

    private static void VerifyBrickRoundTrip()
    {
        var brickId = Id("00000000000000000000000000024100");
        var lineageId = Id("00000000000000000000000000024101");
        var sdf = Enumerable.Range(0, TerrainBrickV1.SdfSampleCount)
            .Select(static index => index - 364)
            .ToArray();
        var materials = Enumerable.Range(0, TerrainBrickV1.SurfaceMaterialCount)
            .Select(static index => (ushort)(index % 23))
            .ToArray();
        var source = new TerrainBrickV1(
            brickId,
            2,
            new SpatialCellKeyV1(3, -4, 5, -6),
            1_000,
            sdf,
            materials,
            11);

        var material = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            source,
            createdStep: 9,
            detailLevel: DetailLevelV1.D1LocalAggregate,
            lineageRef: lineageId);
        Require(material.RecordId == source.BrickId && material.Revision == source.Revision,
            "TerrainBrickV1 brick_id/revision must map to the common v2 record envelope.");

        var encoded = SpatialTerrainGeometryRecordWireCodecV2.Encode(material);
        var decoded = SpatialTerrainGeometryRecordWireCodecV2.Decode(encoded);
        var reconstructed = decoded.ToTerrainBrick();
        Require(reconstructed.BrickId == source.BrickId && reconstructed.Revision == source.Revision,
            "terrain brick wire round-trip changed identity/revision.");
        Require(reconstructed.Level == 2 && reconstructed.CellOrigin == new SpatialCellKeyV1(3, -4, 5, -6),
            "terrain brick wire must preserve level and cell_origin independently.");
        Require(reconstructed.SampleSpacingMm == 1_000 && reconstructed.SdfMm.SequenceEqual(sdf) &&
                reconstructed.SurfaceMaterialIds.SequenceEqual(materials),
            "terrain brick wire round-trip must preserve all SBO-SDF sample/material values.");
        Require(SpatialTerrainGeometryRecordWireCodecV2.Encode(decoded).SequenceEqual(encoded),
            "terrain brick v2 wire must be canonical after decode/re-encode.");
    }

    private static void VerifyFailClosedWire()
    {
        var brick = new TerrainBrickV1(
            Id("00000000000000000000000000024200"),
            0,
            new SpatialCellKeyV1(0, 0, 0, 0),
            250,
            Enumerable.Repeat(0, TerrainBrickV1.SdfSampleCount),
            Enumerable.Repeat((ushort)0, TerrainBrickV1.SurfaceMaterialCount),
            1);
        var encoded = SpatialTerrainGeometryRecordWireCodecV2.Encode(
            SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(brick, 0, DetailLevelV1.D0Entity));

        ExpectReject(
            () => SpatialTerrainGeometryRecordWireCodecV2.Decode(encoded.Concat(new byte[] { 0x50, 0x00 }).ToArray()),
            "record-field-unknown");
        ExpectReject(
            () => SpatialTerrainGeometryRecordWireCodecV2.Decode(encoded.Concat(new byte[] { 0x20, 0x01 }).ToArray()),
            "record-field-order");
        ExpectReject(
            () => _ = new SpatialTerrainRootPayloadV2(
                new PartitionRecordRefV1("spatial.scope_registry", Id("00000000000000000000000000024210")),
                new PartitionRecordRefV1("spatial.void_geometry", Id("00000000000000000000000000024211")),
                1,
                Array.Empty<StableToken>(),
                Array.Empty<PartitionRecordRefV1>(),
                null),
            "spatial.terrain-v2.root-brick-owner");
    }

    private static void VerifyProductionGateRemainsClosed()
    {
        var current = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        Require(current.RecordSchema.Version == new SchemaVersionV1(1, 0),
            "standalone terrain v2 contract must not silently flip the production standard registry.");
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.Any(static code =>
                code.Value == "qa04.material.terrain-brick-authority-undefined"),
            "terrain material gate must remain fail-closed until v2 production integration and canonical brick content exist.");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void ExpectReject(Action action, string code)
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected rejection containing '{code}'.");
        }
        catch (InvalidDataException ex) when (ex.Message.Contains(code, StringComparison.Ordinal))
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
