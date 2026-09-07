using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.InfrastructureInformation;

public sealed record InfrastructureEdgeV1(
    OpaqueId128 EdgeId,
    OpaqueId128 FromNodeId,
    OpaqueId128 ToNodeId,
    ulong Cost)
{
    public void Validate()
    {
        if (EdgeId.IsZero || FromNodeId.IsZero || ToNodeId.IsZero)
            throw new InvalidDataException("infrastructure.edge-id-zero");
        if (FromNodeId == ToNodeId)
            throw new InvalidDataException("infrastructure.edge-self-loop");
    }
}

public enum InfrastructureRouteStatusV1 : byte
{
    Found = 1,
    NoRoute = 2,
}

public sealed record InfrastructureRouteResultV1(
    InfrastructureRouteStatusV1 Status,
    ulong TotalCost,
    IReadOnlyList<OpaqueId128> NodeIds,
    IReadOnlyList<OpaqueId128> EdgeIds);

public static class DeterministicInfrastructureDijkstraV1
{
    public static InfrastructureRouteResultV1 FindRoute(
        OpaqueId128 startNodeId,
        OpaqueId128 goalNodeId,
        IEnumerable<InfrastructureEdgeV1> edges)
    {
        if (startNodeId.IsZero || goalNodeId.IsZero)
            throw new InvalidDataException("infrastructure.route-node-zero");
        ArgumentNullException.ThrowIfNull(edges);
        if (startNodeId == goalNodeId)
            return new InfrastructureRouteResultV1(
                InfrastructureRouteStatusV1.Found,
                0,
                Array.AsReadOnly(new[] { startNodeId }),
                Array.Empty<OpaqueId128>());

        var materialized = edges.ToArray();
        foreach (var edge in materialized) edge.Validate();
        if (materialized.Select(static edge => edge.EdgeId).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("infrastructure.edge-id-duplicate");

        var outgoing = materialized
            .GroupBy(static edge => edge.FromNodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static edge => edge.ToNodeId)
                    .ThenBy(static edge => edge.EdgeId)
                    .ToArray());

        var distances = new Dictionary<OpaqueId128, ulong> { [startNodeId] = 0 };
        var predecessor = new Dictionary<OpaqueId128, (OpaqueId128 Parent, OpaqueId128 Edge)>();
        var open = new SortedSet<RouteQueueEntryV1>(RouteQueueComparerV1.Instance)
        {
            new(0, startNodeId)
        };

        while (open.Count > 0)
        {
            var current = open.Min!;
            open.Remove(current);
            if (!distances.TryGetValue(current.NodeId, out var knownDistance) || knownDistance != current.Distance)
                continue;
            if (current.NodeId == goalNodeId)
                return BuildResult(startNodeId, goalNodeId, current.Distance, predecessor);
            if (!outgoing.TryGetValue(current.NodeId, out var currentEdges))
                continue;

            foreach (var edge in currentEdges)
            {
                ulong candidateDistance;
                try
                {
                    candidateDistance = checked(current.Distance + edge.Cost);
                }
                catch (OverflowException ex)
                {
                    throw new OverflowException("simulation.numeric-overflow", ex);
                }

                if (distances.TryGetValue(edge.ToNodeId, out var existingDistance) &&
                    candidateDistance >= existingDistance)
                    continue;

                distances[edge.ToNodeId] = candidateDistance;
                predecessor[edge.ToNodeId] = (current.NodeId, edge.EdgeId);
                open.Add(new RouteQueueEntryV1(candidateDistance, edge.ToNodeId));
            }
        }

        return new InfrastructureRouteResultV1(
            InfrastructureRouteStatusV1.NoRoute,
            0,
            Array.Empty<OpaqueId128>(),
            Array.Empty<OpaqueId128>());
    }

    private static InfrastructureRouteResultV1 BuildResult(
        OpaqueId128 start,
        OpaqueId128 goal,
        ulong totalCost,
        IReadOnlyDictionary<OpaqueId128, (OpaqueId128 Parent, OpaqueId128 Edge)> predecessor)
    {
        var nodes = new List<OpaqueId128> { goal };
        var edges = new List<OpaqueId128>();
        var cursor = goal;
        while (cursor != start)
        {
            if (!predecessor.TryGetValue(cursor, out var prior))
                throw new InvalidOperationException("infrastructure.route-predecessor-missing");
            edges.Add(prior.Edge);
            cursor = prior.Parent;
            nodes.Add(cursor);
        }
        nodes.Reverse();
        edges.Reverse();
        return new InfrastructureRouteResultV1(
            InfrastructureRouteStatusV1.Found,
            totalCost,
            Array.AsReadOnly(nodes.ToArray()),
            Array.AsReadOnly(edges.ToArray()));
    }

