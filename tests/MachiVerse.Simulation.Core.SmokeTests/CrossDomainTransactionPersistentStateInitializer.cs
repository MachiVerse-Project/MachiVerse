using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

internal static class CrossDomainTransactionPersistentStateInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        const ulong basisStep = 42;
        var worldId = Id("00000000000000000000000000047001");
        var root = new CausalityRefV1(
            CausalityRefKindV1.Operation,
            Id("00000000000000000000000000047002").ToBytes(),
            basisStep);
        var subject = Id("00000000000000000000000000047003");
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var residentEntry = StandardDomainExecutionPlanV1.Create().Entries
            .Single(entry => entry.DomainToken.Value == "resident");
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes("persistent-birth-participant"));
        var participant = new TransactionParticipantCandidateV1(
            residentEntry.DomainToken,
            residentEntry.OwnedPartitions[0],
            [Id("00000000000000000000000000047004")],
            required: true,
            TransactionParticipantOutcomeV1.Ready,
            digest);
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

        Require(candidate.Status == TransactionCandidateStatusV1.Valid && candidate.CanFinalize && !candidate.IsAuthoritative,
            "VALID candidate must remain non-authoritative before durable transition binding.");

        var active = CrossDomainTransactionStateV1.FromValidCandidate(candidate, basisStep + 1);
        Require(active.TransactionId == candidate.TransactionId && active.TransactionKind == kind &&
                active.Lifecycle == TransactionLifecycleV1.Active && active.CreatedStep == basisStep + 1 &&
                active.UpdatedStep == basisStep + 1 && active.TerminalStep is null && active.IsActive,
            "VALID candidate must materialize exact ACTIVE logical state at resulting Step.");
        Require(active.SubjectIds.SequenceEqual(candidate.SubjectRefs) && active.Participants.Count == 1 &&
                active.Participants[0].DomainToken.Value == "resident" &&
                active.Participants[0].CandidateEffectDigest.SequenceEqual(digest),
            "Persistent ACTIVE state must preserve candidate subjects/participant effect material.");
        var activeDigest = active.CanonicalDigest();
        Require(activeDigest.Length == 32 && activeDigest.SequenceEqual(active.CanonicalDigest()),
            "Persistent transaction canonical digest must be stable and 32 bytes.");

        var committed = active.Commit(basisStep + 2);
        Require(!committed.IsActive && committed.Lifecycle == TransactionLifecycleV1.Committed &&
                committed.CreatedStep == active.CreatedStep && committed.UpdatedStep == basisStep + 2 &&
                committed.TerminalStep == basisStep + 2 && committed.TransactionId == active.TransactionId,
            "ACTIVE -> COMMITTED must preserve immutable identity and set terminal Step.");
        ExpectReject(() => committed.Commit(basisStep + 3), "terminal state must not be terminalized twice");
        ExpectReject(() => active.Commit(active.UpdatedStep), "terminalization must advance updated Step");
        ExpectReject(
            () => _ = new CrossDomainTransactionStateSetV1([committed, active]),
            "duplicate transaction id must fail closed");

        var invalidCandidate = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
            worldId,
            kind,
            basisStep,
            root,
            [subject],
            stableLocalOrdinal: 1,
            [participant],
            Array.Empty<InvariantResultV1>());
        Require(invalidCandidate.Status == TransactionCandidateStatusV1.Invalid,
            "Missing invariant fixture must produce INVALID candidate.");
        ExpectReject(
            () => CrossDomainTransactionStateV1.FromValidCandidate(invalidCandidate, basisStep + 1),
            "INVALID candidate must never materialize persistent authority");

        var singleSet = new CrossDomainTransactionStateSetV1([active]);
        Require(singleSet.CanonicalStates.Count == 1 && singleSet.ActiveStates.Count == 1 &&
                singleSet.TryGet(active.TransactionId, out var recovered) && ReferenceEquals(recovered, active),
            "Persistent transaction state set must expose deterministic active authority lookup.");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

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
