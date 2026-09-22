using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04OperationClosedPrefixV1(
    string ProfileId,
    ulong FirstInjectionStep,
    ulong? LastClosedInjectionStep,
    ulong TerminalOperationCount,
    byte[] TerminalSemanticDigest)
{
    public static Qa04OperationClosedPrefixV1 Empty()
        => new(
            Qa04ReferenceLoadV1.BenchmarkProfileId,
            0,
            null,
            0,
            Qa04Step2DeterminismAccumulatorV1.Genesis("mv.qa04-operation-terminal.v1"));

    public void Validate(ulong stateStep)
    {
        if (!string.Equals(ProfileId, Qa04ReferenceLoadV1.BenchmarkProfileId, StringComparison.Ordinal))
            throw new InvalidDataException("qa04.operation-prefix.profile-id-mismatch");
        if (FirstInjectionStep != 0)
            throw new InvalidDataException("qa04.operation-prefix.first-injection-step");
        if (TerminalSemanticDigest is null || TerminalSemanticDigest.Length != 32 || TerminalSemanticDigest.All(static value => value == 0))
            throw new InvalidDataException("qa04.operation-prefix.terminal-digest-invalid");

        if (LastClosedInjectionStep is null)
        {
            if (stateStep != 1 || TerminalOperationCount != 0)
                throw new InvalidDataException("qa04.operation-prefix.empty-state-mismatch");
            return;
        }

        var last = LastClosedInjectionStep.Value;
        var expectedStateStep = checked(last + 2UL);
        if (stateStep != expectedStateStep)
            throw new InvalidDataException("qa04.operation-prefix.state-step-mismatch");
        if (TerminalOperationCount != ExpectedTerminalOperationCount(last))
            throw new InvalidDataException("qa04.operation-prefix.terminal-count-mismatch");
    }

    public Qa04OperationClosedPrefixV1 Advance(
        ulong injectionStep,
        ulong operationCount,
        byte[] terminalSemanticDigest,
        ulong resultingStateStep)
    {
        if (terminalSemanticDigest is null || terminalSemanticDigest.Length != 32)
            throw new ArgumentException("Terminal semantic digest must be 32 bytes.", nameof(terminalSemanticDigest));
        var expectedInjection = LastClosedInjectionStep is null ? 0UL : checked(LastClosedInjectionStep.Value + 1UL);
        if (injectionStep != expectedInjection)
            throw new InvalidDataException("qa04.operation-prefix.hole");
        var next = this with
        {
            LastClosedInjectionStep = injectionStep,
            TerminalOperationCount = checked(TerminalOperationCount + operationCount),
            TerminalSemanticDigest = terminalSemanticDigest.ToArray(),
        };
        next.Validate(resultingStateStep);
        return next;
    }

    public static ulong ExpectedTerminalOperationCount(ulong lastClosedInjectionStep)
    {
        var totalSteps = checked(Qa04ReferenceLoadV1.WarmupSteps + Qa04ReferenceLoadV1.MeasurementSteps);
        if (lastClosedInjectionStep >= totalSteps)
            throw new ArgumentOutOfRangeException(nameof(lastClosedInjectionStep));
        ulong count = 0;
        for (ulong step = 0; step <= lastClosedInjectionStep; step++)
            count = checked(count + Qa04ReferenceLoadV1.OperationCountForStep(step));
        return count;
    }
}

public static class Qa04OperationAuthorityV1
{
    public static readonly SchemaRefV1 Schema = new("core.operation-state", 2, 0);

    public static WorldSubstateRefV1 Canonicalize(
        IReadOnlyCollection<DurableOperationStateV1> mutableOrdinaryOperations,
        Qa04OperationClosedPrefixV1 prefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions,
        ulong stateStep)
        => CanonicalizeCore(
            mutableOrdinaryOperations,
            prefix,
            activeTransactions,
            stateStep,
            digestCache: null);

    internal static WorldSubstateRefV1 Canonicalize(
        IReadOnlyCollection<DurableOperationStateV1> mutableOrdinaryOperations,
        Qa04OperationClosedPrefixV1 prefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions,
        ulong stateStep,
        Qa04ProductionStep2CanonicalDigestCacheV1 digestCache)
    {
        ArgumentNullException.ThrowIfNull(digestCache);
        return CanonicalizeCore(
            mutableOrdinaryOperations,
            prefix,
            activeTransactions,
            stateStep,
            digestCache);
    }

