using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

public enum TransactionLifecycleV1 : byte
{
    Active = 1,
    Committed = 2,
    Aborted = 3,
}

public sealed class PersistentTransactionParticipantV1
{
    private static readonly IReadOnlyDictionary<StableToken, ushort> DomainRankByDomain =
        StandardDomainExecutionPlanV1.Create().Entries.ToDictionary(
            static entry => entry.DomainToken,
            static entry => entry.DomainRank);

    public PersistentTransactionParticipantV1(
        StableToken domainToken,
        StableToken partitionId,
        IEnumerable<OpaqueId128> intentIds,
        bool required,
        TransactionParticipantOutcomeV1 outcome,
        ReadOnlySpan<byte> candidateEffectDigest,
        StableToken? diagnosticCode = null)
    {
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (candidateEffectDigest.Length != 32)
            throw new ArgumentException("Candidate effect digest must be 32 bytes.", nameof(candidateEffectDigest));
        ArgumentNullException.ThrowIfNull(intentIds);

        if (!DomainRankByDomain.ContainsKey(domainToken))
            throw new InvalidDataException("transaction.persistent-participant-domain-unregistered");
        var partition = StandardDomainPartitionRegistry.Get(partitionId.Value);
        if (partition.OwnerDomain != domainToken)
            throw new InvalidDataException("transaction.persistent-participant-partition-owner-mismatch");

        var orderedIntentIds = intentIds.Order().ToArray();
        if (orderedIntentIds.Any(static value => value.IsZero))
            throw new InvalidDataException("transaction.persistent-participant-intent-zero");
        if (orderedIntentIds.Distinct().Count() != orderedIntentIds.Length)
            throw new InvalidDataException("transaction.persistent-participant-intent-duplicate");

        DomainToken = domainToken;
        PartitionId = partitionId;
        IntentIds = Array.AsReadOnly(orderedIntentIds);
        Required = required;
        Outcome = outcome;
        CandidateEffectDigest = candidateEffectDigest.ToArray();
        DiagnosticCode = diagnosticCode;
    }

    public StableToken DomainToken { get; }
    public StableToken PartitionId { get; }
    public IReadOnlyList<OpaqueId128> IntentIds { get; }
    public bool Required { get; }
    public TransactionParticipantOutcomeV1 Outcome { get; }
    public byte[] CandidateEffectDigest { get; }
    public StableToken? DiagnosticCode { get; }

    internal static ushort DomainRank(StableToken domainToken)
        => DomainRankByDomain.TryGetValue(domainToken, out var rank)
            ? rank
            : throw new InvalidDataException("transaction.persistent-participant-domain-unregistered");

    internal static PersistentTransactionParticipantV1 FromCandidate(TransactionParticipantCandidateV1 candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new PersistentTransactionParticipantV1(
            candidate.DomainToken,
            candidate.PartitionId,
            candidate.IntentIds,
            candidate.Required,
            candidate.Outcome,
            candidate.CandidateEffectDigest,
            candidate.DiagnosticCode);
    }
}

