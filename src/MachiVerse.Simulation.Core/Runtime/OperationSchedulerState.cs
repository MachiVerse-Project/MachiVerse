using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

public sealed record OperationSchedulingPolicyEpochV1(
    ulong EffectiveFromStep,
    ulong? EffectiveUntilStep,
    OperationSchedulingPolicyV1 Policy)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Policy);
        if (EffectiveUntilStep is { } until && until < EffectiveFromStep)
            throw new InvalidDataException("operation.scheduling-policy-range-invalid");
    }

    public bool Contains(ulong step)
        => step >= EffectiveFromStep && (EffectiveUntilStep is null || step <= EffectiveUntilStep.Value);
}

public sealed class OperationSchedulingPolicyCatalogV1
{
    private readonly IReadOnlyDictionary<ulong, OperationSchedulingPolicyEpochV1> _byGeneration;

    public OperationSchedulingPolicyCatalogV1(IEnumerable<OperationSchedulingPolicyEpochV1> epochs)
    {
        ArgumentNullException.ThrowIfNull(epochs);
        var ordered = epochs
            .OrderBy(static epoch => epoch.EffectiveFromStep)
            .ThenBy(static epoch => epoch.Policy.OwnerConfigGeneration)
            .ToArray();
        if (ordered.Length == 0)
            throw new ArgumentException("At least one scheduling policy epoch is required.", nameof(epochs));

        for (var index = 0; index < ordered.Length; index++)
        {
            ordered[index].Validate();
            if (index > 0)
            {
                var previous = ordered[index - 1];
                if (previous.EffectiveUntilStep is null || previous.EffectiveUntilStep.Value >= ordered[index].EffectiveFromStep)
                    throw new InvalidDataException("operation.scheduling-policy-range-overlap");
            }
        }

        if (ordered.Select(static epoch => epoch.Policy.OwnerConfigGeneration).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("operation.scheduling-policy-generation-duplicate");

        _byGeneration = ordered.ToDictionary(static epoch => epoch.Policy.OwnerConfigGeneration);
    }

    public OperationSchedulingPolicyV1 Resolve(OperationSchedulingAdmissionV1 admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (!_byGeneration.TryGetValue(admission.SchedulingPolicyGeneration, out var epoch))
            throw new InvalidDataException("operation.scheduling-policy-generation-unknown");
        if (!epoch.Contains(admission.AdmissionBasisStep))
            throw new InvalidDataException("operation.scheduling-policy-not-effective-at-basis");
        return epoch.Policy;
    }
}

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

    public void OpenAfterFinalization(ulong finalizedStep)
    {
        if (FreezeStep is { } frozen && finalizedStep != frozen)
            throw new InvalidDataException("operation.scheduler-finalized-step-mismatch");
        FreezeStep = null;
        if (NextSchedulableStep < finalizedStep)
            NextSchedulableStep = finalizedStep;
    }
}
