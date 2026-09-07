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
            throw new InvalidDataException("infrastructure.edge-self");
    }
}

public sealed record InfrastructureRouteV1(
    bool Found,
    ulong TotalCost,
    IReadOnlyList<OpaqueId128> NodeIds,
    IReadOnlyList<OpaqueId128> EdgeIds);

public static class DeterministicDijkstraV1
{
    public static InfrastructureRouteV1 FindRoute(
        OpaqueId128 startNodeId,
        OpaqueId128 goalNodeId,
        IEnumerable<InfrastructureEdgeV1> edges)
    {
        if (startNodeId.IsZero || goalNodeId.IsZero)
            throw new InvalidDataException("infrastructure.route-node-id-zero");
        ArgumentNullException.ThrowIfNull(edges);
        if (startNodeId == goalNodeId)
            return new InfrastructureRouteV1(true, 0, [startNodeId], Array.Empty<OpaqueId128>());

        var materialized = edges.Select(edge =>
        {
            ArgumentNullException.ThrowIfNull(edge);
            edge.Validate();
            return edge;
        }).ToArray();
        if (materialized.Select(static edge => edge.EdgeId).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("infrastructure.edge-id-duplicate");

        var outgoing = materialized
            .GroupBy(static edge => edge.FromNodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static edge => edge.ToNodeId).ThenBy(static edge => edge.EdgeId).ToArray());
        var queue = new SortedSet<RouteQueueEntryV1>(RouteQueueComparerV1.Instance)
        {
            new(0, startNodeId)
        };
        var distances = new Dictionary<OpaqueId128, ulong> { [startNodeId] = 0 };
        var previous = new Dictionary<OpaqueId128, (OpaqueId128 ParentNodeId, OpaqueId128 EdgeId)>();

        while (queue.Count != 0)
        {
            var current = queue.Min!;
            queue.Remove(current);
            if (!distances.TryGetValue(current.NodeId, out var knownDistance) || knownDistance != current.Distance)
                continue;
            if (current.NodeId == goalNodeId)
                return BuildRoute(startNodeId, goalNodeId, current.Distance, previous);
            if (!outgoing.TryGetValue(current.NodeId, out var nextEdges))
                continue;

            foreach (var edge in nextEdges)
            {
                ulong nextDistance;
                try
                {
                    nextDistance = checked(current.Distance + edge.Cost);
                }
                catch (OverflowException ex)
                {
                    throw new OverflowException("simulation.numeric-overflow", ex);
                }

                if (distances.TryGetValue(edge.ToNodeId, out var existing) && nextDistance >= existing)
                    continue;
                distances[edge.ToNodeId] = nextDistance;
                previous[edge.ToNodeId] = (current.NodeId, edge.EdgeId);
                queue.Add(new RouteQueueEntryV1(nextDistance, edge.ToNodeId));
            }
        }

        return new InfrastructureRouteV1(false, 0, Array.Empty<OpaqueId128>(), Array.Empty<OpaqueId128>());
    }

