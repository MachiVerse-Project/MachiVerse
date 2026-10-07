using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class CrossDomainTransactionSnapshotRecoveryV2Smoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var state = CreateTransaction();
        var section = CoreOperationStateSnapshotSectionProviderV2.Create(2, [], [state]);
        var tile = Qa04ReferenceLoadV1.RegionalTileIndex(state.SubjectIds.Single());
        var regionId = OpaqueId128.Parse("0000000000000000000000000007b001");
        OpaqueId128 Resolve(ushort requestedTile)
            => requestedTile == tile ? regionId : OpaqueId128.Parse((0x7c000UL + requestedTile).ToString("x32"));

        var region = new DetailRegionStateV1(
            regionId,
            OpaqueId128.Parse("0000000000000000000000000007b100"),
            new Dictionary<StableToken, DetailLevelV1>
            {
                [new StableToken("resident")] = DetailLevelV1.D2RegionalAggregate,
            },
            lineageGeneration: 1,
            lastTransitionStep: 0,
            Array.Empty<StableToken>());
        var staleDirectory = new DetailDirectoryV1([region], Array.Empty<DetailTransitionCandidateV1>());

        var recovered = CrossDomainTransactionSnapshotRecoveryV2.RecoverAndRebuildDetailGuards(
            section, 2, staleDirectory, Resolve);
        Require(recovered.OperationState.Transactions.Count == 1 &&
                recovered.OperationState.Transactions[0].CanonicalDigest().SequenceEqual(state.CanonicalDigest()),
            "Snapshot recovery must preserve persistent transaction semantic authority.");
        Require(recovered.DetailDirectory.Regions.Single().ActiveGuards.Contains(DetailTransitionGuardV1.ActiveTransaction),
            "Recovered ACTIVE transaction must rebuild detail.guard.active-transaction.");

        CrossDomainTransactionSnapshotRecoveryV2.ValidateRecoveredDetailMatchesTransactionAuthority(
            section, 2, recovered.DetailDirectory, Resolve);
        ExpectReject(
            () => CrossDomainTransactionSnapshotRecoveryV2.ValidateRecoveredDetailMatchesTransactionAuthority(
                section, 2, staleDirectory, Resolve),
            "Stale recovered detail guard state must fail against recovered transaction authority.");
    }

    private static CrossDomainTransactionStateV1 CreateTransaction()
    {
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var resident = StandardDomainExecutionPlanV1.Create().Entries.Single(entry => entry.DomainToken.Value == "resident");
        var participant = new PersistentTransactionParticipantV1(
            resident.DomainToken,
            resident.OwnedPartitions[0],
            [OpaqueId128.Parse("0000000000000000000000000007a101")],
            required: true,
            TransactionParticipantOutcomeV1.Ready,
            Enumerable.Repeat((byte)0x31, 32).ToArray());
        var invariant = new InvariantResultV1(
            CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind).Single(),
            InvariantSeverityV1.CommitBlocking,
            InvariantOutcomeV1.Pass);
        return new CrossDomainTransactionStateV1(
            OpaqueId128.Parse("0000000000000000000000000007a001"),
            kind,
            TransactionLifecycleV1.Active,
            1,
            1,
            null,
            new CausalityRefV1(CausalityRefKindV1.Operation, OpaqueId128.Parse("0000000000000000000000000007a200").ToBytes(), 0),
            [OpaqueId128.Parse("0000000000000000000000000007a010")],
            [participant],
            [invariant]);
    }

    private static void ExpectReject(Action action, string message)
    {
        try { action(); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
