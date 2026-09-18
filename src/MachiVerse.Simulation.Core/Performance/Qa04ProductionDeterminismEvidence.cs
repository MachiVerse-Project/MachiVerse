using System.Security.Cryptography;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionDeterminismEvidenceV1(
    string FinalStateDigest,
    string TransitionCommittedDigest,
    string OperationTerminalSemanticDigest,
    string ConfigHistoryDigest,
    string PromotionDeferralOrderDigest);

/// <summary>
/// Streaming producer for the five semantic digests required by the Gate4 Step2
/// perf.reference.v1 determinism evidence contract. Every item is derived from the
/// canonical production authorities used by the committed Step; no parallel evidence
/// payload is accepted.
/// </summary>
public sealed class Qa04ProductionDeterminismEvidenceProducerV1
{
    private readonly Qa04Step2DeterminismAccumulatorV1 _transitionCommitted =
        new("mv.qa04-transition-committed.v1");
    private readonly Qa04Step2DeterminismAccumulatorV1 _operationTerminal =
        new("mv.qa04-operation-terminal.v1");
    private readonly Qa04Step2DeterminismAccumulatorV1 _promotionDeferralOrder =
        new("mv.qa04-promotion-deferral-order.v1");
    private readonly Qa04Step2DeterminismAccumulatorV1 _configHistory;
    private readonly ulong _configGeneration;
    private readonly byte[] _configDigest;
    private ulong _terminalOperationCount;

    public Qa04ProductionDeterminismEvidenceProducerV1()
    {
        var config = Qa04ReferenceConfigAuthorityV1.CreateCanonical();
        _configGeneration = config.Generation;
        _configDigest = config.Digest.ToArray();
        _configHistory = Qa04ConfigHistoryAuthorityV1.CreateInitial(_configGeneration, _configDigest);
    }

    public ulong CommittedTransitionCount => _transitionCommitted.Count;
    public ulong TerminalOperationCount => _terminalOperationCount;

    public void Append(
        ulong injectionStep,
        Qa04TransitionCommittedAuthorityV1 transitionAuthority,
        Qa04DetailDecisionAuthorityV1 detailDecisionAuthority,
        Qa04OperationClosedPrefixV1 resultingPrefix)
    {
        ArgumentNullException.ThrowIfNull(transitionAuthority);
        ArgumentNullException.ThrowIfNull(detailDecisionAuthority);
        ArgumentNullException.ThrowIfNull(resultingPrefix);

        var ordinal = checked(_transitionCommitted.Count + 1UL);
        var expectedInjectionStep = checked(ordinal - 1UL);
        if (injectionStep != expectedInjectionStep || injectionStep >= Qa04ProductionReferenceRunV1.CanonicalTransitionCount)
            throw new InvalidDataException("qa04.determinism-evidence.transition-ordinal-drift");

        var effectiveStep = ordinal;
        var resultingStep = checked(ordinal + 1UL);
        if (transitionAuthority.EffectiveStep != effectiveStep ||
            transitionAuthority.ResultingStep != resultingStep)
            throw new InvalidDataException("qa04.determinism-evidence.transition-step-drift");
        if (transitionAuthority.ActiveConfigGeneration != _configGeneration ||
            !CryptographicOperations.FixedTimeEquals(transitionAuthority.ActiveConfigDigest, _configDigest))
            throw new InvalidDataException("qa04.determinism-evidence.config-authority-drift");
        if (detailDecisionAuthority.BasisStep != effectiveStep ||
            detailDecisionAuthority.ResultingStep != resultingStep ||
            !string.Equals(detailDecisionAuthority.History.RecordType, "qa04.detail-promotion-decision.v1", StringComparison.Ordinal))
            throw new InvalidDataException("qa04.determinism-evidence.detail-decision-step-drift");

        var bindings = Qa04ReferenceLoadV1.OperationsForStep(injectionStep)
            .Select(descriptor => Qa04CanonicalOperationBindingV1.Bind(descriptor, transitionAuthority.ActiveConfigGeneration))
            .OrderBy(static binding => binding.OrderKey)
            .ThenBy(static binding => binding.SourceDescriptor.OperationId)
            .ToArray();
        if ((ulong)bindings.Length != Qa04ReferenceLoadV1.OperationCountForStep(injectionStep) ||
            transitionAuthority.AppliedOperationIds.Count != bindings.Length ||
            transitionAuthority.OperationOutcomes.Count != bindings.Length)
            throw new InvalidDataException("qa04.determinism-evidence.operation-cardinality-drift");
        for (var index = 0; index < bindings.Length; index++)
        {
            if (transitionAuthority.AppliedOperationIds[index] != bindings[index].SourceDescriptor.OperationId)
                throw new InvalidDataException("qa04.determinism-evidence.operation-order-drift");
        }

        var terminalBatchDigest = Qa04TerminalSemanticAuthorityV1.ComputeBatchDigest(
            bindings,
            transitionAuthority.OperationOutcomes);
        var terminalStepDigest = Qa04TerminalSemanticAuthorityV1.ComputeStepItemDigest(
            effectiveStep,
            checked((ulong)bindings.Length),
            terminalBatchDigest);

        RequireDigest(transitionAuthority.TransitionCommittedItemDigest, "qa04.determinism-evidence.transition-item-digest-invalid");
        RequireDigest(detailDecisionAuthority.History.NormalizedPayloadDigest, "qa04.determinism-evidence.detail-item-digest-invalid");
        _transitionCommitted.Append(ordinal, transitionAuthority.TransitionCommittedItemDigest);
        _operationTerminal.Append(ordinal, terminalStepDigest);
        _promotionDeferralOrder.Append(ordinal, detailDecisionAuthority.History.NormalizedPayloadDigest);
        _terminalOperationCount = checked(_terminalOperationCount + (ulong)bindings.Length);

        resultingPrefix.Validate(resultingStep);
        if (resultingPrefix.LastClosedInjectionStep != injectionStep ||
            resultingPrefix.TerminalOperationCount != _terminalOperationCount ||
            resultingPrefix.TerminalOperationCount != Qa04OperationClosedPrefixV1.ExpectedTerminalOperationCount(injectionStep) ||
            !CryptographicOperations.FixedTimeEquals(resultingPrefix.TerminalSemanticDigest, _operationTerminal.Digest))
            throw new InvalidDataException("qa04.determinism-evidence.operation-prefix-drift");
    }