    private static WorldSubstateRefV1 CanonicalizeCore(
        IReadOnlyCollection<DurableOperationStateV1> mutableOrdinaryOperations,
        Qa04OperationClosedPrefixV1 prefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions,
        ulong stateStep,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache)
    {
        ArgumentNullException.ThrowIfNull(mutableOrdinaryOperations);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(activeTransactions);
        prefix.Validate(stateStep);

        var ordinary = DurableOperationSubstateV1.Canonicalize(mutableOrdinaryOperations);
        var transactionDigest = digestCache is null
            ? ComputeActiveTransactionSetDigest(activeTransactions)
            : digestCache.ActiveTransactionSetDigest(activeTransactions);

        var digest = HashSuite.DomainHash("mv.qa04-operation-authority.v1", writer =>
        {
            writer.WriteMapStart(3);
            writer.WriteUnsigned(0); writer.WriteBytes(ordinary.CanonicalDigest);
            writer.WriteUnsigned(1); WritePrefix(writer, prefix);
            writer.WriteUnsigned(2); writer.WriteBytes(transactionDigest);
        });
        return new WorldSubstateRefV1(Schema, digest);
    }

    internal static byte[] ComputeActiveTransactionSetDigest(
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions)
    {
        ArgumentNullException.ThrowIfNull(activeTransactions);
        if (activeTransactions.Count != checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount) ||
            activeTransactions.Any(static state => !state.IsActive || state.TerminalStep is not null))
            throw new InvalidDataException("qa04.operation-authority.active-transaction-set-invalid");

        var orderedTransactions = activeTransactions.OrderBy(static state => state.TransactionId).ToArray();
        for (var index = 1; index < orderedTransactions.Length; index++)
        {
            if (orderedTransactions[index - 1].TransactionId == orderedTransactions[index].TransactionId)
                throw new InvalidDataException("qa04.operation-authority.transaction-id-duplicate");
        }

        using var session = HashSuite.BeginDomainHashStreaming("mv.cross-domain-transaction-state-set.v1");
        var writer = session.Writer;
        writer.WriteArrayStart((ulong)orderedTransactions.Length);
        foreach (var state in orderedTransactions)
            writer.WriteBytes(state.CanonicalDigest());
        return session.Complete();
    }

    internal static void WritePrefix(MvDcborWriter writer, Qa04OperationClosedPrefixV1 prefix)
    {
        writer.WriteMapStart(5);
        writer.WriteUnsigned(0); writer.WriteAsciiText(prefix.ProfileId);
        writer.WriteUnsigned(1); writer.WriteUnsigned(prefix.FirstInjectionStep);
        writer.WriteUnsigned(2);
        if (prefix.LastClosedInjectionStep is { } last)
        {
            writer.WriteArrayStart(1);
            writer.WriteUnsigned(last);
        }
        else
        {
            writer.WriteArrayStart(0);
        }
        writer.WriteUnsigned(3); writer.WriteUnsigned(prefix.TerminalOperationCount);
        writer.WriteUnsigned(4); writer.WriteBytes(prefix.TerminalSemanticDigest);
    }
}

public sealed record Qa04ScheduledOperationBatchAuthorityV1(
    ulong InjectionStep,
    ulong EffectiveStep,
    ulong OperationCount,
    byte[] ScheduledBatchDigest,
    HistoryRecordMaterial History);

