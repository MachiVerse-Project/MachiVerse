using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

public sealed record ScheduledOperationRefV1(
    OpaqueId128 OperationId,
    ulong EffectiveStep,
    SameStepOrderKey OrderKey)
{
    public void Validate()
    {
        if (OperationId.IsZero)
            throw new InvalidDataException("operation.scheduler-operation-id-zero");
        ArgumentNullException.ThrowIfNull(OrderKey);
    }
}

public sealed class OperationSchedulerStateV1
{
    private sealed class ScheduledComparer : IComparer<ScheduledOperationRefV1>
    {
        public static ScheduledComparer Instance { get; } = new();

        public int Compare(ScheduledOperationRefV1? left, ScheduledOperationRefV1? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var order = left.OrderKey.CompareTo(right.OrderKey);
            return order != 0 ? order : left.OperationId.CompareTo(right.OperationId);
        }
    }

    private readonly SortedDictionary<ulong, SortedSet<ScheduledOperationRefV1>> _byEffectiveStep = new();
    private readonly HashSet<OpaqueId128> _operationIds = [];

    public OperationSchedulerStateV1(
        ulong nextSchedulableStep,
        ulong? freezeStep,
        IEnumerable<ScheduledOperationRefV1>? scheduled = null)
    {
        if (freezeStep is { } freeze && nextSchedulableStep <= freeze)
            throw new InvalidDataException("operation.scheduler-barrier-invalid");
        NextSchedulableStep = nextSchedulableStep;
        FreezeStep = freezeStep;

        if (scheduled is null) return;
        foreach (var item in scheduled)
            AddDurable(item);
    }

    public ulong NextSchedulableStep { get; private set; }
    public ulong? FreezeStep { get; private set; }

    public IReadOnlyList<ScheduledOperationRefV1> ForEffectiveStep(ulong effectiveStep)
        => _byEffectiveStep.TryGetValue(effectiveStep, out var bucket)
            ? bucket.ToArray()
            : Array.Empty<ScheduledOperationRefV1>();

    public IEnumerable<KeyValuePair<ulong, IReadOnlyList<ScheduledOperationRefV1>>> CanonicalBuckets
        => _byEffectiveStep.Select(static pair =>
            new KeyValuePair<ulong, IReadOnlyList<ScheduledOperationRefV1>>(pair.Key, pair.Value.ToArray()));

    public void AddDurable(ScheduledOperationRefV1 scheduled)
    {
        ArgumentNullException.ThrowIfNull(scheduled);
        scheduled.Validate();

        var minimumRetainedStep = FreezeStep ?? NextSchedulableStep;
        if (scheduled.EffectiveStep < minimumRetainedStep)
            throw new InvalidDataException("operation.scheduler-past-effective-step");

        if (!_operationIds.Add(scheduled.OperationId))
            throw new InvalidDataException("operation.scheduler-duplicate-operation");

        if (!_byEffectiveStep.TryGetValue(scheduled.EffectiveStep, out var bucket))
        {
            bucket = new SortedSet<ScheduledOperationRefV1>(ScheduledComparer.Instance);
            _byEffectiveStep.Add(scheduled.EffectiveStep, bucket);
        }
        if (!bucket.Add(scheduled))
        {
            _operationIds.Remove(scheduled.OperationId);
            throw new InvalidDataException("operation.scheduler-duplicate-order-key");
        }
    }

    public void FreezeExternalInput(ulong step)
    {
        if (FreezeStep is not null)
            throw new InvalidDataException("operation.scheduler-already-frozen");
        if (step != NextSchedulableStep)
            throw new InvalidDataException("operation.scheduler-freeze-step-mismatch");
        if (step == ulong.MaxValue)
            throw new InvalidDataException("operation.scheduling-overflow");
        FreezeStep = step;
        NextSchedulableStep = step + 1;
    }

    /// <summary>
    /// Advances the scheduler only after transition durability is established. Operations for the
    /// finalized transition are removed from the in-memory scheduler at the same semantic boundary;
    /// future-step operations remain queued in canonical order.
    /// </summary>
    public void OpenAfterFinalization(ulong finalizedStep)
    {
        if (FreezeStep is { } frozen && finalizedStep != frozen)
            throw new InvalidDataException("operation.scheduler-finalized-step-mismatch");
        if (finalizedStep == ulong.MaxValue)
            throw new InvalidDataException("operation.scheduling-overflow");

        if (_byEffectiveStep.Remove(finalizedStep, out var finalizedBucket))
        {
            foreach (var operation in finalizedBucket)
                _operationIds.Remove(operation.OperationId);
        }

        FreezeStep = null;
        var minimumNextSchedulableStep = finalizedStep + 1;
        if (NextSchedulableStep < minimumNextSchedulableStep)
            NextSchedulableStep = minimumNextSchedulableStep;
    }
}