    private sealed record RouteQueueEntryV1(ulong Distance, OpaqueId128 NodeId);

    private sealed class RouteQueueComparerV1 : IComparer<RouteQueueEntryV1>
    {
        public static RouteQueueComparerV1 Instance { get; } = new();

        public int Compare(RouteQueueEntryV1? left, RouteQueueEntryV1? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var compare = left.Distance.CompareTo(right.Distance);
            if (compare != 0) return compare;
            return left.NodeId.CompareTo(right.NodeId);
        }
    }
}

public sealed record InfrastructureQueueRequestV1(
    OpaqueId128 RequestId,
    ulong EligibleStep,
    int SemanticPriority)
{
    public void Validate()
    {
        if (RequestId.IsZero) throw new InvalidDataException("infrastructure.queue-request-id-zero");
    }
}

public static class InfrastructureQueueOrderV1
{
    public static IReadOnlyList<InfrastructureQueueRequestV1> Canonicalize(
        IEnumerable<InfrastructureQueueRequestV1> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var materialized = requests.ToArray();
        foreach (var request in materialized) request.Validate();
        if (materialized.Select(static request => request.RequestId).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("infrastructure.queue-request-id-duplicate");
        return Array.AsReadOnly(materialized
            .OrderBy(static request => request.EligibleStep)
            .ThenBy(static request => request.SemanticPriority)
            .ThenBy(static request => request.RequestId)
            .ToArray());
    }
}

public sealed record WdrrRequestV1(OpaqueId128 RequestId, ulong Cost)
{
    public void Validate()
    {
        if (RequestId.IsZero) throw new InvalidDataException("infrastructure.wdrr-request-id-zero");
        if (Cost == 0) throw new InvalidDataException("infrastructure.wdrr-request-cost-zero");
    }
}

public sealed record WdrrParticipantV1(
    OpaqueId128 ParticipantId,
    uint Weight,
    IReadOnlyList<WdrrRequestV1> Requests)
{
    public void Validate()
    {
        if (ParticipantId.IsZero) throw new InvalidDataException("infrastructure.wdrr-participant-id-zero");
        if (Weight == 0) throw new InvalidDataException("infrastructure.wdrr-weight-zero");
        ArgumentNullException.ThrowIfNull(Requests);
        foreach (var request in Requests) request.Validate();
        if (Requests.Select(static request => request.RequestId).Distinct().Count() != Requests.Count)
            throw new InvalidDataException("infrastructure.wdrr-request-id-duplicate");
    }
}

public static class DeterministicWeightedDeficitRoundRobinV1
{
    public static IReadOnlyList<OpaqueId128> Allocate(
        IEnumerable<WdrrParticipantV1> participants,
        ulong baseQuantum,
        int maximumAllocations)
    {
        ArgumentNullException.ThrowIfNull(participants);
        if (baseQuantum == 0) throw new ArgumentOutOfRangeException(nameof(baseQuantum));
        if (maximumAllocations < 0) throw new ArgumentOutOfRangeException(nameof(maximumAllocations));

        var ordered = participants.OrderBy(static participant => participant.ParticipantId).ToArray();
        foreach (var participant in ordered) participant.Validate();
        if (ordered.Select(static participant => participant.ParticipantId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("infrastructure.wdrr-participant-id-duplicate");
        if (maximumAllocations == 0 || ordered.Length == 0)
            return Array.Empty<OpaqueId128>();

        var deficits = new UInt128[ordered.Length];
        var positions = new int[ordered.Length];
        var result = new List<OpaqueId128>();
        while (result.Count < maximumAllocations)
        {
            var progress = false;
            for (var index = 0; index < ordered.Length && result.Count < maximumAllocations; index++)
            {
                var participant = ordered[index];
                if (positions[index] >= participant.Requests.Count) continue;
                deficits[index] += (UInt128)baseQuantum * participant.Weight;
                while (positions[index] < participant.Requests.Count && result.Count < maximumAllocations)
                {
                    var request = participant.Requests[positions[index]];
                    if ((UInt128)request.Cost > deficits[index]) break;
                    deficits[index] -= request.Cost;
                    positions[index]++;
                    result.Add(request.RequestId);
                    progress = true;
                }
            }
            if (!progress && ordered.All((participant, index) => positions[index] >= participant.Requests.Count))
                break;
        }
        return Array.AsReadOnly(result.ToArray());
    }
}

public static class FixedIterationJacobiV1
{
    public const int StandardInfrastructureIterations = 32;

    public static IReadOnlyList<long> Solve(
        IReadOnlyList<long> initial,
        Func<int, IReadOnlyList<long>, long> update,
        int iterations = StandardInfrastructureIterations)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(update);
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));
        var previous = initial.ToArray();
        var next = new long[previous.Length];
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var frozenPrevious = Array.AsReadOnly(previous);
            for (var index = 0; index < previous.Length; index++)
                next[index] = update(index, frozenPrevious);
            (previous, next) = (next, previous);
        }
        return Array.AsReadOnly(previous);
    }
}

