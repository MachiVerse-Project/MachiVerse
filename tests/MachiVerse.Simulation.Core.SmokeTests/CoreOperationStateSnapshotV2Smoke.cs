using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

internal static class CoreOperationStateSnapshotV2Smoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var state = CreateTransaction();
        var authority = CoreOperationStateSnapshotAuthorityV2.Create([], [state], basisStep: 2);
        Require(authority.Schema.Version.Major == 2 && authority.Schema.Version.Minor == 0,
            "core.operation-state v2 schema identity drifted.");
        Require(authority.LogicalItemCount == 1, "v2 logical item count must include transaction state.");

        var encoded = CoreOperationStateSnapshotWireCodecV2.Encode(2, [], [state]);
        var decoded = CoreOperationStateSnapshotWireCodecV2.Decode(encoded);
        Require(decoded.BasisStep == 2 && decoded.Operations.Count == 0 && decoded.Transactions.Count == 1,
            "v2 snapshot round trip shape mismatch.");
        var recovered = decoded.Transactions[0];
        Require(recovered.TransactionId == state.TransactionId && recovered.TransactionKind == state.TransactionKind &&
                recovered.Lifecycle == state.Lifecycle && recovered.CreatedStep == state.CreatedStep &&
                recovered.UpdatedStep == state.UpdatedStep && recovered.TerminalStep == state.TerminalStep,
            "v2 transaction scalar authority did not round trip.");
        Require(recovered.SubjectIds.SequenceEqual(state.SubjectIds) && recovered.Participants.Count == state.Participants.Count &&
                recovered.InvariantResults.Count == state.InvariantResults.Count,
            "v2 transaction nested authority did not round trip.");
        Require(recovered.CanonicalDigest().SequenceEqual(state.CanonicalDigest()),
            "v2 transaction semantic digest changed after round trip.");

        var recoveredAuthority = CoreOperationStateSnapshotAuthorityV2.Create(
            decoded.Operations, decoded.Transactions, decoded.BasisStep);
        Require(recoveredAuthority.CanonicalDigest.SequenceEqual(authority.CanonicalDigest),
            "v2 section semantic digest changed after round trip.");

        var malformed = encoded.Concat(new byte[] { 0x98, 0x06, 0x00 }).ToArray(); // field 99, wire 0
        ExpectReject(() => CoreOperationStateSnapshotWireCodecV2.Decode(malformed),
            "v2 unknown authoritative field must fail closed.");
        ExpectReject(() => CoreOperationStateSnapshotAuthorityV2.Create([], [state], basisStep: 0),
            "transaction updated after Snapshot basis Step must fail closed.");
    }

    private static CrossDomainTransactionStateV1 CreateTransaction()
    {
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var resident = StandardDomainExecutionPlanV1.Create().Entries.Single(entry => entry.DomainToken.Value == "resident");
        var participant = new PersistentTransactionParticipantV1(
            resident.DomainToken,
            resident.OwnedPartitions[0],
            [OpaqueId128.Parse("0000000000000000000000000006a101")],
            required: true,
            TransactionParticipantOutcomeV1.Ready,
            Enumerable.Repeat((byte)0x2a, 32).ToArray());
        var invariant = new InvariantResultV1(
            CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind).Single(),
            InvariantSeverityV1.CommitBlocking,
            InvariantOutcomeV1.Pass,
            [new CausalityRefV1(CausalityRefKindV1.Entity, OpaqueId128.Parse("0000000000000000000000000006a102").ToBytes(), 0)]);
        return new CrossDomainTransactionStateV1(
            OpaqueId128.Parse("0000000000000000000000000006a001"),
            kind,
            TransactionLifecycleV1.Active,
            createdStep: 1,
            updatedStep: 1,
            terminalStep: null,
            new CausalityRefV1(CausalityRefKindV1.Operation, OpaqueId128.Parse("0000000000000000000000000006a200").ToBytes(), 0),
            [OpaqueId128.Parse("0000000000000000000000000006a010")],
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
