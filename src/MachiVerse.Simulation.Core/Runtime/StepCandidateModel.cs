using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

public sealed class PartitionCandidateV1
{
    public PartitionCandidateV1(
        StableToken partitionId,
        StableToken ownerDomain,
        ulong basisRevision,
        ulong basisStep,
        ReadOnlySpan<byte> changeSetDigest)
    {
        if (basisRevision == 0) throw new ArgumentOutOfRangeException(nameof(basisRevision));
        if (basisRevision == ulong.MaxValue) throw new InvalidDataException("step-candidate.partition-revision-overflow");
        if (basisStep == ulong.MaxValue) throw new InvalidDataException("step-candidate.step-overflow");
        if (changeSetDigest.Length != 32)
            throw new ArgumentException("Partition change-set digest must be 32 bytes.", nameof(changeSetDigest));

        var identity = StandardDomainPartitionRegistry.Get(partitionId.Value);
        if (identity.OwnerDomain != ownerDomain)
            throw new InvalidDataException("step-candidate.partition-owner-mismatch");

        PartitionId = partitionId;
        OwnerDomain = ownerDomain;
        BasisRevision = basisRevision;
        CandidateRevision = basisRevision + 1;
        BasisStep = basisStep;
        TargetStep = basisStep + 1;
        ChangeSetDigest = changeSetDigest.ToArray();
        CandidateDigest = HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
        {
            writer.WriteMapStart(7);
            writer.WriteUnsigned(0); writer.WriteAsciiText(PartitionId.Value);
            writer.WriteUnsigned(1); writer.WriteAsciiText(OwnerDomain.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(BasisRevision);
            writer.WriteUnsigned(3); writer.WriteUnsigned(CandidateRevision);
            writer.WriteUnsigned(4); writer.WriteUnsigned(BasisStep);
            writer.WriteUnsigned(5); writer.WriteUnsigned(TargetStep);
            writer.WriteUnsigned(6); writer.WriteBytes(ChangeSetDigest);
        });
    }

    public StableToken PartitionId { get; }
    public StableToken OwnerDomain { get; }
    public ulong BasisRevision { get; }
    public ulong CandidateRevision { get; }
    public ulong BasisStep { get; }
    public ulong TargetStep { get; }
    public byte[] ChangeSetDigest { get; }
    public byte[] CandidateDigest { get; }
}

public sealed class StepCandidateV1
{
    private static readonly StableToken TransactionAtomicityInvariant = new("transaction.atomicity");
    private static readonly StableToken TransactionAtomicityFailure = new("transaction.invariant-failed");

    private StepCandidateV1(
        OpaqueId128 candidateId,
        OpaqueId128 worldId,
        ulong basisStep,
        ulong targetStep,
        ulong configGeneration,
        byte[] configDigest,
        FrozenStepInputV1 frozenInput,
        IReadOnlyList<DomainCandidateOutputV1> domainOutputs,
        IReadOnlyList<MutationIntentCandidateV1> orderedIntents,
        IReadOnlyList<ConflictGroupResolutionV1> conflictResolutions,
        IReadOnlyList<PartitionCandidateV1> partitionCandidates,
        IReadOnlyList<CrossDomainTransactionCandidateV1> transactionCandidates,
        IReadOnlyList<InvariantResultV1> invariantResults,
        InvariantBarrierDecisionV1 commitDecision,
        byte[] diagnosticDigest)
    {
        CandidateId = candidateId;
        WorldId = worldId;
        BasisStep = basisStep;
        TargetStep = targetStep;
        ConfigGeneration = configGeneration;
        ConfigDigest = configDigest;
        FrozenInput = frozenInput;
        DomainOutputs = domainOutputs;
        OrderedIntents = orderedIntents;
        ConflictResolutions = conflictResolutions;
        PartitionCandidates = partitionCandidates;
        TransactionCandidates = transactionCandidates;
        InvariantResults = invariantResults;
        CommitDecision = commitDecision;
        DiagnosticDigest = diagnosticDigest;
    }

    public OpaqueId128 CandidateId { get; }
    public OpaqueId128 WorldId { get; }
    public ulong BasisStep { get; }
    public ulong TargetStep { get; }
    public ulong ConfigGeneration { get; }
    public byte[] ConfigDigest { get; }
    public FrozenStepInputV1 FrozenInput { get; }
    public IReadOnlyList<DomainCandidateOutputV1> DomainOutputs { get; }
    public IReadOnlyList<MutationIntentCandidateV1> OrderedIntents { get; }
    public IReadOnlyList<ConflictGroupResolutionV1> ConflictResolutions { get; }
    public IReadOnlyList<PartitionCandidateV1> PartitionCandidates { get; }
    public IReadOnlyList<CrossDomainTransactionCandidateV1> TransactionCandidates { get; }
    public IReadOnlyList<InvariantResultV1> InvariantResults { get; }
    public InvariantBarrierDecisionV1 CommitDecision { get; }
    public byte[] DiagnosticDigest { get; }
    public bool IsPublishable => false;