public sealed record InfrastructureDependencyV1(
    OpaqueId128 DependencyId,
    OpaqueId128 ProviderId,
    OpaqueId128 ConsumerId)
{
    public void Validate()
    {
        if (DependencyId.IsZero || ProviderId.IsZero || ConsumerId.IsZero)
            throw new InvalidDataException("infrastructure.dependency-id-zero");
        if (ProviderId == ConsumerId)
            throw new InvalidDataException("infrastructure.dependency-self-cycle");
    }
}

public static class DeterministicOutageCascadeV1
{
    public static IReadOnlyList<OpaqueId128> Propagate(
        IEnumerable<OpaqueId128> initiallyFailed,
        IEnumerable<InfrastructureDependencyV1> dependencies,
        int maximumAffected)
    {
        ArgumentNullException.ThrowIfNull(initiallyFailed);
        ArgumentNullException.ThrowIfNull(dependencies);
        if (maximumAffected <= 0) throw new ArgumentOutOfRangeException(nameof(maximumAffected));
        var edges = dependencies.ToArray();
        foreach (var edge in edges) edge.Validate();
        EnsureDag(edges);
        var failed = new SortedSet<OpaqueId128>(initiallyFailed);
        if (failed.Any(static id => id.IsZero))
            throw new InvalidDataException("infrastructure.outage-id-zero");
        if (failed.Count > maximumAffected)
            throw new InvalidDataException("infrastructure.outage-cascade-budget-exceeded");

        var outgoing = edges
            .GroupBy(static edge => edge.ProviderId)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static edge => edge.ConsumerId).ThenBy(static edge => edge.DependencyId).ToArray());
        var queue = new Queue<OpaqueId128>(failed);
        while (queue.Count > 0)
        {
            var provider = queue.Dequeue();
            if (!outgoing.TryGetValue(provider, out var providerEdges)) continue;
            foreach (var edge in providerEdges)
            {
                if (!failed.Add(edge.ConsumerId)) continue;
                if (failed.Count > maximumAffected)
                    throw new InvalidDataException("infrastructure.outage-cascade-budget-exceeded");
                queue.Enqueue(edge.ConsumerId);
            }
        }
        return Array.AsReadOnly(failed.ToArray());
    }

    private static void EnsureDag(IReadOnlyList<InfrastructureDependencyV1> edges)
    {
        var nodes = edges.SelectMany(static edge => new[] { edge.ProviderId, edge.ConsumerId }).Distinct().ToArray();
        var indegree = nodes.ToDictionary(static node => node, static _ => 0);
        var outgoing = nodes.ToDictionary(static node => node, static _ => new List<OpaqueId128>());
        foreach (var edge in edges)
        {
            outgoing[edge.ProviderId].Add(edge.ConsumerId);
            indegree[edge.ConsumerId]++;
        }
        var ready = new SortedSet<OpaqueId128>(indegree.Where(static pair => pair.Value == 0).Select(static pair => pair.Key));
        var visited = 0;
        while (ready.Count > 0)
        {
            var node = ready.Min;
            ready.Remove(node);
            visited++;
            foreach (var consumer in outgoing[node].Order())
            {
                indegree[consumer]--;
                if (indegree[consumer] == 0) ready.Add(consumer);
            }
        }
        if (visited != nodes.Length)
            throw new InvalidDataException("infrastructure.dependency-cycle-requires-coupled-policy");
    }
}

public sealed record InformationDeliveryFactV1(
    OpaqueId128 DeliveryId,
    OpaqueId128 RecipientRef,
    OpaqueId128 ContentOrClaimRef,
    ulong DeliveredStep)
{
    public void Validate()
    {
        if (DeliveryId.IsZero || RecipientRef.IsZero || ContentOrClaimRef.IsZero)
            throw new InvalidDataException("information.delivery-id-zero");
    }
}

public sealed class InformationDeliveryProjectionV1
{
    private readonly List<InformationDeliveryFactV1> _delivered = [];
    public IReadOnlyList<InformationDeliveryFactV1> Delivered => _delivered.AsReadOnly();
    public int ResidentBeliefMutations => 0;

    public void RecordDelivered(InformationDeliveryFactV1 delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        delivery.Validate();
        if (_delivered.Any(item => item.DeliveryId == delivery.DeliveryId))
            throw new InvalidDataException("information.delivery-id-duplicate");
        _delivered.Add(delivery);
        _delivered.Sort(static (left, right) => left.DeliveryId.CompareTo(right.DeliveryId));
    }
}
