using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

/// <summary>
/// Immutable external-input boundary for transition State(S) -> State(S+1).
/// Scheduled Operations are copied into canonical SameStepOrderKey order at freeze time.
/// </summary>
public sealed class FrozenStepInputV1
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

    public FrozenStepInputV1(
        OpaqueId128 worldId,
        ulong basisStep,
        ulong configGeneration,
        byte[] basisStateDigest,
        IEnumerable<ScheduledOperationRefV1> scheduledOperations)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        if (configGeneration == 0) throw new ArgumentOutOfRangeException(nameof(configGeneration));
        ArgumentNullException.ThrowIfNull(basisStateDigest);
        if (basisStateDigest.Length != 32)
            throw new ArgumentException("Basis state digest must be exactly 32 bytes.", nameof(basisStateDigest));
        ArgumentNullException.ThrowIfNull(scheduledOperations);
        if (basisStep == ulong.MaxValue)
            throw new InvalidDataException("step.target-overflow");

        var operations = scheduledOperations
            .OrderBy(static item => item, ScheduledComparer.Instance)
            .ToArray();
        var ids = new HashSet<OpaqueId128>();
        foreach (var operation in operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            operation.Validate();
            if (operation.EffectiveStep != basisStep)
                throw new InvalidDataException("step.frozen-operation-effective-step-mismatch");
            if (!ids.Add(operation.OperationId))
                throw new InvalidDataException("step.frozen-operation-duplicate");
        }

        WorldId = worldId;
        BasisStep = basisStep;
        TargetStep = basisStep + 1;
        ConfigGeneration = configGeneration;
        BasisStateDigest = basisStateDigest.ToArray();
        ScheduledOperations = Array.AsReadOnly(operations);
    }

    public OpaqueId128 WorldId { get; }
    public ulong BasisStep { get; }
    public ulong TargetStep { get; }
    public ulong ConfigGeneration { get; }
    public byte[] BasisStateDigest { get; }
    public IReadOnlyList<ScheduledOperationRefV1> ScheduledOperations { get; }

    public static FrozenStepInputV1 Capture(
        OpaqueId128 worldId,
        ulong basisStep,
        ulong configGeneration,
        byte[] basisStateDigest,
        OperationSchedulerStateV1 scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (scheduler.NextSchedulableStep != basisStep)
            throw new InvalidDataException("step.freeze-barrier-mismatch");

        // Validate all non-scheduler inputs before advancing the scheduler barrier.
        _ = new FrozenStepInputV1(
            worldId,
            basisStep,
            configGeneration,
            basisStateDigest,
            scheduler.ForEffectiveStep(basisStep));

        scheduler.FreezeExternalInput(basisStep);
        return new FrozenStepInputV1(
            worldId,
            basisStep,
            configGeneration,
            basisStateDigest,
            scheduler.ForEffectiveStep(basisStep));
    }
}

public sealed class DomainExecutionDescriptorV1
{
    public DomainExecutionDescriptorV1(
        StableToken domainToken,
        ushort domainRank,
        IEnumerable<StableToken>? afterDomains = null,
        IEnumerable<StableToken>? ownedPartitions = null)
    {
        if (string.IsNullOrEmpty(domainToken.Value))
            throw new ArgumentException("Domain token is required.", nameof(domainToken));

        DomainToken = domainToken;
        DomainRank = domainRank;
        AfterDomains = CanonicalTokens(afterDomains, "step.execution-dependency-duplicate");
        OwnedPartitions = CanonicalTokens(ownedPartitions, "step.execution-owned-partition-duplicate");
        if (AfterDomains.Contains(domainToken))
            throw new InvalidDataException("step.execution-self-dependency");
    }

    public StableToken DomainToken { get; }
    public ushort DomainRank { get; }
    public IReadOnlyList<StableToken> AfterDomains { get; }
    public IReadOnlyList<StableToken> OwnedPartitions { get; }

