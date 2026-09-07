using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

public enum TransactionParticipantOutcomeV1 : byte
{
    Ready = 1,
    Failed = 2,
}

public sealed class TransactionParticipantCandidateV1
{
    public TransactionParticipantCandidateV1(
        StableToken domainToken,
        TransactionParticipantOutcomeV1 outcome,
        ReadOnlySpan<byte> candidateEffectDigest,
        StableToken? diagnosticCode = null)
    {
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (candidateEffectDigest.Length != 32)
            throw new ArgumentException("Candidate effect digest must be 32 bytes.", nameof(candidateEffectDigest));
        if (!StandardDomainExecutionPlanV1.Create().Entries.Any(entry => entry.DomainToken == domainToken))
            throw new InvalidDataException("transaction.participant-domain-unregistered");

        DomainToken = domainToken;
        Outcome = outcome;
        CandidateEffectDigest = candidateEffectDigest.ToArray();
        DiagnosticCode = diagnosticCode;
    }

    public StableToken DomainToken { get; }
    public TransactionParticipantOutcomeV1 Outcome { get; }
    public byte[] CandidateEffectDigest { get; }
    public StableToken? DiagnosticCode { get; }
}

public sealed class CrossDomainTransactionCandidateV1
{
    internal CrossDomainTransactionCandidateV1(
        OpaqueId128 transactionId,
        StableToken transactionKind,
        ulong basisStep,
        CausalityRefV1 rootCausalityRef,
        IReadOnlyList<OpaqueId128> subjectRefs,
        IReadOnlyList<TransactionParticipantCandidateV1> participants,
        IReadOnlyList<InvariantResultV1> invariantResults,
        TransactionCandidateStatusV1 status,
        byte[] diagnosticDigest,
        StableToken? failureCode)
    {
        TransactionId = transactionId;
        TransactionKind = transactionKind;
        BasisStep = basisStep;
        RootCausalityRef = rootCausalityRef;
        SubjectRefs = subjectRefs;
        Participants = participants;
        InvariantResults = invariantResults;
        Status = status;
        DiagnosticDigest = diagnosticDigest;
        FailureCode = failureCode;
    }

    public OpaqueId128 TransactionId { get; }
    public StableToken TransactionKind { get; }
    public ulong BasisStep { get; }
    public CausalityRefV1 RootCausalityRef { get; }
    public IReadOnlyList<OpaqueId128> SubjectRefs { get; }
    public IReadOnlyList<TransactionParticipantCandidateV1> Participants { get; }
    public IReadOnlyList<InvariantResultV1> InvariantResults { get; }
    public TransactionCandidateStatusV1 Status { get; }
    public byte[] DiagnosticDigest { get; }
    public StableToken? FailureCode { get; }
    public bool CanFinalize => Status == TransactionCandidateStatusV1.Valid;
    public bool IsAuthoritative => false;
}

public static class CrossDomainTransactionAssemblerV1
{
    private static readonly StableToken ParticipantMissing = new("transaction.participant-missing");
    private static readonly StableToken RequiredParticipantFailed = new("transaction.required-participant-failed");
    private static readonly StableToken ParticipantFailed = new("transaction.participant-failed");
    private static readonly StableToken InvariantFailed = new("transaction.invariant-failed");