/// <summary>
/// Authoritative logical CrossDomainTransaction state. Unlike CrossDomainTransactionCandidateV1,
/// this type is eligible for durable Core effect-custody persistence once committed by the durable
/// transition boundary.
/// </summary>
public sealed class CrossDomainTransactionStateV1
{
    public CrossDomainTransactionStateV1(
        OpaqueId128 transactionId,
        StableToken transactionKind,
        TransactionLifecycleV1 lifecycle,
        ulong createdStep,
        ulong updatedStep,
        ulong? terminalStep,
        CausalityRefV1 rootCausality,
        IEnumerable<OpaqueId128> subjectIds,
        IEnumerable<PersistentTransactionParticipantV1> participants,
        IEnumerable<InvariantResultV1> invariantResults)
    {
        if (transactionId.IsZero) throw new ArgumentException("Transaction id ZERO is invalid.", nameof(transactionId));
        if (!CrossDomainTransactionKindRegistryV1.Contains(transactionKind))
            throw new InvalidDataException("transaction.persistent-kind-unregistered");
        if (!Enum.IsDefined(lifecycle)) throw new ArgumentOutOfRangeException(nameof(lifecycle));
        if (updatedStep < createdStep)
            throw new InvalidDataException("transaction.persistent-updated-before-created");
        if (lifecycle == TransactionLifecycleV1.Active && terminalStep is not null)
            throw new InvalidDataException("transaction.persistent-active-terminal-step");
        if (lifecycle != TransactionLifecycleV1.Active && terminalStep is null)
            throw new InvalidDataException("transaction.persistent-terminal-step-missing");
        if (terminalStep is { } terminal && (terminal < createdStep || terminal != updatedStep))
            throw new InvalidDataException("transaction.persistent-terminal-step-invalid");
        ArgumentNullException.ThrowIfNull(rootCausality);
        ArgumentNullException.ThrowIfNull(subjectIds);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(invariantResults);

        var orderedSubjects = subjectIds.Order().ToArray();
        if (orderedSubjects.Any(static value => value.IsZero))
            throw new InvalidDataException("transaction.persistent-subject-zero");
        if (orderedSubjects.Distinct().Count() != orderedSubjects.Length)
            throw new InvalidDataException("transaction.persistent-subject-duplicate");

        var orderedParticipants = participants
            .Select(static participant => participant ?? throw new ArgumentNullException(nameof(participants)))
            .OrderBy(participant => PersistentTransactionParticipantV1.DomainRank(participant.DomainToken))
            .ThenBy(static participant => participant.DomainToken.Value, StringComparer.Ordinal)
            .ThenBy(static participant => participant.PartitionId.Value, StringComparer.Ordinal)
            .ToArray();
        if (orderedParticipants.Select(static participant => (participant.DomainToken, participant.PartitionId)).Distinct().Count() != orderedParticipants.Length)
            throw new InvalidDataException("transaction.persistent-participant-duplicate");
        ValidateParticipantContract(transactionKind, orderedParticipants);

        var orderedInvariants = invariantResults
            .Select(static result => result ?? throw new ArgumentNullException(nameof(invariantResults)))
            .OrderBy(static result => result.InvariantId.Value, StringComparer.Ordinal)
            .ThenBy(static result => result.Severity)
            .ToArray();

        TransactionId = transactionId;
        TransactionKind = transactionKind;
        Lifecycle = lifecycle;
        CreatedStep = createdStep;
        UpdatedStep = updatedStep;
        TerminalStep = terminalStep;
        RootCausality = new CausalityRefV1(rootCausality.Kind, rootCausality.Id, rootCausality.BasisStep);
        SubjectIds = Array.AsReadOnly(orderedSubjects);
        Participants = Array.AsReadOnly(orderedParticipants);
        InvariantResults = Array.AsReadOnly(orderedInvariants);
    }

    public OpaqueId128 TransactionId { get; }
    public StableToken TransactionKind { get; }
    public TransactionLifecycleV1 Lifecycle { get; }
    public ulong CreatedStep { get; }
    public ulong UpdatedStep { get; }
    public ulong? TerminalStep { get; }
    public CausalityRefV1 RootCausality { get; }
    public IReadOnlyList<OpaqueId128> SubjectIds { get; }
    public IReadOnlyList<PersistentTransactionParticipantV1> Participants { get; }
    public IReadOnlyList<InvariantResultV1> InvariantResults { get; }
    public bool IsActive => Lifecycle == TransactionLifecycleV1.Active;