    private static InfrastructureRouteV1 BuildRoute(
        OpaqueId128 startNodeId,
        OpaqueId128 goalNodeId,
        ulong totalCost,
        IReadOnlyDictionary<OpaqueId128, (OpaqueId128 ParentNodeId, OpaqueId128 EdgeId)> previous)
    {
        var nodes = new List<OpaqueId128> { goalNodeId };
        var edges = new List<OpaqueId128>();
        var cursor = goalNodeId;
        while (cursor != startNodeId)
        {
            if (!previous.TryGetValue(cursor, out var step))
                throw new InvalidDataException("infrastructure.route-parent-missing");
            edges.Add(step.EdgeId);
            cursor = step.ParentNodeId;
            nodes.Add(cursor);
        }
        nodes.Reverse();
        edges.Reverse();
        return new InfrastructureRouteV1(true, totalCost, Array.AsReadOnly(nodes.ToArray()), Array.AsReadOnly(edges.ToArray()));
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
    int SemanticPriority,
    long RequestedAmount)
{
    public void Validate()
    {
        if (RequestId.IsZero) throw new InvalidDataException("infrastructure.queue-request-id-zero");
        if (RequestedAmount <= 0) throw new InvalidDataException("infrastructure.queue-request-amount-invalid");
    }
}

public static class InfrastructureQueueOrderV1
{
    public static IReadOnlyList<InfrastructureQueueRequestV1> Canonicalize(
        IEnumerable<InfrastructureQueueRequestV1> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var materialized = requests.Select(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Validate();
            return request;
        }).ToArray();
        if (materialized.Select(static request => request.RequestId).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("infrastructure.queue-request-id-duplicate");
        return Array.AsReadOnly(materialized
            .OrderBy(static request => request.EligibleStep)
            .ThenBy(static request => request.SemanticPriority)
            .ThenBy(static request => request.RequestId)
            .ToArray());
    }
}

public sealed record WeightedServiceDemandV1(
    OpaqueId128 ParticipantId,
    uint Weight,
    long Demand)
{
    public void Validate()
    {
        if (ParticipantId.IsZero) throw new InvalidDataException("infrastructure.wdrr-participant-id-zero");
        if (Weight == 0) throw new InvalidDataException("infrastructure.wdrr-weight-zero");
        if (Demand < 0) throw new InvalidDataException("infrastructure.wdrr-demand-negative");
    }
}

public sealed record WeightedServiceAllocationV1(OpaqueId128 ParticipantId, long Allocated);

public static class DeterministicWeightedDeficitRoundRobinV1
{
    public static IReadOnlyList<WeightedServiceAllocationV1> Allocate(
        IEnumerable<WeightedServiceDemandV1> demands,
        long totalCapacity,
        long baseQuantum = 1)
    {
        if (totalCapacity < 0 || baseQuantum <= 0)
            throw new InvalidDataException("infrastructure.wdrr-capacity-invalid");
        ArgumentNullException.ThrowIfNull(demands);
        var ordered = demands.Select(demand =>
        {
            ArgumentNullException.ThrowIfNull(demand);
            demand.Validate();
            return demand;
        }).OrderBy(static demand => demand.ParticipantId).ToArray();
        if (ordered.Select(static demand => demand.ParticipantId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("infrastructure.wdrr-participant-id-duplicate");

        var remainingDemand = ordered.ToDictionary(static demand => demand.ParticipantId, static demand => demand.Demand);
        var deficit = ordered.ToDictionary(static demand => demand.ParticipantId, static _ => (Int128)0);
        var allocated = ordered.ToDictionary(static demand => demand.ParticipantId, static _ => 0L);
        var remainingCapacity = totalCapacity;

        while (remainingCapacity > 0 && ordered.Any(demand => remainingDemand[demand.ParticipantId] > 0))
        {
            var progress = false;
            foreach (var demand in ordered)
            {
                if (remainingCapacity == 0) break;
                var id = demand.ParticipantId;
                if (remainingDemand[id] == 0) continue;
                deficit[id] += (Int128)baseQuantum * demand.Weight;
                var grant = Int128.Min(deficit[id], Int128.Min(remainingDemand[id], remainingCapacity));
                if (grant <= 0) continue;
                var grantLong = (long)grant;
                remainingDemand[id] -= grantLong;
                allocated[id] = checked(allocated[id] + grantLong);
                remainingCapacity -= grantLong;
                deficit[id] -= grant;
                progress = true;
            }
            if (!progress) throw new InvalidOperationException("infrastructure.wdrr-stalled");
        }

        return Array.AsReadOnly(ordered
            .Select(demand => new WeightedServiceAllocationV1(demand.ParticipantId, allocated[demand.ParticipantId]))
            .ToArray());
    }
}

public sealed record JacobiNetworkNodeV1(
    OpaqueId128 NodeId,
    FixedQ32_32 Diagonal,
    FixedQ32_32 RightHandSide)
{
    public void Validate()
    {
        if (NodeId.IsZero) throw new InvalidDataException("infrastructure.jacobi-node-id-zero");
        if (Diagonal.Raw == 0) throw new InvalidDataException("infrastructure.jacobi-diagonal-zero");
    }
}

public sealed record JacobiNetworkCoefficientV1(
    OpaqueId128 RowNodeId,
    OpaqueId128 ColumnNodeId,
    FixedQ32_32 Coefficient);

public sealed record JacobiNetworkResultV1(
    IReadOnlyDictionary<OpaqueId128, FixedQ32_32> Values,
    uint Iterations);

public static class DeterministicJacobiNetworkV1
{
    public const uint MaxIterations = 32;

    public static JacobiNetworkResultV1 Solve(
        IEnumerable<JacobiNetworkNodeV1> nodes,
        IEnumerable<JacobiNetworkCoefficientV1> coefficients,
        IReadOnlyDictionary<OpaqueId128, FixedQ32_32>? initialValues = null,
        uint iterations = MaxIterations)
    {
        if (iterations is 0 or > MaxIterations) throw new ArgumentOutOfRangeException(nameof(iterations));
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(coefficients);
        var orderedNodes = nodes.Select(node =>
        {
            ArgumentNullException.ThrowIfNull(node);
            node.Validate();
            return node;
        }).OrderBy(static node => node.NodeId).ToArray();
        if (orderedNodes.Length == 0) throw new InvalidDataException("infrastructure.jacobi-node-empty");
        if (orderedNodes.Select(static node => node.NodeId).Distinct().Count() != orderedNodes.Length)
            throw new InvalidDataException("infrastructure.jacobi-node-id-duplicate");
        var nodeIds = orderedNodes.Select(static node => node.NodeId).ToHashSet();
        var byRow = coefficients.Select(coefficient =>
        {
            ArgumentNullException.ThrowIfNull(coefficient);
            if (!nodeIds.Contains(coefficient.RowNodeId) || !nodeIds.Contains(coefficient.ColumnNodeId))
                throw new InvalidDataException("infrastructure.jacobi-coefficient-node-missing");
            if (coefficient.RowNodeId == coefficient.ColumnNodeId)
                throw new InvalidDataException("infrastructure.jacobi-diagonal-coefficient-forbidden");
            return coefficient;
        }).GroupBy(static coefficient => coefficient.RowNodeId)
          .ToDictionary(
              static group => group.Key,
              static group => group.OrderBy(static coefficient => coefficient.ColumnNodeId).ToArray());

        var previous = orderedNodes.ToDictionary(
            static node => node.NodeId,
            node => initialValues is not null && initialValues.TryGetValue(node.NodeId, out var initial)
                ? initial
                : FixedQ32_32.Zero);

        for (uint iteration = 0; iteration < iterations; iteration++)
        {
            var next = new Dictionary<OpaqueId128, FixedQ32_32>(orderedNodes.Length);
            foreach (var node in orderedNodes)
            {
                var sum = FixedQ32_32.Zero;
                if (byRow.TryGetValue(node.NodeId, out var row))
                {
                    foreach (var coefficient in row)
                        sum += coefficient.Coefficient * previous[coefficient.ColumnNodeId];
                }
                next[node.NodeId] = (node.RightHandSide - sum) / node.Diagonal;
            }
            previous = next;
        }

        return new JacobiNetworkResultV1(
            new System.Collections.ObjectModel.ReadOnlyDictionary<OpaqueId128, FixedQ32_32>(previous),
            iterations);
    }
}

public static class PowerNetworkJacobiV1
{
    public static JacobiNetworkResultV1 Solve(
        IEnumerable<JacobiNetworkNodeV1> nodes,
        IEnumerable<JacobiNetworkCoefficientV1> coefficients,
        IReadOnlyDictionary<OpaqueId128, FixedQ32_32>? initialValues = null)
        => DeterministicJacobiNetworkV1.Solve(nodes, coefficients, initialValues, DeterministicJacobiNetworkV1.MaxIterations);
}

public static class WaterNetworkJacobiV1
{
    public static JacobiNetworkResultV1 Solve(
        IEnumerable<JacobiNetworkNodeV1> nodes,
        IEnumerable<JacobiNetworkCoefficientV1> coefficients,
        IReadOnlyDictionary<OpaqueId128, FixedQ32_32>? initialValues = null)
        => DeterministicJacobiNetworkV1.Solve(nodes, coefficients, initialValues, DeterministicJacobiNetworkV1.MaxIterations);
}

public sealed record InfrastructureDependencyV1(OpaqueId128 UpstreamId, OpaqueId128 DownstreamId)
{
    public void Validate()
    {
        if (UpstreamId.IsZero || DownstreamId.IsZero)
            throw new InvalidDataException("infrastructure.dependency-id-zero");
        if (UpstreamId == DownstreamId)
            throw new InvalidDataException("infrastructure.dependency-self");
    }
}

public static class DeterministicOutageCascadeV1
{
    public static IReadOnlyList<OpaqueId128> Propagate(
        IEnumerable<OpaqueId128> initialFailures,
        IEnumerable<InfrastructureDependencyV1> dependencies)
    {
        ArgumentNullException.ThrowIfNull(initialFailures);
        ArgumentNullException.ThrowIfNull(dependencies);
        var failed = new SortedSet<OpaqueId128>(initialFailures);
        if (failed.Any(static id => id.IsZero)) throw new InvalidDataException("infrastructure.outage-id-zero");
        var orderedDependencies = dependencies.Select(dependency =>
        {
            ArgumentNullException.ThrowIfNull(dependency);
            dependency.Validate();
            return dependency;
        }).OrderBy(static dependency => dependency.UpstreamId)
          .ThenBy(static dependency => dependency.DownstreamId)
          .ToArray();

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var dependency in orderedDependencies)
            {
                if (failed.Contains(dependency.UpstreamId) && failed.Add(dependency.DownstreamId))
                    changed = true;
            }
        }
        return Array.AsReadOnly(failed.ToArray());
    }
}

public enum InformationDeliveryStatusV1 : byte
{
    Queued = 1,
    Delivered = 2,
    Failed = 3,
}

public sealed record InformationDeliveryV1(
    OpaqueId128 DeliveryId,
    OpaqueId128 SourceRef,
    OpaqueId128 DestinationRef,
    StableToken ClaimRef,
    ulong EligibleStep,
    int Priority,
    InformationDeliveryStatusV1 Status)
{
    public void Validate()
    {
        if (DeliveryId.IsZero || SourceRef.IsZero || DestinationRef.IsZero)
            throw new InvalidDataException("information.delivery-id-zero");
        if (!Enum.IsDefined(Status)) throw new InvalidDataException("information.delivery-status-invalid");
    }

    public InformationDeliveryV1 MarkDelivered()
    {
        Validate();
        if (Status != InformationDeliveryStatusV1.Queued)
            throw new InvalidDataException("information.delivery-transition-invalid");
        return this with { Status = InformationDeliveryStatusV1.Delivered };
    }
}

public static class InformationDeliveryOrderV1
{
    public static IReadOnlyList<InformationDeliveryV1> Canonicalize(IEnumerable<InformationDeliveryV1> deliveries)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        var materialized = deliveries.Select(delivery =>
        {
            ArgumentNullException.ThrowIfNull(delivery);
            delivery.Validate();
            return delivery;
        }).ToArray();
        if (materialized.Select(static delivery => delivery.DeliveryId).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("information.delivery-id-duplicate");
        return Array.AsReadOnly(materialized
            .OrderBy(static delivery => delivery.EligibleStep)
            .ThenBy(static delivery => delivery.Priority)
            .ThenBy(static delivery => delivery.DeliveryId)
            .ToArray());
    }
}
