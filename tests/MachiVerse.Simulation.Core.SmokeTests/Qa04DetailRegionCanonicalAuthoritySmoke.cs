using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04DetailRegionCanonicalAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04DetailRegionCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04DetailRegionCanonicalAuthorityV1.MaterializeCanonical();

        Require(materialization.MaterializedRecordCount == 4_096 &&
                materialization.Partition.ItemCount == 4_096 &&
                materialization.RegionsByTile.Count == 4_096,
            "Canonical DetailRegion authority must materialize exactly 4,096 records.");

        var recovered = Qa04DetailRegionSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(recovered == 4_096,
            "Canonical DetailRegion Snapshot/recovery must semantically recover all 4,096 records.");

        var levelEntries = materialization.Partition.RecordsCanonical
            .SelectMany(static record => record.Payload.LevelByDomain)
            .ToArray();
        Require(levelEntries.Length == 32_768 &&
                levelEntries.Count(static entry => entry.Value == (byte)DetailLevelV1.D0Entity) == 890 &&
                levelEntries.Count(static entry => entry.Value == (byte)DetailLevelV1.D1LocalAggregate) == 534 &&
                levelEntries.Count(static entry => entry.Value == (byte)DetailLevelV1.D2RegionalAggregate) == 31_344,
            "Canonical DetailRegion genesis detail-level cardinality drifted.");

        var requirements = Qa04CanonicalDetailTransitionBindingV1.CanonicalRequirements().ToArray();
        Require(requirements.Length == 1_424 && requirements.Select(static item => item.TileIndex).Distinct().Count() == 1_424,
            "Canonical DetailRegion workload override set must remain 1,424 unique tiles.");
        foreach (var requirement in requirements)
        {
            var region = materialization.RegionForTile(requirement.TileIndex);
            Require(region.DetailRegionId == Qa04DetailRegionCanonicalAuthorityV1.RegionId(requirement.TileIndex) &&
                    region.SpatialScopeRef == requirement.SpatialScopeId &&
                    region.GetLevel(requirement.DomainToken) == requirement.CurrentLevel,
                "Canonical DetailRegion requirement must bind to exact genesis authority.");
        }

        var configDigest = HashSuite.DomainHash("mv.qa04-reference-world-config.v1", writer =>
        {
            writer.WriteMapStart(1);
            writer.WriteUnsigned(0);
            writer.WriteAsciiText(Qa04ReferenceLoadV1.BenchmarkProfileId);
        });
        var boundCount = 0;
        for (ulong cadence = 0; cadence < Qa04CanonicalDetailTransitionBindingV1.CanonicalCadenceCount; cadence++)
        {
            var step = checked((cadence + 1) * Qa04ReferenceLoadV1.DetailTransitionEverySteps);
            var bound = Qa04CanonicalDetailTransitionBindingV1.BindForStep(
                step,
                activeConfigGeneration: 1,
                configDigest,
                requirement => materialization.RegionForTile(requirement.TileIndex));
            Require(bound.Count == 16,
                "Every canonical DetailRegion cadence must bind all 16 requests.");
            boundCount = checked(boundCount + bound.Count);
        }
        Require(boundCount == 1_424,
            "Canonical DetailRegion authority must bind all 1,424 production requests.");

        var firstRequirement = requirements[0];
        var firstRegion = materialization.RegionForTile(firstRequirement.TileIndex);
        var wrongScopeTile = checked((ushort)((firstRequirement.TileIndex + 1) % 4_096));
        var wrongScope = new DetailRegionStateV1(
            firstRegion.DetailRegionId,
            Qa04SpatialTileScopeAuthorityV1.ScopeId(wrongScopeTile),
            firstRegion.LevelByDomain,
            firstRegion.LineageGeneration,
            firstRegion.LastTransitionStep,
            firstRegion.ActiveGuards);
        ExpectInvalid(() => Qa04CanonicalDetailTransitionBindingV1.BindForStep(
            firstRequirement.BasisStep,
            1,
            configDigest,
            requirement => requirement.RequestOrdinal == firstRequirement.RequestOrdinal
                ? wrongScope
                : materialization.RegionForTile(requirement.TileIndex)),
            "Wrong DetailRegion scope must fail closed.");

        var wrongLevels = firstRegion.LevelByDomain
            .Select(entry => entry.Key == firstRequirement.DomainToken
                ? new KeyValuePair<StableToken, DetailLevelV1>(entry.Key, DetailLevelV1.D2RegionalAggregate)
                : entry)
            .ToArray();
        var wrongLevel = new DetailRegionStateV1(
            firstRegion.DetailRegionId,
            firstRegion.SpatialScopeRef,
            wrongLevels,
            firstRegion.LineageGeneration,
            firstRegion.LastTransitionStep,
            firstRegion.ActiveGuards);
        ExpectInvalid(() => Qa04CanonicalDetailTransitionBindingV1.BindForStep(
            firstRequirement.BasisStep,
            1,
            configDigest,
            requirement => requirement.RequestOrdinal == firstRequirement.RequestOrdinal
                ? wrongLevel
                : materialization.RegionForTile(requirement.TileIndex)),
            "Wrong DetailRegion current level must fail closed.");

        ExpectInvalid(() => _ = new DetailDirectoryV1(new[] { firstRegion, firstRegion }),
            "Duplicate DetailRegion identity must fail closed.");

        var firstRecord = materialization.Partition.RecordsCanonical.First();
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                SpatialDetailRegionsPayloadV1.PartitionId,
                firstRecord.Payload.ToStandardPayload(),
                new EmptyReferenceResolver()),
            "Missing TileScope authority must fail closed.");

        Require(firstRecord.Payload.LineageGeneration == 0 &&
                firstRecord.Payload.LastTransitionStep == 0 &&
                firstRecord.Payload.ActiveGuards.Count == 0,
            "Canonical DetailRegion non-level genesis fields drifted.");

        var scope0 = Qa04SpatialTileScopeAuthorityV1.ScopeRef(0);
        var region0 = new PartitionRecordRefV1(
            SpatialDetailRegionsPayloadV1.PartitionId,
            Qa04DetailRegionCanonicalAuthorityV1.RegionId(0));
        Require(materialization.References.TryGetRecordSchema(scope0, out var scopeSchema) &&
                scopeSchema == StandardDomainPartitionRegistry.Get(SpatialScopeRegistryPayloadV1.PartitionId).RecordSchema &&
                materialization.References.TryGetRecordSchema(region0, out var regionSchema) &&
                regionSchema == StandardDomainPartitionRegistry.Get(SpatialDetailRegionsPayloadV1.PartitionId).RecordSchema,
            "Canonical DetailRegion resolver must retain actual TileScope and DetailRegion schemas.");
    }

    private static void ExpectInvalid(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException(message);
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