    private static IReadOnlyList<StableToken> CanonicalTokens(
        IEnumerable<StableToken>? values,
        string duplicateCode)
    {
        if (values is null) return Array.Empty<StableToken>();
        var ordered = values.OrderBy(static token => token.Value, StringComparer.Ordinal).ToArray();
        if (ordered.Select(static token => token.Value).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidDataException(duplicateCode);
        return Array.AsReadOnly(ordered);
    }
}

/// <summary>
/// Deterministic Kahn-style domain execution plan. Selectable domains are tie-broken by
/// DomainToken ASCII ordinal order; discovery/hash-map order is never semantic.
/// </summary>
public sealed class DomainExecutionPlanV1
{
    private DomainExecutionPlanV1(IReadOnlyList<DomainExecutionDescriptorV1> orderedDomains)
    {
        OrderedDomains = orderedDomains;
    }

    public IReadOnlyList<DomainExecutionDescriptorV1> OrderedDomains { get; }

    public static DomainExecutionPlanV1 Build(IEnumerable<DomainExecutionDescriptorV1> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var all = descriptors.ToArray();
        if (all.Length == 0)
            throw new ArgumentException("At least one domain descriptor is required.", nameof(descriptors));

        var byToken = new Dictionary<string, DomainExecutionDescriptorV1>(StringComparer.Ordinal);
        var ranks = new HashSet<ushort>();
        foreach (var descriptor in all)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            if (!byToken.TryAdd(descriptor.DomainToken.Value, descriptor))
                throw new InvalidDataException("step.execution-domain-duplicate");
            if (!ranks.Add(descriptor.DomainRank))
                throw new InvalidDataException("step.execution-domain-rank-duplicate");
        }

        foreach (var descriptor in all)
        {
            foreach (var dependency in descriptor.AfterDomains)
            {
                if (!byToken.ContainsKey(dependency.Value))
                    throw new InvalidDataException($"step.execution-dependency-unknown:{dependency.Value}");
            }
        }

        var indegree = all.ToDictionary(
            static descriptor => descriptor.DomainToken.Value,
            static descriptor => descriptor.AfterDomains.Count,
            StringComparer.Ordinal);
        var dependents = all.ToDictionary(
            static descriptor => descriptor.DomainToken.Value,
            static _ => new List<string>(),
            StringComparer.Ordinal);
        foreach (var descriptor in all)
        {
            foreach (var dependency in descriptor.AfterDomains)
                dependents[dependency.Value].Add(descriptor.DomainToken.Value);
        }
        foreach (var values in dependents.Values)
            values.Sort(StringComparer.Ordinal);

        var selectable = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in all)
        {
            if (indegree[descriptor.DomainToken.Value] == 0)
                selectable.Add(descriptor.DomainToken.Value);
        }

        var ordered = new List<DomainExecutionDescriptorV1>(all.Length);
        while (selectable.Count != 0)
        {
            var token = selectable.Min!;
            selectable.Remove(token);
            ordered.Add(byToken[token]);
            foreach (var dependent in dependents[token])
            {
                var next = indegree[dependent] - 1;
                indegree[dependent] = next;
                if (next == 0) selectable.Add(dependent);
            }
        }

        if (ordered.Count != all.Length)
            throw new InvalidDataException("step.execution-dependency-cycle");

        return new DomainExecutionPlanV1(Array.AsReadOnly(ordered.ToArray()));
    }
}

/// <summary>
/// Read-only runtime context handed to a domain execution slot. It intentionally exposes no
/// foreign-domain mutation handle; cross-domain effects must leave the domain as MutationIntents.
/// </summary>
public sealed record DomainRuntimeContextV1(
    FrozenStepInputV1 FrozenInput,
    DomainExecutionDescriptorV1 Domain)
{
    public OpaqueId128 WorldId => FrozenInput.WorldId;
    public ulong BasisStep => FrozenInput.BasisStep;
    public ulong TargetStep => FrozenInput.TargetStep;
    public ulong ConfigGeneration => FrozenInput.ConfigGeneration;
}