    public static StepCandidateV1 Build(
        OpaqueId128 candidateId,
        WorldStateV1 state,
        FrozenStepInputV1 frozenInput,
        IEnumerable<DomainCandidateOutputV1> domainOutputs,
        IEnumerable<ConflictGroupResolutionV1> conflictResolutions,
        IEnumerable<PartitionCandidateV1>? partitionCandidates = null,
        IEnumerable<InvariantResultV1>? invariantResults = null,
        IEnumerable<CrossDomainTransactionCandidateV1>? transactionCandidates = null)
    {
        if (candidateId.IsZero) throw new ArgumentException("CandidateId ZERO is invalid.", nameof(candidateId));
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frozenInput);
        ArgumentNullException.ThrowIfNull(domainOutputs);
        ArgumentNullException.ThrowIfNull(conflictResolutions);
        if (state.Header.WorldId != frozenInput.WorldId || state.Header.Step != frozenInput.BasisStep)
            throw new InvalidDataException("step-candidate.frozen-input-basis-mismatch");
        if (state.Header.Step == ulong.MaxValue)
            throw new InvalidDataException("step-candidate.step-overflow");
        if (frozenInput.ConfigGeneration < state.Header.ConfigGeneration)
            throw new InvalidDataException("step-candidate.config-generation-behind-state");
        if (frozenInput.ConfigGeneration == state.Header.ConfigGeneration &&
            !CryptographicOperations.FixedTimeEquals(frozenInput.ConfigDigest, state.Diagnostic.ConfigDigest))
            throw new InvalidDataException("step-candidate.config-digest-mismatch-at-generation");

        var plan = StandardDomainExecutionPlanV1.Create();
        var rankByDomain = plan.Entries.ToDictionary(static entry => entry.DomainToken, static entry => entry.DomainRank);
        var outputs = domainOutputs
            .OrderBy(output => rankByDomain.TryGetValue(output.DomainToken, out var rank) ? rank : ushort.MaxValue)
            .ThenBy(static output => output.DomainToken.Value, StringComparer.Ordinal)
            .ToArray();
        if (outputs.Length != plan.Entries.Count ||
            outputs.Select(static output => output.DomainToken).Distinct().Count() != outputs.Length ||
            plan.Entries.Any(entry => outputs.All(output => output.DomainToken != entry.DomainToken)))
            throw new InvalidDataException("step-candidate.domain-output-coverage-mismatch");
        if (outputs.Any(output => output.BasisStep != state.Header.Step))
            throw new InvalidDataException("step-candidate.domain-output-basis-mismatch");

        var intents = DeterministicIntentMergerV1.CanonicalOrder(
            outputs.SelectMany(static output => output.Intents),
            state.Header.Step).ToArray();

        var resolutions = conflictResolutions
            .OrderBy(static resolution => Convert.ToHexString(resolution.Group.Scope.Digest), StringComparer.Ordinal)
            .ToArray();
        ValidateResolutionCoverage(intents, resolutions);

        var externalPartitions = (partitionCandidates ?? Array.Empty<PartitionCandidateV1>()).ToArray();
        if (externalPartitions.Length != 0)
            throw new InvalidDataException("step-candidate.external-partition-candidates-not-allowed");

        var partitions = outputs
            .SelectMany(static output => output.LocalPartitionCandidates)
            .OrderBy(static candidate => candidate.PartitionId.Value, StringComparer.Ordinal)
            .ToArray();
        if (partitions.Select(static candidate => candidate.PartitionId).Distinct().Count() != partitions.Length)
            throw new InvalidDataException("step-candidate.duplicate-partition-candidate");
        if (partitions.Any(candidate => candidate.BasisStep != state.Header.Step || candidate.TargetStep != state.Header.Step + 1))
            throw new InvalidDataException("step-candidate.partition-basis-mismatch");
        foreach (var candidate in partitions)
        {
            var basis = state.Partitions.Get(candidate.PartitionId.Value).Header;
            if (basis.OwnerDomain != candidate.OwnerDomain)
                throw new InvalidDataException("step-candidate.partition-owner-mismatch");
            if (basis.Revision != candidate.BasisRevision)
                throw new InvalidDataException("step-candidate.partition-basis-revision-mismatch");
            if (basis.BasisStep > state.Header.Step)
                throw new InvalidDataException("step-candidate.partition-basis-ahead");
        }