    public static CrossDomainTransactionCandidateV1 Assemble(
        OpaqueId128 worldId,
        StableToken transactionKind,
        ulong basisStep,
        CausalityRefV1 rootCausalityRef,
        IEnumerable<OpaqueId128> subjectRefs,
        ulong stableLocalOrdinal,
        IEnumerable<TransactionParticipantCandidateV1> participants)
    {
        ArgumentNullException.ThrowIfNull(rootCausalityRef);
        ArgumentNullException.ThrowIfNull(subjectRefs);
        ArgumentNullException.ThrowIfNull(participants);

        var registration = CrossDomainTransactionKindRegistryV1.GetRegistration(transactionKind);
        var rankByDomain = StandardDomainExecutionPlanV1.Create().Entries
            .ToDictionary(static entry => entry.DomainToken, static entry => entry.DomainRank);

        var subjects = subjectRefs.Order().ToArray();
        if (subjects.Any(static subject => subject.IsZero))
            throw new InvalidDataException("transaction.subject-id-zero");
        if (subjects.Distinct().Count() != subjects.Length)
            throw new InvalidDataException("transaction.subject-id-duplicate");

        var orderedParticipants = participants
            .Select(participant => participant ?? throw new ArgumentNullException(nameof(participants)))
            .OrderBy(participant => rankByDomain[participant.DomainToken])
            .ThenBy(static participant => participant.DomainToken.Value, StringComparer.Ordinal)
            .ToArray();
        if (orderedParticipants.Select(static participant => participant.DomainToken).Distinct().Count() != orderedParticipants.Length)
            throw new InvalidDataException("transaction.participant-duplicate");

        var allowedDomains = registration.RequiredDomains.Concat(registration.OptionalDomains).ToHashSet();
        if (orderedParticipants.Any(participant => !allowedDomains.Contains(participant.DomainToken)))
            throw new InvalidDataException("transaction.participant-domain-not-allowed");

        var transactionId = TransactionIdentityV1.Derive(
            worldId,
            transactionKind,
            basisStep,
            rootCausalityRef,
            subjects,
            stableLocalOrdinal);

        var participantDomains = orderedParticipants.Select(static participant => participant.DomainToken).ToHashSet();
        var missingRequired = registration.RequiredDomains.FirstOrDefault(domain => !participantDomains.Contains(domain));
        var failedRequired = orderedParticipants.FirstOrDefault(participant =>
            registration.RequiredDomains.Contains(participant.DomainToken) &&
            participant.Outcome == TransactionParticipantOutcomeV1.Failed);
        var failedOptional = orderedParticipants.FirstOrDefault(participant =>
            registration.OptionalDomains.Contains(participant.DomainToken) &&
            participant.Outcome == TransactionParticipantOutcomeV1.Failed);

        TransactionCandidateStatusV1 status;
        StableToken? failureCode;
        if (!EqualityComparer<StableToken>.Default.Equals(missingRequired, default))
        {
            status = TransactionCandidateStatusV1.Invalid;
            failureCode = ParticipantMissing;
        }
        else if (failedRequired is not null)
        {
            status = TransactionCandidateStatusV1.Invalid;
            failureCode = failedRequired.DiagnosticCode ?? RequiredParticipantFailed;
        }
        else if (failedOptional is not null)
        {
            status = TransactionCandidateStatusV1.Invalid;
            failureCode = failedOptional.DiagnosticCode ?? ParticipantFailed;
        }
        else
        {
            status = TransactionCandidateStatusV1.ReadyForValidation;
            failureCode = null;
        }

        return CreateCandidate(
            transactionId,
            transactionKind,
            basisStep,
            rootCausalityRef,
            subjects,
            orderedParticipants,
            Array.Empty<InvariantResultV1>(),
            status,
            failureCode);
    }

    public static CrossDomainTransactionCandidateV1 Validate(
        CrossDomainTransactionCandidateV1 candidate,
        IEnumerable<InvariantResultV1> invariantResults)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(invariantResults);
        if (candidate.Status != TransactionCandidateStatusV1.ReadyForValidation)
            throw new InvalidDataException("transaction.candidate-not-ready-for-validation");

        var invariants = invariantResults
            .Select(result => result ?? throw new ArgumentNullException(nameof(invariantResults)))
            .OrderBy(static result => result.InvariantId.Value, StringComparer.Ordinal)
            .ThenBy(static result => result.Severity)
            .ToArray();
        var decision = InvariantBarrierV1.Evaluate(invariants);
        var failed = invariants.FirstOrDefault(static result =>
            result.Outcome == InvariantOutcomeV1.Fail &&
            result.Severity is InvariantSeverityV1.CommitBlocking or InvariantSeverityV1.FatalAuthority);
        var status = decision.CanCommit
            ? TransactionCandidateStatusV1.Valid
            : TransactionCandidateStatusV1.Invalid;
        var failureCode = decision.CanCommit
            ? null
            : failed?.DiagnosticCode ?? InvariantFailed;