public static class Qa04ScheduledOperationBatchAuthorityBuilderV1
{
    public static Qa04ScheduledOperationBatchAuthorityV1 Create(
        OpaqueId128 worldId,
        HistoryAnchor anchor,
        ulong injectionStep,
        ulong effectiveStep,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (effectiveStep != checked(injectionStep + 1UL))
            throw new InvalidDataException("qa04.operation-batch.effective-step-drift");
        if ((ulong)bindings.Count != Qa04ReferenceLoadV1.OperationCountForStep(injectionStep))
            throw new InvalidDataException("qa04.operation-batch.cardinality-drift");

        var operationIds = new HashSet<OpaqueId128>(bindings.Count);
        Qa04CanonicalOperationBindingResultV1? previous = null;
        foreach (var binding in bindings)
        {
            if (!operationIds.Add(binding.SourceDescriptor.OperationId))
                throw new InvalidDataException("qa04.operation-batch.operation-id-duplicate");

            if (previous is not null)
            {
                var order = previous.OrderKey.CompareTo(binding.OrderKey);
                if (order > 0 ||
                    (order == 0 &&
                     previous.SourceDescriptor.OperationId.CompareTo(binding.SourceDescriptor.OperationId) > 0))
                    throw new InvalidDataException("qa04.operation-batch.noncanonical-order");
            }
            previous = binding;
        }

        var scheduledBatchDigest = ComputeScheduledBatchDigest(bindings);

        var physical = new MvDcborWriter();
        WriteNormalized(physical, injectionStep, effectiveStep, checked((ulong)bindings.Count), scheduledBatchDigest);
        var history = HistoryRecordMaterial.Create(
            worldId,
            checked(anchor.Sequence + 1UL),
            anchor.Digest,
            "qa04.operation-batch.scheduled.v1",
            "qa04.operation-batch",
            1,
            0,
            physical.ToArray(),
            writer => WriteNormalized(writer, injectionStep, effectiveStep, checked((ulong)bindings.Count), scheduledBatchDigest));
        return new Qa04ScheduledOperationBatchAuthorityV1(
            injectionStep,
            effectiveStep,
            checked((ulong)bindings.Count),
            scheduledBatchDigest,
            history);
    }

    public static byte[] ComputeScheduledBatchDigest(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> canonicalBindings)
    {
        ArgumentNullException.ThrowIfNull(canonicalBindings);
        using var session = HashSuite.BeginDomainHashStreaming("mv.qa04-operation-scheduled-batch.v1");
        var writer = session.Writer;
        writer.WriteArrayStart((ulong)canonicalBindings.Count);
        Span<byte> operationIdBytes = stackalloc byte[16];
        Span<byte> orderKeyBytes = stackalloc byte[SameStepOrderKey.DatabaseKeyLength];
        foreach (var binding in canonicalBindings)
        {
            binding.SourceDescriptor.OperationId.WriteBytes(operationIdBytes);
            binding.OrderKey.WriteDatabaseBytes(orderKeyBytes);
            writer.WriteArrayStart(3);
            writer.WriteBytes(operationIdBytes);
            writer.WriteBytes(binding.BoundDescriptor.PayloadDigest);
            writer.WriteBytes(orderKeyBytes);
        }
        return session.Complete();
    }

    private static void WriteNormalized(
        MvDcborWriter writer,
        ulong injectionStep,
        ulong effectiveStep,
        ulong operationCount,
        byte[] scheduledBatchDigest)
    {
        writer.WriteMapStart(5);
        writer.WriteUnsigned(0); writer.WriteAsciiText(Qa04ReferenceLoadV1.BenchmarkProfileId);
        writer.WriteUnsigned(1); writer.WriteUnsigned(injectionStep);
        writer.WriteUnsigned(2); writer.WriteUnsigned(effectiveStep);
        writer.WriteUnsigned(3); writer.WriteUnsigned(operationCount);
        writer.WriteUnsigned(4); writer.WriteBytes(scheduledBatchDigest);
    }
}

public sealed class Qa04Step2DeterminismAccumulatorV1
{
    private readonly string _label;
    private byte[] _digest;

    public Qa04Step2DeterminismAccumulatorV1(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _label = label;
        _digest = Genesis(label);
    }

    public ulong Count { get; private set; }
    public byte[] Digest => _digest.ToArray();

    public static byte[] Genesis(string label)
        => HashSuite.DomainHash(label + ".genesis", writer => writer.WriteArrayStart(0));

