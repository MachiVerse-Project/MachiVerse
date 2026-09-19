using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04CrossDomainTransactionDetailGuardReconstructionSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var subjects = SubjectsAcrossTwoTiles();
        var active = State(
            "0000000000000000000000000004a001",
            TransactionLifecycleV1.Active,
            [subjects.SameTileA, subjects.SameTileB, subjects.OtherTile]);
        var committed = State(
            "0000000000000000000000000004a002",
            TransactionLifecycleV1.Committed,
            [OpaqueId128.Parse("0033000000000000000000000004a013")]);

        var activeTiles = active.SubjectIds
            .Select(Qa04ReferenceLoadV1.RegionalTileIndex)
            .Distinct()
            .Order()
            .ToArray();
        Require(activeTiles.Length == 2, "Fixture must span exactly two canonical tiles.");

        var scopeByTile = activeTiles.ToDictionary(
            static tile => tile,
            static tile => OpaqueId128.Parse((0x4e000UL + tile).ToString("x32")));
        PartitionRecordRefV1 TileScope(ushort tile)
        {
            if (!scopeByTile.TryGetValue(tile, out var scopeId))
                scopeId = OpaqueId128.Parse((0x4e000UL + tile).ToString("x32"));
            return new PartitionRecordRefV1(SpatialScopeRegistryPayloadV1.PartitionId, scopeId);
        }

        var regionByTile = activeTiles.ToDictionary(
            static tile => tile,
            static tile => OpaqueId128.Parse((0x4b000UL + tile).ToString("x32")));
        var unrelatedRegion = OpaqueId128.Parse("0000000000000000000000000004bfff");
        var unrelatedScope = OpaqueId128.Parse("0000000000000000000000000004efff");
        var regions = activeTiles.Select(tile => Region(
                regionByTile[tile],
                scopeByTile[tile],
                tile == activeTiles[1]
                    ? [new StableToken("detail.guard.pinned")]
                    : Array.Empty<StableToken>()))
            .Append(Region(unrelatedRegion, unrelatedScope, [DetailTransitionGuardV1.ActiveTransaction]))
            .ToArray();
        var stale = new DetailDirectoryV1(regions, Array.Empty<DetailTransitionCandidateV1>());

        var counts = Qa04CrossDomainTransactionDetailGuardReconstructionV1.ReconstructCountsFromTileScopes(
            [active, committed], stale, TileScope);
        Require(counts.Sum(static count => count.ActiveSubjectReferenceCount) == 3,
            "Detail guard counts must include only ACTIVE transaction subjects.");
        Require(counts.Count == 2 && counts.Any(static count => count.ActiveSubjectReferenceCount == 2) &&
                counts.Any(static count => count.ActiveSubjectReferenceCount == 1),
            "TileScope binding must aggregate subject references by authoritative detail region.");
        Require(counts.All(count => regionByTile[count.TileIndex] == count.DetailRegionId),
            "TileScope resolver must derive DetailRegionId only from DetailDirectory.SpatialScopeRef.");

        var guardedIds = counts.Select(static count => count.DetailRegionId).ToHashSet();
        var rebuilt = Qa04CrossDomainTransactionDetailGuardReconstructionV1.RebuildDirectoryGuardsFromTileScopes(
            stale, [active, committed], TileScope);
        Require(rebuilt.Regions.Where(region => guardedIds.Contains(region.DetailRegionId)).All(region =>
                region.ActiveGuards.Contains(DetailTransitionGuardV1.ActiveTransaction)),
            "Every region referenced by ACTIVE transaction subjects must receive the active-transaction guard.");
        Require(rebuilt.Regions.Single(region => region.DetailRegionId == unrelatedRegion).ActiveGuards.All(static guard =>
                guard != DetailTransitionGuardV1.ActiveTransaction),
            "Stale active-transaction guards must be removed from unreferenced regions.");
        Require(rebuilt.Regions.Any(region => region.ActiveGuards.Contains(new StableToken("detail.guard.pinned"))),
            "Reconstruction must preserve unrelated guards.");

        Qa04CrossDomainTransactionDetailGuardReconstructionV1.ValidateDirectoryMatchesTileScopeAuthority(
            rebuilt, [active, committed], TileScope);
        ExpectReject(
            () => Qa04CrossDomainTransactionDetailGuardReconstructionV1.ValidateDirectoryMatchesTileScopeAuthority(
                stale, [active, committed], TileScope),
            "Stale recovered detail guards must fail TileScope authority comparison.");

        ExpectReject(
            () => Qa04CrossDomainTransactionDetailGuardReconstructionV1.ReconstructCountsFromTileScopes(
                [active], stale, tile => new PartitionRecordRefV1("spatial.terrain_geometry", TileScope(tile).RecordId)),
            "Non-scope-registry refs must fail closed.");
        ExpectReject(
            () => Qa04CrossDomainTransactionDetailGuardReconstructionV1.ReconstructCountsFromTileScopes(
                [active], stale, static _ => new PartitionRecordRefV1(SpatialScopeRegistryPayloadV1.PartitionId, OpaqueId128.Zero)),
            "ZERO TileScope RecordId must fail closed.");
        ExpectReject(
            () => Qa04CrossDomainTransactionDetailGuardReconstructionV1.ReconstructCountsFromTileScopes(
                [active], stale, static _ => new PartitionRecordRefV1(
                    SpatialScopeRegistryPayloadV1.PartitionId,
                    OpaqueId128.Parse("0000000000000000000000000004eeee"))),
            "TileScope without a DetailDirectory region must fail closed.");

        var ambiguous = new DetailDirectoryV1(
            [
                Region(OpaqueId128.Parse("0000000000000000000000000004c001"), scopeByTile[activeTiles[0]], []),
                Region(OpaqueId128.Parse("0000000000000000000000000004c002"), scopeByTile[activeTiles[0]], []),
            ],
            Array.Empty<DetailTransitionCandidateV1>());
        ExpectReject(
            () => Qa04CrossDomainTransactionDetailGuardReconstructionV1.CreateRegionResolverFromTileScopes(
                ambiguous, TileScope),
            "Multiple detail regions owning one SpatialScopeRef must fail closed.");
    }

    private static (OpaqueId128 SameTileA, OpaqueId128 SameTileB, OpaqueId128 OtherTile) SubjectsAcrossTwoTiles()
    {
        var first = OpaqueId128.Parse("0000000000000000000000000004d001");
        var firstTile = Qa04ReferenceLoadV1.RegionalTileIndex(first);
        OpaqueId128 same = OpaqueId128.Zero;
        OpaqueId128 other = OpaqueId128.Zero;
        for (ulong ordinal = 0x4d002; ordinal < 0x5d002 && (same.IsZero || other.IsZero); ordinal++)
        {
            var candidate = OpaqueId128.Parse(ordinal.ToString("x32"));
            var tile = Qa04ReferenceLoadV1.RegionalTileIndex(candidate);
            if (tile == firstTile && same.IsZero) same = candidate;
            if (tile != firstTile && other.IsZero) other = candidate;
        }
        if (same.IsZero || other.IsZero)
            throw new InvalidOperationException("Unable to derive deterministic two-tile detail guard fixture.");
        return (first, same, other);
    }

    private static CrossDomainTransactionStateV1 State(
        string transactionId,
        TransactionLifecycleV1 lifecycle,
        IReadOnlyList<OpaqueId128> subjects)
    {
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var resident = StandardDomainExecutionPlanV1.Create().Entries.Single(entry => entry.DomainToken.Value == "resident");
        var participant = new PersistentTransactionParticipantV1(
            resident.DomainToken,
            resident.OwnedPartitions[0],
            [OpaqueId128.Parse("0000000000000000000000000004a100")],
            required: true,
            TransactionParticipantOutcomeV1.Ready,
            new byte[32],
            null);
        var invariant = new InvariantResultV1(
            CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind).Single(),
            InvariantSeverityV1.CommitBlocking,
            InvariantOutcomeV1.Pass,
            Array.Empty<CausalityRefV1>(),
            null);
        var terminal = lifecycle == TransactionLifecycleV1.Active ? (ulong?)null : 2;
        return new CrossDomainTransactionStateV1(
            OpaqueId128.Parse(transactionId),
            kind,
            lifecycle,
            createdStep: 1,
            updatedStep: terminal ?? 1,
            terminalStep: terminal,
            new CausalityRefV1(
                CausalityRefKindV1.Operation,
                OpaqueId128.Parse("0000000000000000000000000004a200").ToBytes(),
                0),
            subjects,
            [participant],
            [invariant]);
    }

    private static DetailRegionStateV1 Region(
        OpaqueId128 id,
        OpaqueId128 scopeId,
        IEnumerable<StableToken> guards)
        => new(
            id,
            scopeId,
            new Dictionary<StableToken, DetailLevelV1>
            {
                [new StableToken("resident")] = DetailLevelV1.D2RegionalAggregate,
            },
            lineageGeneration: 1,
            lastTransitionStep: 0,
            guards);

    private static void ExpectReject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
