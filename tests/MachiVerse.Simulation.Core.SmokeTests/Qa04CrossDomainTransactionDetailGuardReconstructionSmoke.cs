using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04CrossDomainTransactionDetailGuardReconstructionSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var active = State(
            "0000000000000000000000000004a001",
            TransactionLifecycleV1.Active,
            [
                OpaqueId128.Parse("0011000000000000000000000004a010"),
                OpaqueId128.Parse("0011000000000000000000000004a011"),
                OpaqueId128.Parse("0022000000000000000000000004a012")
            ]);
        var committed = State(
            "0000000000000000000000000004a002",
            TransactionLifecycleV1.Committed,
            [OpaqueId128.Parse("0033000000000000000000000004a013")]);

        var regionByTile = new Dictionary<ushort, OpaqueId128>();
        OpaqueId128 Resolve(ushort tile)
        {
            if (!regionByTile.TryGetValue(tile, out var id))
            {
                id = OpaqueId128.Parse((0x4b000UL + tile).ToString("x32"));
                regionByTile.Add(tile, id);
            }
            return id;
        }

        var counts = Qa04CrossDomainTransactionDetailGuardReconstructionV1.ReconstructCounts(
            [active, committed], Resolve);
        Require(counts.Sum(static count => count.ActiveSubjectReferenceCount) == 3,
            "Detail guard counts must include only ACTIVE transaction subjects.");
        Require(counts.Count == 2 && counts.Any(static count => count.ActiveSubjectReferenceCount == 2) &&
                counts.Any(static count => count.ActiveSubjectReferenceCount == 1),
            "Detail guard reconstruction must aggregate subject references by canonical tile region.");

        var guardedIds = counts.Select(static count => count.DetailRegionId).ToHashSet();
        var unrelatedRegion = OpaqueId128.Parse("0000000000000000000000000004bfff");
        var regions = counts.Select(count => Region(
                count.DetailRegionId,
                guards: count.ActiveSubjectReferenceCount == 1
                    ? [new StableToken("detail.guard.pinned")]
                    : Array.Empty<StableToken>()))
            .Append(Region(unrelatedRegion, [DetailTransitionGuardV1.ActiveTransaction]))
            .ToArray();
        var stale = new DetailDirectoryV1(regions, Array.Empty<DetailTransitionCandidateV1>());
        var rebuilt = Qa04CrossDomainTransactionDetailGuardReconstructionV1.RebuildDirectoryGuards(
            stale, [active, committed], Resolve);

        Require(rebuilt.Regions.Where(region => guardedIds.Contains(region.DetailRegionId)).All(region =>
                region.ActiveGuards.Contains(DetailTransitionGuardV1.ActiveTransaction)),
            "Every region referenced by ACTIVE transaction subjects must receive the active-transaction guard.");
        Require(rebuilt.Regions.Single(region => region.DetailRegionId == unrelatedRegion).ActiveGuards.All(static guard =>
                guard != DetailTransitionGuardV1.ActiveTransaction),
            "Stale active-transaction guards must be removed from unreferenced regions.");
        Require(rebuilt.Regions.Any(region => region.ActiveGuards.Contains(new StableToken("detail.guard.pinned"))),
            "Reconstruction must preserve unrelated guards.");

        Qa04CrossDomainTransactionDetailGuardReconstructionV1.ValidateDirectoryMatchesAuthority(
            rebuilt, [active, committed], Resolve);
        ExpectReject(
            () => Qa04CrossDomainTransactionDetailGuardReconstructionV1.ValidateDirectoryMatchesAuthority(
                stale, [active, committed], Resolve),
            "Stale recovered detail guards must fail authority comparison.");

        ExpectReject(
            () => Qa04CrossDomainTransactionDetailGuardReconstructionV1.ReconstructCounts([active], static _ => OpaqueId128.Zero),
            "Zero detail-region resolver output must fail closed.");
        ExpectReject(
            () => Qa04CrossDomainTransactionDetailGuardReconstructionV1.ReconstructCounts([active], static _ =>
                OpaqueId128.Parse("0000000000000000000000000004c001")),
            "Distinct tiles must not alias to one detail-region id.");

        var incompleteDirectory = new DetailDirectoryV1(
            rebuilt.Regions.Where(region => region.DetailRegionId != guardedIds.First()).ToArray(),
            Array.Empty<DetailTransitionCandidateV1>());
        ExpectReject(
            () => Qa04CrossDomainTransactionDetailGuardReconstructionV1.RebuildDirectoryGuards(
                incompleteDirectory, [active], Resolve),
            "Missing authoritative guarded detail region must fail closed.");
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

    private static DetailRegionStateV1 Region(OpaqueId128 id, IEnumerable<StableToken> guards)
        => new(
            id,
            OpaqueId128.Parse((id == OpaqueId128.Zero ? 1UL : 0x4d000UL).ToString("x32")),
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