        var transactions = (transactionCandidates ?? Array.Empty<CrossDomainTransactionCandidateV1>())
            .Select(candidate => candidate ?? throw new ArgumentNullException(nameof(transactionCandidates)))
            .OrderBy(static candidate => candidate.TransactionId)
            .ToArray();
        if (transactions.Select(static candidate => candidate.TransactionId).Distinct().Count() != transactions.Length)
            throw new InvalidDataException("step-candidate.duplicate-transaction-candidate");
        if (transactions.Any(candidate => candidate.WorldId != state.Header.WorldId))
            throw new InvalidDataException("step-candidate.transaction-world-mismatch");
        if (transactions.Any(candidate => candidate.BasisStep != state.Header.Step))
            throw new InvalidDataException("step-candidate.transaction-basis-mismatch");
        if (transactions.Any(static candidate => candidate.IsAuthoritative))
            throw new InvalidDataException("step-candidate.transaction-premature-authority");

        CrossDomainTransactionStepBindingV1.Validate(transactions, intents, resolutions, partitions);

        var providedInvariants = (invariantResults ?? Array.Empty<InvariantResultV1>()).ToArray();
        var invariants = (transactions.Length == 0
                ? providedInvariants
                : providedInvariants.Append(BuildTransactionAtomicityInvariant(transactions)))
            .OrderBy(static result => result.InvariantId.Value, StringComparer.Ordinal)
            .ThenBy(static result => result.Severity)
            .ToArray();
        var decision = InvariantBarrierV1.Evaluate(invariants);
        var targetStep = state.Header.Step + 1;
        var diagnostic = ComputeDiagnostic(
            state,
            frozenInput,
            outputs,
            resolutions,
            partitions,
            transactions,
            invariants,
            targetStep);

