using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;

internal static class Qa04AcceptedOperationLossGuardSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var canonicalDescriptors = Qa04ReferenceLoadV1.OperationsForStep(1).ToArray();
        var canonicalExpectations = Qa04AcceptedOperationLossGuardV1.FromCanonicalDescriptors(canonicalDescriptors);
        Require(canonicalDescriptors.Length == 5_000 && canonicalExpectations.Count == 5_000,
            "QA-04 steady 5,000 Operation descriptors must map one-to-one into accepted-loss expectations.");
        Require(canonicalExpectations.Select(static item => item.OperationId).Distinct().Count() == 5_000,
            "QA-04 accepted-loss expectations must preserve canonical OperationId uniqueness.");
        Require(canonicalExpectations.Zip(canonicalDescriptors).All(static pair =>
                pair.First.OperationId == pair.Second.OperationId &&
                pair.First.PayloadDigest.SequenceEqual(pair.Second.PayloadDigest)),
            "QA-04 accepted-loss expectations must preserve canonical OperationId/payload digest exactly.");

        var operationA = OpaqueId128.Parse("0000000000000000000000000000aa01");
        var operationB = OpaqueId128.Parse("0000000000000000000000000000aa02");
        var digestA = Enumerable.Repeat((byte)0x11, 32).ToArray();
        var digestB = Enumerable.Repeat((byte)0x22, 32).ToArray();
        var expected = new[]
        {
            new Qa04AcceptedOperationExpectationV1(operationA, digestA),
            new Qa04AcceptedOperationExpectationV1(operationB, digestB),
        };

        var intermediate = Qa04AcceptedOperationLossGuardV1.Validate(
            expected,
            new[]
            {
                State(operationA, digestA, DurableOperationLifecycleV1.TerminalDurable, 2, 3, 0, 6),
                State(operationB, digestB, DurableOperationLifecycleV1.ScheduledDurable, 4, 5, 1, null),
            });
        Require(intermediate.Passed &&
                intermediate.ExpectedAcceptedCount == 2 &&
                intermediate.ScheduledDurableCount == 1 &&
                intermediate.TerminalDurableCount == 1,
            "QA-04 accepted Operation loss guard must preserve accepted custody across scheduled/terminal states.");

        var terminal = Qa04AcceptedOperationLossGuardV1.Validate(
            expected,
            new[]
            {
                State(operationA, digestA, DurableOperationLifecycleV1.TerminalDurable, 2, 3, 0, 6),
                State(operationB, digestB, DurableOperationLifecycleV1.TerminalDurable, 4, 5, 1, 8),
            });
        Require(terminal.Passed && terminal.TerminalDurableCount == 2,
            "QA-04 accepted Operation loss guard must accept complete terminal durable custody.");

        ExpectFailure(
            () => Qa04AcceptedOperationLossGuardV1.Validate(expected, new[]
            {
                State(operationA, digestA, DurableOperationLifecycleV1.TerminalDurable, 2, 3, 0, 6),
            }),
            "qa04.operation-loss.accepted-operation-missing");

        ExpectFailure(
            () => Qa04AcceptedOperationLossGuardV1.Validate(expected, new[]
            {
                State(operationA, digestA, DurableOperationLifecycleV1.TerminalDurable, 2, 3, 0, 6),
                State(operationB, Enumerable.Repeat((byte)0x33, 32).ToArray(), DurableOperationLifecycleV1.ScheduledDurable, 4, 5, 1, null),
            }),
            "qa04.operation-loss.payload-digest-mismatch");

        ExpectFailure(
            () => Qa04AcceptedOperationLossGuardV1.Validate(
                [new Qa04AcceptedOperationExpectationV1(operationA, digestA)],
                [State(operationA, digestA, DurableOperationLifecycleV1.TerminalDurable, null, null, null, 2)]),
            "qa04.operation-loss.accepted-sequence-missing");
    }

    private static DurableOperationStateV1 State(
        OpaqueId128 operationId,
        byte[] digest,
        DurableOperationLifecycleV1 lifecycle,
        ulong? acceptedSequence,
        ulong? scheduledSequence,
        ulong? effectiveStep,
        ulong? terminalSequence)
        => new(
            operationId,
            digest,
            lifecycle,
            acceptedSequence,
            scheduledSequence,
            effectiveStep,
            terminalSequence,
            terminalSequence is null ? null : 1,
            terminalSequence is null ? null : "qa04.operation.success",
            null);

    private static void ExpectFailure(Action action, string expectedCode)
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected failure was not raised: {expectedCode}");
        }
        catch (InvalidDataException ex) when (ex.Message == expectedCode)
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
