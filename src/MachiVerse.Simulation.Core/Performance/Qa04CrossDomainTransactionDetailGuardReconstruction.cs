using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ActiveTransactionDetailGuardCountV1(
    ushort TileIndex,
    OpaqueId128 DetailRegionId,
    uint ActiveSubjectReferenceCount);

/// <summary>
/// Rebuilds detail.guard.active-transaction exclusively from authoritative ACTIVE transaction
/// states. The tile -> detail-region identity remains an external canonical Spatial/detail authority
/// input; this type never invents region ids or persists reference counts as a second authority.
/// </summary>
public static class Qa04CrossDomainTransactionDetailGuardReconstructionV1
{
    public static IReadOnlyList<Qa04ActiveTransactionDetailGuardCountV1> ReconstructCounts(
        IEnumerable<CrossDomainTransactionStateV1> transactionStates,
        Func<ushort, OpaqueId128> detailRegionForTile)
    {
        ArgumentNullException.ThrowIfNull(transactionStates);
        ArgumentNullException.ThrowIfNull(detailRegionForTile);

        var byRegion = new Dictionary<OpaqueId128, (ushort Tile, uint Count)>();
        var regionByTile = new Dictionary<ushort, OpaqueId128>();

        foreach (var transaction in transactionStates)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            if (!transaction.IsActive) continue;

            foreach (var subject in transaction.SubjectIds)
            {
                var tile = Qa04ReferenceLoadV1.RegionalTileIndex(subject);
                var regionId = ResolveRegion(tile, detailRegionForTile, regionByTile);
                if (byRegion.TryGetValue(regionId, out var current))
                {
                    if (current.Tile != tile)
                        throw new InvalidDataException("qa04.transaction.detail-guard-region-alias");
                    byRegion[regionId] = (tile, checked(current.Count + 1));
                }
                else
                {
                    byRegion.Add(regionId, (tile, 1));
                }
            }
        }

        return Array.AsReadOnly(byRegion
            .Select(static pair => new Qa04ActiveTransactionDetailGuardCountV1(
                pair.Value.Tile,
                pair.Key,
                pair.Value.Count))
            .OrderBy(static value => value.TileIndex)
            .ThenBy(static value => value.DetailRegionId)
            .ToArray());
    }

    public static DetailDirectoryV1 RebuildDirectoryGuards(
        DetailDirectoryV1 recoveredDirectory,
        IEnumerable<CrossDomainTransactionStateV1> transactionStates,
        Func<ushort, OpaqueId128> detailRegionForTile)
    {
        ArgumentNullException.ThrowIfNull(recoveredDirectory);
        var counts = ReconstructCounts(transactionStates, detailRegionForTile);
        var guarded = counts.Select(static value => value.DetailRegionId).ToHashSet();
        var existing = recoveredDirectory.Regions.Select(static region => region.DetailRegionId).ToHashSet();

        var missing = guarded.Where(regionId => !existing.Contains(regionId)).Order().FirstOrDefault();
        if (!missing.IsZero)
            throw new InvalidDataException("qa04.transaction.detail-guard-region-missing");

        var rebuilt = recoveredDirectory.Regions.Select(region =>
        {
            var guards = region.ActiveGuards
                .Where(static guard => guard != DetailTransitionGuardV1.ActiveTransaction)
                .Concat(guarded.Contains(region.DetailRegionId)
                    ? [DetailTransitionGuardV1.ActiveTransaction]
                    : Array.Empty<StableToken>());
            return new DetailRegionStateV1(
                region.DetailRegionId,
                region.SpatialScopeRef,
                region.LevelByDomain,
                region.LineageGeneration,
                region.LastTransitionStep,
                guards);
        }).ToArray();

        return new DetailDirectoryV1(rebuilt, recoveredDirectory.PendingTransitions);
    }

    public static void ValidateDirectoryMatchesAuthority(
        DetailDirectoryV1 recoveredDirectory,
        IEnumerable<CrossDomainTransactionStateV1> transactionStates,
        Func<ushort, OpaqueId128> detailRegionForTile)
    {
        ArgumentNullException.ThrowIfNull(recoveredDirectory);
        var expected = RebuildDirectoryGuards(recoveredDirectory, transactionStates, detailRegionForTile);
        var expectedById = expected.Regions.ToDictionary(static region => region.DetailRegionId);
        foreach (var actual in recoveredDirectory.Regions)
        {
            var expectedRegion = expectedById[actual.DetailRegionId];
            if (!actual.ActiveGuards.SequenceEqual(expectedRegion.ActiveGuards))
                throw new InvalidDataException("qa04.transaction.detail-guard-recovery-mismatch");
        }
    }

    private static OpaqueId128 ResolveRegion(
        ushort tile,
        Func<ushort, OpaqueId128> detailRegionForTile,
        IDictionary<ushort, OpaqueId128> regionByTile)
    {
        if (regionByTile.TryGetValue(tile, out var existing)) return existing;
        var regionId = detailRegionForTile(tile);
        if (regionId.IsZero)
            throw new InvalidDataException("qa04.transaction.detail-guard-region-zero");
        if (regionByTile.Any(pair => pair.Key != tile && pair.Value == regionId))
            throw new InvalidDataException("qa04.transaction.detail-guard-region-alias");
        regionByTile.Add(tile, regionId);
        return regionId;
    }
}
