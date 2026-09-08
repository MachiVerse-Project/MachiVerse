using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

public enum OperationLifecycleStateV1
{
    Unseen = 0,
    AcceptedDurable = 1,
    ScheduledDurable = 2,
    TerminalDurable = 3,
}

public enum CoreOperationResultStatusV1
{
    Success = 1,
    Accepted = 2,
    Pending = 3,
    NoChange = 4,
    Duplicate = 5,
    Rejected = 6,
    Failed = 7,
}

public static class OperationLifecycleRulesV1
{
    public static void RequireTransition(
        OperationLifecycleStateV1 current,
        OperationLifecycleStateV1 next,
        CoreOperationResultStatusV1? directTerminalStatus = null)
    {
        var valid = (current, next) switch
        {
            (OperationLifecycleStateV1.Unseen, OperationLifecycleStateV1.AcceptedDurable) => true,
            (OperationLifecycleStateV1.AcceptedDurable, OperationLifecycleStateV1.ScheduledDurable) => true,
            (OperationLifecycleStateV1.ScheduledDurable, OperationLifecycleStateV1.TerminalDurable) => true,
            (OperationLifecycleStateV1.Unseen, OperationLifecycleStateV1.TerminalDurable)
                => directTerminalStatus == CoreOperationResultStatusV1.Rejected,
            _ => false,
        };

        if (!valid)
            throw new InvalidDataException($"operation.lifecycle-transition-invalid:{current}:{next}");
    }

    public static bool IsTerminalResult(CoreOperationResultStatusV1 status)
        => status is CoreOperationResultStatusV1.Success
            or CoreOperationResultStatusV1.NoChange
            or CoreOperationResultStatusV1.Rejected
            or CoreOperationResultStatusV1.Failed;
}

public sealed class OperationDedupTombstoneV1
{
    public OperationDedupTombstoneV1(
        OpaqueId128 operationId,
        ReadOnlySpan<byte> operationPayloadDigest,
        CoreOperationResultStatusV1 terminalStatus,
        StableToken resultCode,
        ulong? effectiveStep,
        ulong terminalHistorySequence)
    {
        if (operationId.IsZero)
            throw new ArgumentException("OperationId ZERO is invalid.", nameof(operationId));
        if (operationPayloadDigest.Length != 32)
            throw new ArgumentException("Operation payload digest must be exactly 32 bytes.", nameof(operationPayloadDigest));
        if (!OperationLifecycleRulesV1.IsTerminalResult(terminalStatus))
            throw new ArgumentException("Dedup tombstone requires a terminal result status.", nameof(terminalStatus));
        if (terminalHistorySequence == 0)
            throw new ArgumentOutOfRangeException(nameof(terminalHistorySequence), "HistorySequence starts at 1.");

        OperationId = operationId;
        OperationPayloadDigest = operationPayloadDigest.ToArray();
        TerminalStatus = terminalStatus;
        ResultCode = resultCode;
        EffectiveStep = effectiveStep;
        TerminalHistorySequence = terminalHistorySequence;
    }

    public OpaqueId128 OperationId { get; }
    public byte[] OperationPayloadDigest { get; }
    public CoreOperationResultStatusV1 TerminalStatus { get; }
    public StableToken ResultCode { get; }
    public ulong? EffectiveStep { get; }
    public ulong TerminalHistorySequence { get; }
}