    public void Append(ulong ordinal, ReadOnlySpan<byte> itemDigest)
    {
        if (ordinal != checked(Count + 1UL))
            throw new InvalidDataException("qa04.determinism-accumulator.ordinal-gap");
        if (itemDigest.Length != 32)
            throw new ArgumentException("Accumulator item digest must be 32 bytes.", nameof(itemDigest));
        var prior = _digest;
        var item = itemDigest.ToArray();
        _digest = HashSuite.DomainHash(_label + ".append", writer =>
        {
            writer.WriteArrayStart(3);
            writer.WriteBytes(prior);
            writer.WriteUnsigned(ordinal);
            writer.WriteBytes(item);
        });
        Count = ordinal;
    }
}

public static class Qa04TerminalSemanticAuthorityV1
{
    public static byte[] ComputeBatchDigest(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        IReadOnlyList<TerminalOperationCommit> outcomes)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(outcomes);
        if (bindings.Count != outcomes.Count)
            throw new InvalidDataException("qa04.operation-terminal.cardinality-mismatch");
        using var session = HashSuite.BeginDomainHashStreaming("mv.qa04-operation-terminal-batch.v1");
        var writer = session.Writer;
        writer.WriteArrayStart((ulong)outcomes.Count);
        Span<byte> operationIdBytes = stackalloc byte[16];
        for (var i = 0; i < outcomes.Count; i++)
        {
            var binding = bindings[i];
            var outcome = outcomes[i];
            if (outcome.OperationId != binding.SourceDescriptor.OperationId)
                throw new InvalidDataException("qa04.operation-terminal.order-mismatch");
            if (outcome.TerminalStatus < 0)
                throw new InvalidDataException("qa04.operation-terminal.status-negative");
            _ = new StableToken(outcome.ResultCode);
            outcome.OperationId.WriteBytes(operationIdBytes);
            writer.WriteArrayStart(4);
            writer.WriteBytes(operationIdBytes);
            writer.WriteUnsigned(checked((ulong)outcome.TerminalStatus));
            writer.WriteAsciiText(outcome.ResultCode);
            writer.WriteArrayStart(0);
        }
        return session.Complete();
    }

    internal static byte[] ComputeFinalizationBatchDigest(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        IReadOnlyList<TerminalOperationCommit> outcomes)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(outcomes);
        if (bindings.Count != outcomes.Count)
            throw new InvalidDataException("qa04.step2.finalization-terminal-coverage-count-drift");

        using var session = HashSuite.BeginDomainHashStreaming("mv.qa04-operation-terminal-batch.v1");
        var writer = session.Writer;
        writer.WriteArrayStart((ulong)outcomes.Count);
        Span<byte> operationIdBytes = stackalloc byte[16];
        for (var index = 0; index < outcomes.Count; index++)
        {
            var outcome = outcomes[index];
            if (outcome.OperationId != bindings[index].SourceDescriptor.OperationId)
                throw new InvalidDataException("qa04.step2.finalization-terminal-coverage-drift");
            if (!Enum.IsDefined(typeof(CoreOperationResultStatusV1), outcome.TerminalStatus) ||
                !OperationLifecycleRulesV1.IsTerminalResult((CoreOperationResultStatusV1)outcome.TerminalStatus))
                throw new InvalidDataException("qa04.step2.finalization-terminal-status-invalid");
            _ = new StableToken(outcome.ResultCode);

            outcome.OperationId.WriteBytes(operationIdBytes);
            writer.WriteArrayStart(4);
            writer.WriteBytes(operationIdBytes);
            writer.WriteUnsigned(checked((ulong)outcome.TerminalStatus));
            writer.WriteAsciiText(outcome.ResultCode);
            writer.WriteArrayStart(0);
        }
        return session.Complete();
    }

    public static byte[] ComputeStepItemDigest(ulong effectiveStep, ulong operationCount, byte[] terminalBatchDigest)
        => HashSuite.DomainHash("mv.qa04-operation-terminal-step.v1", writer =>
        {
            writer.WriteArrayStart(3);
            writer.WriteUnsigned(effectiveStep);
            writer.WriteUnsigned(operationCount);
            writer.WriteBytes(terminalBatchDigest);
        });
}

public sealed record Qa04DetailDecisionAuthorityV1(
    ulong BasisStep,
    ulong ResultingStep,
    HistoryRecordMaterial History);