        return new StepCandidateV1(
            candidateId,
            state.Header.WorldId,
            state.Header.Step,
            targetStep,
            frozenInput.ConfigGeneration,
            frozenInput.ConfigDigest.ToArray(),
            frozenInput,
            Array.AsReadOnly(outputs),
            Array.AsReadOnly(intents),
            Array.AsReadOnly(resolutions),
            Array.AsReadOnly(partitions),
            Array.AsReadOnly(transactions),
            Array.AsReadOnly(invariants),
            decision,
            diagnostic);
    }

    private static InvariantResultV1 BuildTransactionAtomicityInvariant(
        IReadOnlyList<CrossDomainTransactionCandidateV1> transactions)
    {
        var allValid = transactions.All(static candidate => candidate.CanFinalize);
        var failureCode = transactions
            .Where(static candidate => !candidate.CanFinalize)
            .Select(static candidate => candidate.FailureCode)
            .FirstOrDefault(static code => code is not null)
            ?? (allValid ? null : TransactionAtomicityFailure);
        var strongestFailedSeverity = transactions
            .SelectMany(static candidate => candidate.InvariantResults)
            .Where(static result => result.Outcome == InvariantOutcomeV1.Fail &&
                                    result.Severity is InvariantSeverityV1.CommitBlocking or InvariantSeverityV1.FatalAuthority)
            .Select(static result => result.Severity)
            .DefaultIfEmpty(InvariantSeverityV1.CommitBlocking)
            .Max();
        var participantRefs = transactions.Select(candidate =>
            new CausalityRefV1(
                CausalityRefKindV1.Transaction,
                candidate.TransactionId.ToBytes(),
                candidate.BasisStep));

        return new InvariantResultV1(
            TransactionAtomicityInvariant,
            strongestFailedSeverity,
            allValid ? InvariantOutcomeV1.Pass : InvariantOutcomeV1.Fail,
            participantRefs,
            failureCode);
    }

    private static void ValidateResolutionCoverage(
        IReadOnlyCollection<MutationIntentCandidateV1> intents,
        IReadOnlyCollection<ConflictGroupResolutionV1> resolutions)
    {
        var intended = intents.Select(static intent => intent.IntentId).ToHashSet();
        var resolved = new HashSet<OpaqueId128>();
        foreach (var outcome in resolutions.SelectMany(static resolution => resolution.Outcomes))
        {
            if (!resolved.Add(outcome.Intent.IntentId))
                throw new InvalidDataException("step-candidate.intent-resolved-more-than-once");
        }
        if (!intended.SetEquals(resolved))
            throw new InvalidDataException("step-candidate.intent-resolution-coverage-mismatch");
    }

    private static byte[] ComputeDiagnostic(
        WorldStateV1 state,
        FrozenStepInputV1 frozenInput,
        IReadOnlyList<DomainCandidateOutputV1> outputs,
        IReadOnlyList<ConflictGroupResolutionV1> resolutions,
        IReadOnlyList<PartitionCandidateV1> partitions,
        IReadOnlyList<CrossDomainTransactionCandidateV1> transactions,
        IReadOnlyList<InvariantResultV1> invariants,
        ulong targetStep)
        => HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
        {
            var hasTransactions = transactions.Count != 0;
            writer.WriteMapStart(hasTransactions ? 11UL : 10UL);
            writer.WriteUnsigned(0); writer.WriteBytes(state.Header.WorldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(state.Header.Step);
            writer.WriteUnsigned(2); writer.WriteUnsigned(targetStep);
            writer.WriteUnsigned(3); writer.WriteUnsigned(frozenInput.ConfigGeneration);
            writer.WriteUnsigned(4); writer.WriteBytes(frozenInput.ConfigDigest);
            writer.WriteUnsigned(5);
            writer.WriteArrayStart((ulong)frozenInput.ScheduledOperations.Count);
            foreach (var operation in frozenInput.ScheduledOperations)
            {
                writer.WriteArrayStart(2);
                writer.WriteBytes(operation.OperationId.ToBytes());
                writer.WriteBytes(operation.OrderKey.ToDatabaseBytes());
            }
            writer.WriteUnsigned(6);
            writer.WriteArrayStart((ulong)outputs.Count);
            foreach (var output in outputs)
            {
                writer.WriteArrayStart(2);
                writer.WriteAsciiText(output.DomainToken.Value);
                writer.WriteArrayStart((ulong)output.Intents.Count);
                foreach (var intent in output.Intents)
                    writer.WriteBytes(intent.IntentId.ToBytes());
            }
            writer.WriteUnsigned(7);
            writer.WriteArrayStart((ulong)resolutions.Count);
            foreach (var resolution in resolutions)
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteBytes(resolution.Group.Scope.Digest);
                writer.WriteUnsigned(1); writer.WriteUnsigned((uint)resolution.Group.ResolutionMode);
                writer.WriteUnsigned(2);
                writer.WriteArrayStart((ulong)resolution.Outcomes.Count);
                foreach (var outcome in resolution.Outcomes)
                {
                    writer.WriteArrayStart(2);
                    writer.WriteBytes(outcome.Intent.IntentId.ToBytes());
                    writer.WriteUnsigned((uint)outcome.Disposition);
                }
                writer.WriteUnsigned(3);
                if (resolution.AggregateDigest is null)
                {
                    writer.WriteArrayStart(0);
                }
                else
                {
                    writer.WriteArrayStart(1);
                    writer.WriteBytes(resolution.AggregateDigest);
                }
            }
            writer.WriteUnsigned(8);
            writer.WriteArrayStart((ulong)partitions.Count);
            foreach (var partition in partitions)
            {
                writer.WriteArrayStart(2);
                writer.WriteAsciiText(partition.PartitionId.Value);
                writer.WriteBytes(partition.CandidateDigest);
            }

            if (hasTransactions)
            {
                writer.WriteUnsigned(9);
                writer.WriteArrayStart((ulong)transactions.Count);
                foreach (var transaction in transactions)
                {
                    writer.WriteMapStart(4);
                    writer.WriteUnsigned(0); writer.WriteBytes(transaction.TransactionId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteAsciiText(transaction.TransactionKind.Value);
                    writer.WriteUnsigned(2); writer.WriteUnsigned((uint)transaction.Status);
                    writer.WriteUnsigned(3); writer.WriteBytes(transaction.DiagnosticDigest);
                }
            }

            writer.WriteUnsigned(hasTransactions ? 10UL : 9UL);
            writer.WriteArrayStart((ulong)invariants.Count);
            foreach (var invariant in invariants)
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteAsciiText(invariant.InvariantId.Value);
                writer.WriteUnsigned(1); writer.WriteUnsigned((uint)invariant.Severity);
                writer.WriteUnsigned(2); writer.WriteUnsigned((uint)invariant.Outcome);
                writer.WriteUnsigned(3);
                if (invariant.DiagnosticCode is { } code)
                {
                    writer.WriteArrayStart(1);
                    writer.WriteAsciiText(code.Value);
                }
                else
                {
                    writer.WriteArrayStart(0);
                }
            }
        });
}