    public static CrossDomainTransactionStateV1 FromValidCandidate(
        CrossDomainTransactionCandidateV1 candidate,
        ulong resultingStep)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.CanFinalize || candidate.Status != TransactionCandidateStatusV1.Valid)
            throw new InvalidDataException("transaction.persistent-candidate-not-valid");
        if (resultingStep != checked(candidate.BasisStep + 1))
            throw new InvalidDataException("transaction.persistent-candidate-resulting-step");

        return new CrossDomainTransactionStateV1(
            candidate.TransactionId,
            candidate.TransactionKind,
            TransactionLifecycleV1.Active,
            resultingStep,
            resultingStep,
            null,
            candidate.RootCausalityRef,
            candidate.SubjectRefs,
            candidate.Participants.Select(PersistentTransactionParticipantV1.FromCandidate),
            candidate.InvariantResults);
    }

    public CrossDomainTransactionStateV1 RefreshFromValidCandidate(
        CrossDomainTransactionCandidateV1 candidate,
        ulong resultingStep)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (Lifecycle != TransactionLifecycleV1.Active)
            throw new InvalidDataException("transaction.persistent-refresh-terminal");
        if (!candidate.CanFinalize || candidate.Status != TransactionCandidateStatusV1.Valid)
            throw new InvalidDataException("transaction.persistent-refresh-candidate-not-valid");
        if (candidate.TransactionId != TransactionId || candidate.TransactionKind != TransactionKind ||
            candidate.BasisStep + 1 != resultingStep || resultingStep <= UpdatedStep)
            throw new InvalidDataException("transaction.persistent-refresh-identity-or-step");
        if (!candidate.SubjectRefs.SequenceEqual(SubjectIds) || !SameCausality(candidate.RootCausalityRef, RootCausality))
            throw new InvalidDataException("transaction.persistent-refresh-immutable-material");

        return new CrossDomainTransactionStateV1(
            TransactionId,
            TransactionKind,
            TransactionLifecycleV1.Active,
            CreatedStep,
            resultingStep,
            null,
            RootCausality,
            SubjectIds,
            candidate.Participants.Select(PersistentTransactionParticipantV1.FromCandidate),
            candidate.InvariantResults);
    }

    public CrossDomainTransactionStateV1 Commit(ulong terminalStep)
        => Terminalize(TransactionLifecycleV1.Committed, terminalStep);

    public CrossDomainTransactionStateV1 Abort(ulong terminalStep)
        => Terminalize(TransactionLifecycleV1.Aborted, terminalStep);

    private CrossDomainTransactionStateV1 Terminalize(TransactionLifecycleV1 target, ulong terminalStep)
    {
        if (Lifecycle != TransactionLifecycleV1.Active)
            throw new InvalidDataException("transaction.persistent-terminal-transition-invalid");
        if (terminalStep <= UpdatedStep)
            throw new InvalidDataException("transaction.persistent-terminal-step-not-forward");
        return new CrossDomainTransactionStateV1(
            TransactionId,
            TransactionKind,
            target,
            CreatedStep,
            terminalStep,
            terminalStep,
            RootCausality,
            SubjectIds,
            Participants,
            InvariantResults);
    }

    public byte[] CanonicalDigest()
        => HashSuite.DomainHash("mv.cross-domain-transaction-state.v1", writer =>
        {
            writer.WriteArrayStart(10);
            writer.WriteBytes(TransactionId.ToBytes());
            writer.WriteAsciiText(TransactionKind.Value);
            writer.WriteUnsigned((uint)Lifecycle);
            writer.WriteUnsigned(CreatedStep);
            writer.WriteUnsigned(UpdatedStep);
            WriteOptionalUnsigned(writer, TerminalStep);
            WriteCausality(writer, RootCausality);
            writer.WriteArrayStart((ulong)SubjectIds.Count);
            foreach (var subject in SubjectIds) writer.WriteBytes(subject.ToBytes());
            writer.WriteArrayStart((ulong)Participants.Count);
            foreach (var participant in Participants) WriteParticipant(writer, participant);
            writer.WriteArrayStart((ulong)InvariantResults.Count);
            foreach (var invariant in InvariantResults) WriteInvariant(writer, invariant);
        });

    private static void ValidateParticipantContract(
        StableToken transactionKind,
        IReadOnlyList<PersistentTransactionParticipantV1> participants)
    {
        var registration = CrossDomainTransactionKindRegistryV1.GetRegistration(transactionKind);
        var conditional = registration.RequiredAnyDomainGroups.SelectMany(static group => group).ToHashSet();
        var allowed = registration.RequiredDomains.Concat(registration.OptionalDomains).Concat(conditional).ToHashSet();
        if (participants.Any(participant => !allowed.Contains(participant.DomainToken)))
            throw new InvalidDataException("transaction.persistent-participant-domain-not-allowed");
        foreach (var participant in participants)
        {
            var expectedRequired = registration.RequiredDomains.Contains(participant.DomainToken) || conditional.Contains(participant.DomainToken);
            if (participant.Required != expectedRequired)
                throw new InvalidDataException("transaction.persistent-participant-requiredness-mismatch");
        }

        var present = participants.Select(static participant => participant.DomainToken).ToHashSet();
        if (registration.RequiredDomains.Any(domain => !present.Contains(domain)) ||
            registration.RequiredAnyDomainGroups.Any(group => !group.Any(present.Contains)))
            throw new InvalidDataException("transaction.persistent-participant-required-missing");
    }

    private static bool SameCausality(CausalityRefV1 left, CausalityRefV1 right)
        => left.Kind == right.Kind && left.BasisStep == right.BasisStep && left.Id.AsSpan().SequenceEqual(right.Id);

    private static void WriteCausality(MvDcborWriter writer, CausalityRefV1 value)
    {
        writer.WriteArrayStart(3);
        writer.WriteUnsigned((uint)value.Kind);
        writer.WriteBytes(value.Id);
        WriteOptionalUnsigned(writer, value.BasisStep);
    }

    private static void WriteParticipant(MvDcborWriter writer, PersistentTransactionParticipantV1 value)
    {
        writer.WriteArrayStart(7);
        writer.WriteAsciiText(value.DomainToken.Value);
        writer.WriteAsciiText(value.PartitionId.Value);
        writer.WriteArrayStart((ulong)value.IntentIds.Count);
        foreach (var intent in value.IntentIds) writer.WriteBytes(intent.ToBytes());
        writer.WriteBoolean(value.Required);
        writer.WriteUnsigned((uint)value.Outcome);
        writer.WriteBytes(value.CandidateEffectDigest);
        if (value.DiagnosticCode is { } code)
        {
            writer.WriteArrayStart(1);
            writer.WriteAsciiText(code.Value);
        }
        else writer.WriteArrayStart(0);
    }

    private static void WriteInvariant(MvDcborWriter writer, InvariantResultV1 value)
    {
        writer.WriteArrayStart(5);
        writer.WriteAsciiText(value.InvariantId.Value);
        writer.WriteUnsigned((uint)value.Severity);
        writer.WriteUnsigned((uint)value.Outcome);
        if (value.DiagnosticCode is { } code)
        {
            writer.WriteArrayStart(1);
            writer.WriteAsciiText(code.Value);
        }
        else writer.WriteArrayStart(0);
        writer.WriteArrayStart((ulong)value.ParticipantRefs.Count);
        foreach (var reference in value.ParticipantRefs) WriteCausality(writer, reference);
    }

    private static void WriteOptionalUnsigned(MvDcborWriter writer, ulong? value)
    {
        writer.WriteArrayStart(value.HasValue ? 2UL : 1UL);
        writer.WriteBoolean(value.HasValue);
        if (value is { } present) writer.WriteUnsigned(present);
    }
}

public sealed class CrossDomainTransactionStateSetV1
{
    private readonly SortedDictionary<OpaqueId128, CrossDomainTransactionStateV1> _states;

    public CrossDomainTransactionStateSetV1(IEnumerable<CrossDomainTransactionStateV1> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        _states = new SortedDictionary<OpaqueId128, CrossDomainTransactionStateV1>();
        foreach (var state in states)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (!_states.TryAdd(state.TransactionId, state))
                throw new InvalidDataException("transaction.persistent-state-id-duplicate");
        }
    }

    public IReadOnlyList<CrossDomainTransactionStateV1> CanonicalStates
        => Array.AsReadOnly(_states.Values.ToArray());

    public IReadOnlyList<CrossDomainTransactionStateV1> ActiveStates
        => Array.AsReadOnly(_states.Values.Where(static state => state.IsActive).ToArray());

    public bool TryGet(OpaqueId128 transactionId, out CrossDomainTransactionStateV1? state)
        => _states.TryGetValue(transactionId, out state);
}
