using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04AcceptedOperationExpectationV1(
    OpaqueId128 OperationId,
    byte[] PayloadDigest);

public sealed record Qa04AcceptedOperationLossGuardReceiptV1(
    int ExpectedAcceptedCount,
    int AcceptedDurableCount,
    int ScheduledDurableCount,
    int TerminalDurableCount)
{
    public bool Passed =>
        ExpectedAcceptedCount == AcceptedDurableCount + ScheduledDurableCount + TerminalDurableCount;
}

/// <summary>
/// durable ACCEPTED を返した Operation が、後続の scheduled / terminal 遷移や recovery 対象の
/// operation_state から消失していないことを検証する QA-04 用の fail-closed guard。
///
/// Phase 1 の authoritative lifecycle
/// UNSEEN -> ACCEPTED_DURABLE -> SCHEDULED_DURABLE -> TERMINAL_DURABLE
/// にのみ依存し、benchmark 固有の scheduling policy や payload semantics は追加しない。
/// UNSEEN -> TERMINAL_DURABLE の direct reject は durable ACCEPTED ではないため、本 guard の
/// accepted expectation を満たすものとして扱わない。
/// </summary>
public static class Qa04AcceptedOperationLossGuardV1
{
    /// <summary>
    /// canonical QA-04 Operation descriptor から、durable acceptance 後も失ってはならない
    /// identity/digest expectation だけを抽出する。Domain、effective Step、SameStepOrderKey 等の
    /// 未確定 workload binding はここでは決定しない。
    /// </summary>
    public static IReadOnlyList<Qa04AcceptedOperationExpectationV1> FromCanonicalDescriptors(
        IEnumerable<Qa04OperationDescriptorV1> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        return Array.AsReadOnly(descriptors
            .Select(static descriptor => new Qa04AcceptedOperationExpectationV1(
                descriptor.OperationId,
                descriptor.PayloadDigest.ToArray()))
            .ToArray());
    }

    /// <summary>
    /// production SQLite の canonical operation_state 一覧を読み、同じ fail-closed guard を適用する。
    /// Snapshot/recovery の代替ではなく、durable operation authority の観測境界としてのみ使用する。
    /// </summary>
    public static async Task<Qa04AcceptedOperationLossGuardReceiptV1> ValidateStoreAsync(
        IEnumerable<Qa04AcceptedOperationExpectationV1> acceptedExpectations,
        SqlitePersistenceStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var durableStates = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        return Validate(acceptedExpectations, durableStates);
    }

    public static Qa04AcceptedOperationLossGuardReceiptV1 Validate(
        IEnumerable<Qa04AcceptedOperationExpectationV1> acceptedExpectations,
        IEnumerable<DurableOperationStateV1> durableStates)
    {
        ArgumentNullException.ThrowIfNull(acceptedExpectations);
        ArgumentNullException.ThrowIfNull(durableStates);

        var expected = acceptedExpectations.ToArray();
        var actual = durableStates.ToArray();

        if (expected.Select(static item => item.OperationId).Distinct().Count() != expected.Length)
            throw new InvalidDataException("qa04.operation-loss.expected-id-duplicate");
        if (actual.Select(static item => item.OperationId).Distinct().Count() != actual.Length)
            throw new InvalidDataException("qa04.operation-loss.actual-id-duplicate");

        var actualById = actual.ToDictionary(static item => item.OperationId);
        var acceptedCount = 0;
        var scheduledCount = 0;
        var terminalCount = 0;

        foreach (var expectation in expected)
        {
            if (expectation.OperationId.IsZero)
                throw new InvalidDataException("qa04.operation-loss.expected-id-zero");
            ArgumentNullException.ThrowIfNull(expectation.PayloadDigest);
            if (expectation.PayloadDigest.Length != 32)
                throw new InvalidDataException("qa04.operation-loss.expected-digest-length");

            if (!actualById.TryGetValue(expectation.OperationId, out var state))
                throw new InvalidDataException("qa04.operation-loss.accepted-operation-missing");
            if (!CryptographicOperations.FixedTimeEquals(expectation.PayloadDigest, state.OperationPayloadDigest))
                throw new InvalidDataException("qa04.operation-loss.payload-digest-mismatch");
            if (state.AcceptedSequence is null)
                throw new InvalidDataException("qa04.operation-loss.accepted-sequence-missing");

            switch (state.Lifecycle)
            {
                case DurableOperationLifecycleV1.AcceptedDurable:
                    acceptedCount++;
                    break;
                case DurableOperationLifecycleV1.ScheduledDurable:
                    scheduledCount++;
                    break;
                case DurableOperationLifecycleV1.TerminalDurable:
                    terminalCount++;
                    break;
                default:
                    throw new InvalidDataException("qa04.operation-loss.lifecycle-invalid");
            }
        }

        var receipt = new Qa04AcceptedOperationLossGuardReceiptV1(
            expected.Length,
            acceptedCount,
            scheduledCount,
            terminalCount);
        if (!receipt.Passed)
            throw new InvalidDataException("qa04.operation-loss.guard-failed");
        return receipt;
    }
}
