using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

internal static class Qa04CompactOperationSnapshotSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        const ulong snapshotStep = 18_000;
        const ulong lastClosedInjectionStep = 17_998;
        var prefix = new Qa04OperationClosedPrefixV1(
            Qa04ReferenceLoadV1.BenchmarkProfileId,
            0,
            lastClosedInjectionStep,
            Qa04OperationClosedPrefixV1.ExpectedTerminalOperationCount(lastClosedInjectionStep),
            Enumerable.Repeat((byte)0x7a, 32).ToArray());
        var transactions = Enumerable.Range(0, checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount))
            .Select(CreateTransaction)
            .OrderBy(static state => state.TransactionId)
            .ToArray();

        var expected = Qa04OperationAuthorityV1.Canonicalize([], prefix, transactions, snapshotStep);
        var section = Qa04CompactOperationStateSnapshotSectionProviderV1.Create(
            snapshotStep,
            [],
            prefix,
            transactions);
        Require(section.SectionId == "core.operation-state" &&
                section.SectionSchema == CoreOperationStateSnapshotAuthorityV2.Schema,
            "QA-04 compact Snapshot must keep the canonical core.operation-state /2.0 section identity.");
        Require(section.LogicalItemCount == Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount + 1UL,
            "QA-04 compact Snapshot logical count must include one prefix certificate plus active transactions.");
        Require(section.LogicalContentDigest.SequenceEqual(expected.CanonicalDigest),
            "QA-04 compact Snapshot digest must equal the live Step2 Operation authority.");

        var verified = CoreOperationStateSnapshotSectionProviderV2.SemanticVerifier(snapshotStep)
            .Verify(section.Fragments);
        Require(verified.LogicalItemCount == section.LogicalItemCount &&
                verified.LogicalContentDigest.SequenceEqual(section.LogicalContentDigest),
            "The v2 semantic recovery boundary must recognize and rehash QA-04 compact material.");

        var recovered = Qa04CompactOperationStateSnapshotSectionProviderV1.Recover(section, snapshotStep);
        Require(recovered.MutableOperations.Count == 0 &&
                recovered.ActiveTransactions.Count == checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount) &&
                recovered.ClosedPrefix.LastClosedInjectionStep == lastClosedInjectionStep &&
                recovered.ClosedPrefix.TerminalOperationCount == prefix.TerminalOperationCount &&
                recovered.ClosedPrefix.TerminalSemanticDigest.SequenceEqual(prefix.TerminalSemanticDigest) &&
                recovered.LogicalContentDigest.SequenceEqual(expected.CanonicalDigest),
            "QA-04 compact Snapshot recovery did not reconstruct the closed-prefix semantic authority.");

        var tamperedDigest = section with
        {
            LogicalContentDigest = Enumerable.Repeat((byte)0x55, 32).ToArray(),
        };
        ExpectReject(
            () => Qa04CompactOperationStateSnapshotSectionProviderV1.Recover(tamperedDigest, snapshotStep),
            "QA-04 compact Snapshot recovery must reject semantic digest tamper.");

        var wrongEndpoint = prefix with
        {
            LastClosedInjectionStep = lastClosedInjectionStep - 1,
            TerminalOperationCount = Qa04OperationClosedPrefixV1.ExpectedTerminalOperationCount(lastClosedInjectionStep - 1),
        };
        ExpectReject(
            () => Qa04CompactOperationStateSnapshotSectionProviderV1.Create(snapshotStep, [], wrongEndpoint, transactions),
            "QA-04 compact Snapshot must reject a prefix endpoint incompatible with State numbering.");

        var wrongCount = prefix with { TerminalOperationCount = checked(prefix.TerminalOperationCount + 1UL) };
        ExpectReject(
            () => Qa04CompactOperationStateSnapshotSectionProviderV1.Create(snapshotStep, [], wrongCount, transactions),
            "QA-04 compact Snapshot must reject a closed-prefix terminal count gap.");

        ExpectReject(
            () => Qa04CompactOperationStateSnapshotSectionProviderV1.Create(
                snapshotStep,
                [],
                prefix,
                transactions.Take(transactions.Length - 1).ToArray()),
            "QA-04 compact Snapshot must reject an incomplete active CrossDomainTransaction set.");
    }

    private static CrossDomainTransactionStateV1 CreateTransaction(int ordinal)
    {
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var resident = StandardDomainExecutionPlanV1.Create().Entries.Single(entry => entry.DomainToken.Value == "resident");
        var participant = new PersistentTransactionParticipantV1(
            resident.DomainToken,
            resident.OwnedPartitions[0],
            [OpaqueId128.Parse("0000000000000000000000000007a101")],
            required: true,
            TransactionParticipantOutcomeV1.Ready,
            Enumerable.Repeat((byte)0x2a, 32).ToArray());
        var invariant = new InvariantResultV1(
            CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind).Single(),
            InvariantSeverityV1.CommitBlocking,
            InvariantOutcomeV1.Pass,
            [new CausalityRefV1(
                CausalityRefKindV1.Entity,
                OpaqueId128.Parse("0000000000000000000000000007a102").ToBytes(),
                0)]);
        var transactionId = OpaqueId128.Parse(checked((ulong)ordinal + 1UL).ToString("x32"));
        return new CrossDomainTransactionStateV1(
            transactionId,
            kind,
            TransactionLifecycleV1.Active,
            createdStep: 1,
            updatedStep: 1,
            terminalStep: null,
            new CausalityRefV1(
                CausalityRefKindV1.Operation,
                OpaqueId128.Parse("0000000000000000000000000007a200").ToBytes(),
                0),
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
