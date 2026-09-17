using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

internal static class Qa04Step2DeterminismAuthoritySmoke
{
    internal static void Run()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();

        var prefix = Qa04OperationClosedPrefixV1.Empty();
        prefix.Validate(1);

        var bindings = Qa04ReferenceLoadV1.OperationsForStep(0)
            .Select(static descriptor => Qa04CanonicalOperationBindingV1.Bind(descriptor, 1))
            .OrderBy(static binding => binding.OrderKey)
            .ToArray();
        var terminals = bindings
            .Select(static binding => new TerminalOperationCommit(
                binding.SourceDescriptor.OperationId,
                (int)CoreOperationResultStatusV1.Success,
                "operation.succeeded"))
            .ToArray();

        var terminalBatch = Qa04TerminalSemanticAuthorityV1.ComputeBatchDigest(bindings, terminals);
        var terminalStep = Qa04TerminalSemanticAuthorityV1.ComputeStepItemDigest(
            effectiveStep: 1,
            operationCount: checked((ulong)bindings.Length),
            terminalBatch);
        var terminalAccumulator = new Qa04Step2DeterminismAccumulatorV1("mv.qa04-operation-terminal.v1");
        terminalAccumulator.Append(1, terminalStep);
        prefix = prefix.Advance(0, checked((ulong)bindings.Length), terminalAccumulator.Digest, resultingStateStep: 2);
        prefix.Validate(2);
        Require(prefix.TerminalOperationCount == Qa04ReferenceLoadV1.OperationCountForStep(0),
            "Step2 closed-prefix terminal count drifted.");

        var anchor = new HistoryAnchor(1, HashSuite.Hash256([1, 2, 3]));
        var batch = Qa04ScheduledOperationBatchAuthorityBuilderV1.Create(
            Qa04ReferenceLoadV1.WorldId,
            anchor,
            injectionStep: 0,
            effectiveStep: 1,
            bindings);
        Require(batch.OperationCount == checked((ulong)bindings.Length),
            "Step2 compact scheduled batch cardinality drifted.");
        Require(batch.ScheduledBatchDigest.Length == 32 && batch.History.RecordType == "qa04.operation-batch.scheduled.v1",
            "Step2 compact scheduled batch authority drifted.");

        var config = Qa04ReferenceConfigAuthorityV1.CreateCanonical();
        var configHistory = Qa04ConfigHistoryAuthorityV1.CreateInitial(1, config.Digest);
        Require(configHistory.Count == 1 && configHistory.Digest.Length == 32,
            "Step2 config-history accumulator did not bind the initial config authority.");

        VerifyCompleteTransitionAuthority(bindings, terminals, config, anchor);

        var gapRejected = false;
        try
        {
            terminalAccumulator.Append(3, terminalStep);
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.determinism-accumulator.ordinal-gap")
        {
            gapRejected = true;
        }
        Require(gapRejected, "Step2 determinism accumulator must fail closed on an ordinal gap.");
    }

    private static void VerifyCompleteTransitionAuthority(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        IReadOnlyList<TerminalOperationCommit> terminals,
        Qa04ReferenceConfigAuthorityV1 config,
        HistoryAnchor anchor)
    {
        var previousContinuity = HashSuite.Hash256([7, 7, 7]);
        var resultingStateDigest = HashSuite.Hash256([8, 8, 8]);
        var partitionDigest = HashSuite.Hash256([9, 9, 9]);
        var authority = Qa04TransitionCommittedAuthorityV1.Create(
            Qa04ReferenceLoadV1.WorldId,
            historySequence: 2,
            previousHistoryRecordDigest: anchor.Digest,
            effectiveStep: 1,
            resultingStep: 2,
            activeConfigGeneration: 1,
            activeConfigDigest: config.Digest,
            appliedOperationIds: bindings.Select(static binding => binding.SourceDescriptor.OperationId).ToArray(),
            operationOutcomes: terminals,
            previousStateContinuityToken: previousContinuity,
            stateDiagnosticHash: resultingStateDigest,
            partitionDigests:
            [
                new Qa04TransitionPartitionDigestV1("resident.identity", partitionDigest),
            ]);
        var decoded = Qa04TransitionCommittedAuthorityV1.DecodeAndValidate(authority.History);
        Qa04TransitionCommittedAuthorityV1.RequireEquivalent(
            decoded,
            authority,
            "qa04.transition-smoke.roundtrip-drift");
        Require(authority.History.NormalizedPayloadDigest.Length == 32 &&
                authority.ResultingStateContinuityToken.Length == 32,
            "Step2 complete transition authority did not materialize canonical digests.");

        var tamperedPhysical = authority.History.PayloadBytes.ToArray();
        tamperedPhysical[^1] ^= 0x01;
        var tamperedHistory = HistoryRecordMaterial.Create(
            authority.History.WorldId,
            authority.History.Sequence,
            authority.History.PreviousRecordDigest,
            authority.History.RecordType,
            authority.History.PayloadSchemaId,
            authority.History.PayloadSchemaMajor,
            authority.History.PayloadSchemaMinor,
            tamperedPhysical,
            writer => writer.WriteCanonicalValue(authority.History.NormalizedPayloadBytes));
        var tamperRejected = false;
        try
        {
            _ = Qa04TransitionCommittedAuthorityV1.DecodeAndValidate(tamperedHistory);
        }
        catch (InvalidDataException)
        {
            tamperRejected = true;
        }
        Require(tamperRejected,
            "Step2 transition decoder must fail closed when the physical wrapper drifts from semantic authority.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