public static class Qa04DetailDecisionAuthorityBuilderV1
{
    private static readonly IReadOnlyDictionary<StableToken, ushort> DomainRanks =
        StandardDomainExecutionPlanV1.Create().Entries.ToDictionary(static entry => entry.DomainToken, static entry => entry.DomainRank);

    public static Qa04DetailDecisionAuthorityV1 Create(
        OpaqueId128 worldId,
        HistoryAnchor anchor,
        ulong basisStep,
        DetailTransitionPlanV1 plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.BasisStep != basisStep)
            throw new InvalidDataException("qa04.detail-decision.plan-step-drift");
        var resultingStep = checked(basisStep + 1UL);
        var classified = new Dictionary<DetailTransitionCandidateV1, ulong>();
        Add(plan.Selected, 1);
        Add(plan.Deferred, 2);
        Add(plan.NotYetEligible, 3);
        var ordered = DetailTransitionCanonicalOrderV1.Order(classified.Keys).ToArray();
        if (ordered.Length != classified.Count)
            throw new InvalidDataException("qa04.detail-decision.candidate-duplicate");

        var physical = new MvDcborWriter();
        WritePayload(physical, basisStep, resultingStep, ordered, classified);
        var history = HistoryRecordMaterial.Create(
            worldId,
            checked(anchor.Sequence + 1UL),
            anchor.Digest,
            "qa04.detail-promotion-decision.v1",
            "qa04.detail-promotion-decision",
            1,
            0,
            physical.ToArray(),
            writer => WritePayload(writer, basisStep, resultingStep, ordered, classified));
        return new Qa04DetailDecisionAuthorityV1(basisStep, resultingStep, history);

        void Add(IEnumerable<DetailTransitionCandidateV1> candidates, ulong decisionClass)
        {
            foreach (var candidate in candidates)
            {
                if (!classified.TryAdd(candidate, decisionClass))
                    throw new InvalidDataException("qa04.detail-decision.candidate-duplicate");
            }
        }
    }

    private static void WritePayload(
        MvDcborWriter writer,
        ulong basisStep,
        ulong resultingStep,
        IReadOnlyList<DetailTransitionCandidateV1> ordered,
        IReadOnlyDictionary<DetailTransitionCandidateV1, ulong> classified)
    {
        writer.WriteMapStart(3);
        writer.WriteUnsigned(0); writer.WriteUnsigned(basisStep);
        writer.WriteUnsigned(1); writer.WriteUnsigned(resultingStep);
        writer.WriteUnsigned(2); writer.WriteArrayStart((ulong)ordered.Count);
        foreach (var candidate in ordered)
        {
            if (candidate.SemanticPriority < 0)
                throw new InvalidDataException("qa04.detail-decision.semantic-priority-negative");
            writer.WriteArrayStart(6);
            writer.WriteUnsigned(classified[candidate]);
            writer.WriteUnsigned(candidate.RequiredEffectiveStep);
            writer.WriteUnsigned(checked((ulong)candidate.SemanticPriority));
            writer.WriteBytes(candidate.DetailRegionId.ToBytes());
            writer.WriteUnsigned(DomainRanks[candidate.DomainToken]);
            writer.WriteBytes(candidate.TriggerId.ToBytes());
        }
    }
}

public static class Qa04ConfigHistoryAuthorityV1
{
    public static Qa04Step2DeterminismAccumulatorV1 CreateInitial(ulong generation, byte[] configDigest)
    {
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
        if (configDigest is null || configDigest.Length != 32) throw new ArgumentException("Config digest must be 32 bytes.", nameof(configDigest));
        var accumulator = new Qa04Step2DeterminismAccumulatorV1("mv.qa04-config-history.v1");
        var item = HashSuite.Hash256(NormalizedInitial(generation, configDigest));
        accumulator.Append(1, item);
        return accumulator;
    }

    private static byte[] NormalizedInitial(ulong generation, byte[] configDigest)
    {
        var writer = new MvDcborWriter();
        writer.WriteArrayStart(4);
        writer.WriteUnsigned(1);
        writer.WriteUnsigned(generation);
        writer.WriteBytes(configDigest);
        writer.WriteArrayStart(0);
        return writer.ToArray();
    }
}
