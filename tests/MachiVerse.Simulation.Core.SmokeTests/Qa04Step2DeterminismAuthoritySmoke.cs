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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
