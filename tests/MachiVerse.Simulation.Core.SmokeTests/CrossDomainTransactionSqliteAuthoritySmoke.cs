using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

internal static class CrossDomainTransactionSqliteAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "machiverse-cross-domain-tx-sqlite-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = CreateActiveState();
            var paths = PersistenceLayout.Resolve(rootPath, OpaqueId128.Parse("00000000000000000000000000047a01"), 1);
            await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths);
            Require(await store.HasTableAsync("cross_domain_transaction_state"), "Transaction authority table missing.");

            var activeWire = Encoding.ASCII.GetBytes("fixture.cross-domain-transaction.active.v1");
            var persisted = await store.PersistCrossDomainTransactionStateAsync(state, activeWire);
            Require(persisted.TransactionId == state.TransactionId && persisted.Lifecycle == TransactionLifecycleV1.Active &&
                    persisted.CreatedStep == state.CreatedStep && persisted.UpdatedStep == state.UpdatedStep &&
                    persisted.TerminalStep is null && persisted.StateWire.SequenceEqual(activeWire) &&
                    persisted.StateDigest.SequenceEqual(state.CanonicalDigest()),
                "Initial ACTIVE SQLite transaction authority mismatch.");

            var retry = await store.PersistCrossDomainTransactionStateAsync(state, activeWire);
            Require(retry == persisted || (retry.TransactionId == persisted.TransactionId && retry.StateDigest.SequenceEqual(persisted.StateDigest)),
                "Exact SQLite transaction retry must be idempotent.");

            var activeRows = await store.ListActiveCrossDomainTransactionStatesCanonicalAsync();
            Require(activeRows.Count == 1 && activeRows[0].TransactionId == state.TransactionId,
                "ACTIVE transaction catalog must expose committed SQLite authority.");

            var committed = state.Commit(state.UpdatedStep + 1);
            var terminalWire = Encoding.ASCII.GetBytes("fixture.cross-domain-transaction.committed.v1");
            var terminal = await store.PersistCrossDomainTransactionStateAsync(committed, terminalWire);
            Require(terminal.Lifecycle == TransactionLifecycleV1.Committed && terminal.TerminalStep == committed.TerminalStep &&
                    terminal.StateDigest.SequenceEqual(committed.CanonicalDigest()),
                "ACTIVE -> COMMITTED SQLite transition mismatch.");
            Require((await store.ListActiveCrossDomainTransactionStatesCanonicalAsync()).Count == 0,
                "Terminal transaction must leave ACTIVE catalog.");

            var recovered = await store.ReadCrossDomainTransactionStateAsync(state.TransactionId);
            Require(recovered is not null && recovered.Lifecycle == TransactionLifecycleV1.Committed &&
                    recovered.StateWire.SequenceEqual(terminalWire) && recovered.StateDigest.SequenceEqual(committed.CanonicalDigest()),
                "Committed transaction row did not round-trip from SQLite.");

            await ExpectRejectAsync(
                () => store.PersistCrossDomainTransactionStateAsync(state, activeWire),
                "Terminal transaction authority must reject lifecycle regression.");
            await ExpectRejectAsync(
                () => store.PersistCrossDomainTransactionStateAsync(committed, Encoding.ASCII.GetBytes("different-wire")),
                "Terminal transaction authority must reject non-identical retry.");
        }
        finally
        {
            if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        }
    }

    private static CrossDomainTransactionStateV1 CreateActiveState()
    {
        const ulong basisStep = 73;
        var worldId = OpaqueId128.Parse("00000000000000000000000000047a02");
        var root = new CausalityRefV1(
            CausalityRefKindV1.Operation,
            OpaqueId128.Parse("00000000000000000000000000047a03").ToBytes(),
            basisStep);
        var subject = OpaqueId128.Parse("00000000000000000000000000047a04");
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var residentEntry = StandardDomainExecutionPlanV1.Create().Entries.Single(entry => entry.DomainToken.Value == "resident");
        var participant = new TransactionParticipantCandidateV1(
            residentEntry.DomainToken,
            residentEntry.OwnedPartitions[0],
            [OpaqueId128.Parse("00000000000000000000000000047a05")],
            required: true,
            TransactionParticipantOutcomeV1.Ready,
            SHA256.HashData(Encoding.ASCII.GetBytes("sqlite-cross-domain-authority")));
        var invariant = new InvariantResultV1(
            CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind).Single(),
            InvariantSeverityV1.CommitBlocking,
            InvariantOutcomeV1.Pass,
            Array.Empty<CausalityRefV1>(),
            null);
        var candidate = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
            worldId,
            kind,
            basisStep,
            root,
            [subject],
            stableLocalOrdinal: 0,
            [participant],
            [invariant]);
        if (!candidate.CanFinalize) throw new InvalidOperationException("SQLite authority fixture candidate is invalid.");
        return CrossDomainTransactionStateV1.FromValidCandidate(candidate, basisStep + 1);
    }

    private static async Task ExpectRejectAsync(Func<Task> action, string message)
    {
        try
        {
            await action();
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