    public Qa04ProductionDeterminismEvidenceV1 Complete(
        WorldStateV1 finalState,
        Qa04OperationClosedPrefixV1 finalPrefix)
        => Complete(
            finalState,
            finalPrefix,
            checked((ulong)Qa04ProductionReferenceRunV1.CanonicalTransitionCount));

    public Qa04ProductionDeterminismEvidenceV1 Complete(
        WorldStateV1 finalState,
        Qa04OperationClosedPrefixV1 finalPrefix,
        ulong expectedTransitionCount)
    {
        ArgumentNullException.ThrowIfNull(finalState);
        ArgumentNullException.ThrowIfNull(finalPrefix);
        if (expectedTransitionCount == 0 ||
            expectedTransitionCount > checked((ulong)Qa04ProductionReferenceRunV1.CanonicalTransitionCount))
            throw new ArgumentOutOfRangeException(nameof(expectedTransitionCount));
        if (_transitionCommitted.Count != expectedTransitionCount ||
            _operationTerminal.Count != expectedTransitionCount ||
            _promotionDeferralOrder.Count != expectedTransitionCount)
            throw new InvalidDataException("qa04.determinism-evidence.append-count-mismatch");
        if (_configHistory.Count != 1)
            throw new InvalidDataException("qa04.determinism-evidence.config-history-count-mismatch");

        var expectedFinalStep = checked(expectedTransitionCount + 1UL);
        if (finalState.Header.Step != expectedFinalStep)
            throw new InvalidDataException("qa04.determinism-evidence.final-step-mismatch");
        if (finalState.Header.ConfigGeneration != _configGeneration ||
            !CryptographicOperations.FixedTimeEquals(finalState.Diagnostic.ConfigDigest, _configDigest))
            throw new InvalidDataException("qa04.determinism-evidence.final-config-drift");

        finalPrefix.Validate(finalState.Header.Step);
        var expectedLastInjection = checked(expectedTransitionCount - 1UL);
        var expectedTerminalCount = Qa04OperationClosedPrefixV1.ExpectedTerminalOperationCount(expectedLastInjection);
        if (finalPrefix.LastClosedInjectionStep != expectedLastInjection ||
            finalPrefix.TerminalOperationCount != expectedTerminalCount ||
            _terminalOperationCount != expectedTerminalCount ||
            !CryptographicOperations.FixedTimeEquals(finalPrefix.TerminalSemanticDigest, _operationTerminal.Digest))
            throw new InvalidDataException("qa04.determinism-evidence.final-operation-prefix-drift");

        RequireDigest(finalState.Diagnostic.StateDigest, "qa04.determinism-evidence.final-state-digest-invalid");
        RequireDigest(_transitionCommitted.Digest, "qa04.determinism-evidence.transition-digest-invalid");
        RequireDigest(_operationTerminal.Digest, "qa04.determinism-evidence.operation-digest-invalid");
        RequireDigest(_configHistory.Digest, "qa04.determinism-evidence.config-history-digest-invalid");
        RequireDigest(_promotionDeferralOrder.Digest, "qa04.determinism-evidence.promotion-digest-invalid");

        return new Qa04ProductionDeterminismEvidenceV1(
            Hex(finalState.Diagnostic.StateDigest),
            Hex(_transitionCommitted.Digest),
            Hex(_operationTerminal.Digest),
            Hex(_configHistory.Digest),
            Hex(_promotionDeferralOrder.Digest));
    }

    private static void RequireDigest(ReadOnlySpan<byte> digest, string error)
    {
        if (digest.Length != 32 || digest.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException(error);
    }

    private static string Hex(ReadOnlySpan<byte> value)
        => Convert.ToHexString(value).ToLowerInvariant();
}