        return CreateCandidate(
            candidate.TransactionId,
            candidate.TransactionKind,
            candidate.BasisStep,
            candidate.RootCausalityRef,
            candidate.SubjectRefs,
            candidate.Participants,
            invariants,
            status,
            failureCode);
    }

    public static CrossDomainTransactionCandidateV1 AssembleAndValidate(
        OpaqueId128 worldId,
        StableToken transactionKind,
        ulong basisStep,
        CausalityRefV1 rootCausalityRef,
        IEnumerable<OpaqueId128> subjectRefs,
        ulong stableLocalOrdinal,
        IEnumerable<TransactionParticipantCandidateV1> participants,
        IEnumerable<InvariantResultV1> invariantResults)
    {
        var candidate = Assemble(
            worldId,
            transactionKind,
            basisStep,
            rootCausalityRef,
            subjectRefs,
            stableLocalOrdinal,
            participants);
        return candidate.Status == TransactionCandidateStatusV1.ReadyForValidation
            ? Validate(candidate, invariantResults)
            : candidate;
    }

    private static CrossDomainTransactionCandidateV1 CreateCandidate(
        OpaqueId128 transactionId,
        StableToken transactionKind,
        ulong basisStep,
        CausalityRefV1 rootCausalityRef,
        IReadOnlyList<OpaqueId128> subjects,
        IReadOnlyList<TransactionParticipantCandidateV1> participants,
        IReadOnlyList<InvariantResultV1> invariants,
        TransactionCandidateStatusV1 status,
        StableToken? failureCode)
    {
        var diagnosticDigest = ComputeDiagnostic(
            transactionId,
            transactionKind,
            basisStep,
            subjects,
            participants,
            invariants,
            status,
            failureCode);

        return new CrossDomainTransactionCandidateV1(
            transactionId,
            transactionKind,
            basisStep,
            rootCausalityRef,
            Array.AsReadOnly(subjects.ToArray()),
            Array.AsReadOnly(participants.ToArray()),
            Array.AsReadOnly(invariants.ToArray()),
            status,
            diagnosticDigest,
            failureCode);
    }

    private static byte[] ComputeDiagnostic(
        OpaqueId128 transactionId,
        StableToken transactionKind,
        ulong basisStep,
        IReadOnlyList<OpaqueId128> subjects,
        IReadOnlyList<TransactionParticipantCandidateV1> participants,
        IReadOnlyList<InvariantResultV1> invariants,
        TransactionCandidateStatusV1 status,
        StableToken? failureCode)
        => HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
        {
            writer.WriteMapStart(8);
            writer.WriteUnsigned(0); writer.WriteBytes(transactionId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteAsciiText(transactionKind.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(basisStep);
            writer.WriteUnsigned(3);
            writer.WriteArrayStart((ulong)subjects.Count);
            foreach (var subject in subjects)
                writer.WriteBytes(subject.ToBytes());
            writer.WriteUnsigned(4);
            writer.WriteArrayStart((ulong)participants.Count);
            foreach (var participant in participants)
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteAsciiText(participant.DomainToken.Value);
                writer.WriteUnsigned(1); writer.WriteUnsigned((uint)participant.Outcome);
                writer.WriteUnsigned(2); writer.WriteBytes(participant.CandidateEffectDigest);
                writer.WriteUnsigned(3);
                if (participant.DiagnosticCode is { } participantCode)
                {
                    writer.WriteArrayStart(1);
                    writer.WriteAsciiText(participantCode.Value);
                }
                else
                {
                    writer.WriteArrayStart(0);
                }
            }
            writer.WriteUnsigned(5);
            writer.WriteArrayStart((ulong)invariants.Count);
            foreach (var invariant in invariants)
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteAsciiText(invariant.InvariantId.Value);
                writer.WriteUnsigned(1); writer.WriteUnsigned((uint)invariant.Severity);
                writer.WriteUnsigned(2); writer.WriteUnsigned((uint)invariant.Outcome);
                writer.WriteUnsigned(3);
                if (invariant.DiagnosticCode is { } invariantCode)
                {
                    writer.WriteArrayStart(1);
                    writer.WriteAsciiText(invariantCode.Value);
                }
                else
                {
                    writer.WriteArrayStart(0);
                }
            }
            writer.WriteUnsigned(6); writer.WriteUnsigned((uint)status);
            writer.WriteUnsigned(7);
            if (failureCode is { } code)
            {
                writer.WriteArrayStart(1);
                writer.WriteAsciiText(code.Value);
            }
            else
            {
                writer.WriteArrayStart(0);
            }
        });
}
