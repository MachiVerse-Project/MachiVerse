using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.InfrastructureInformation;

public sealed record InfrastructureEdgeV1(OpaqueId128 EdgeId, OpaqueId128 FromNodeId, OpaqueId128 ToNodeId, ulong Cost)
{
    public void Validate()
    {
        if (EdgeId.IsZero || FromNodeId.IsZero || ToNodeId.IsZero) throw new InvalidDataException("infrastructure.edge-id-zero");
        if (FromNodeId == ToNodeId) throw new InvalidDataException("infrastructure.edge-self");
    }
}
public sealed record InfrastructureRouteV1(bool Found, ulong TotalCost, IReadOnlyList<OpaqueId128> NodeIds, IReadOnlyList<OpaqueId128> EdgeIds);
public static class DeterministicDijkstraV1
{
    public static InfrastructureRouteV1 FindRoute(OpaqueId128 startNodeId, OpaqueId128 goalNodeId, IEnumerable<InfrastructureEdgeV1> edges)
    {
        if (startNodeId.IsZero || goalNodeId.IsZero) throw new InvalidDataException("infrastructure.route-node-id-zero");
        ArgumentNullException.ThrowIfNull(edges);
        if (startNodeId == goalNodeId) return new(true, 0, [startNodeId], []);
        var materialized = edges.ToArray(); foreach (var edge in materialized) edge.Validate();
        if (materialized.Select(x => x.EdgeId).Distinct().Count() != materialized.Length) throw new InvalidDataException("infrastructure.edge-id-duplicate");
        var outgoing = materialized.GroupBy(x => x.FromNodeId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.ToNodeId).ThenBy(x => x.EdgeId).ToArray());
        var queue = new SortedSet<RouteQueueEntryV1>(RouteQueueComparerV1.Instance) { new(0, startNodeId) };
        var distances = new Dictionary<OpaqueId128, ulong> { [startNodeId] = 0 };
        var previous = new Dictionary<OpaqueId128, (OpaqueId128 ParentNodeId, OpaqueId128 EdgeId)>();
        while (queue.Count != 0)
        {
            var current = queue.Min!; queue.Remove(current);
            if (!distances.TryGetValue(current.NodeId, out var known) || known != current.Distance) continue;
            if (current.NodeId == goalNodeId) return BuildRoute(startNodeId, goalNodeId, current.Distance, previous);
            if (!outgoing.TryGetValue(current.NodeId, out var nextEdges)) continue;
            foreach (var edge in nextEdges)
            {
                ulong next; try { next = checked(current.Distance + edge.Cost); } catch (OverflowException ex) { throw new OverflowException("simulation.numeric-overflow", ex); }
                if (distances.TryGetValue(edge.ToNodeId, out var existing) && next >= existing) continue;
                distances[edge.ToNodeId] = next; previous[edge.ToNodeId] = (current.NodeId, edge.EdgeId); queue.Add(new(next, edge.ToNodeId));
            }
        }
        return new(false, 0, [], []);
    }
    private static InfrastructureRouteV1 BuildRoute(OpaqueId128 start, OpaqueId128 goal, ulong cost, IReadOnlyDictionary<OpaqueId128,(OpaqueId128 ParentNodeId,OpaqueId128 EdgeId)> previous)
    {
        var nodes = new List<OpaqueId128>{goal}; var edges = new List<OpaqueId128>(); var cursor=goal;
        while(cursor!=start){ if(!previous.TryGetValue(cursor,out var step)) throw new InvalidDataException("infrastructure.route-parent-missing"); edges.Add(step.EdgeId); cursor=step.ParentNodeId; nodes.Add(cursor); }
        nodes.Reverse(); edges.Reverse(); return new(true,cost,nodes.AsReadOnly(),edges.AsReadOnly());
    }
    private sealed record RouteQueueEntryV1(ulong Distance, OpaqueId128 NodeId);
    private sealed class RouteQueueComparerV1:IComparer<RouteQueueEntryV1>{public static readonly RouteQueueComparerV1 Instance=new(); public int Compare(RouteQueueEntryV1? a,RouteQueueEntryV1? b){if(ReferenceEquals(a,b))return 0;if(a is null)return -1;if(b is null)return 1;var c=a.Distance.CompareTo(b.Distance);return c!=0?c:a.NodeId.CompareTo(b.NodeId);}}
}

public sealed record InfrastructureQueueRequestV1(OpaqueId128 RequestId, ulong EligibleStep, int SemanticPriority, long RequestedAmount){public void Validate(){if(RequestId.IsZero)throw new InvalidDataException("infrastructure.queue-request-id-zero");if(RequestedAmount<=0)throw new InvalidDataException("infrastructure.queue-request-amount-invalid");}}
public static class InfrastructureQueueOrderV1{public static IReadOnlyList<InfrastructureQueueRequestV1> Canonicalize(IEnumerable<InfrastructureQueueRequestV1> requests){ArgumentNullException.ThrowIfNull(requests);var a=requests.ToArray();foreach(var x in a)x.Validate();if(a.Select(x=>x.RequestId).Distinct().Count()!=a.Length)throw new InvalidDataException("infrastructure.queue-request-id-duplicate");return Array.AsReadOnly(a.OrderBy(x=>x.EligibleStep).ThenBy(x=>x.SemanticPriority).ThenBy(x=>x.RequestId).ToArray());}}

public sealed record WeightedServiceDemandV1(OpaqueId128 ParticipantId,uint Weight,long Demand){public void Validate(){if(ParticipantId.IsZero)throw new InvalidDataException("infrastructure.wdrr-participant-id-zero");if(Weight==0)throw new InvalidDataException("infrastructure.wdrr-weight-zero");if(Demand<0)throw new InvalidDataException("infrastructure.wdrr-demand-negative");}}
public sealed record WeightedServiceAllocationV1(OpaqueId128 ParticipantId,long Allocated);
public static class DeterministicWeightedDeficitRoundRobinV1
{
    public static IReadOnlyList<WeightedServiceAllocationV1> Allocate(IEnumerable<WeightedServiceDemandV1> demands,long totalCapacity,long baseQuantum=1)
    {
        if(totalCapacity<0||baseQuantum<=0)throw new InvalidDataException("infrastructure.wdrr-capacity-invalid");var ordered=demands.OrderBy(x=>x.ParticipantId).ToArray();foreach(var x in ordered)x.Validate();if(ordered.Select(x=>x.ParticipantId).Distinct().Count()!=ordered.Length)throw new InvalidDataException("infrastructure.wdrr-participant-id-duplicate");
        var remaining=ordered.ToDictionary(x=>x.ParticipantId,x=>x.Demand);var deficit=ordered.ToDictionary(x=>x.ParticipantId,_=>(Int128)0);var allocated=ordered.ToDictionary(x=>x.ParticipantId,_=>0L);var capacity=totalCapacity;
        while(capacity>0&&ordered.Any(x=>remaining[x.ParticipantId]>0)){var progress=false;foreach(var demand in ordered){if(capacity==0)break;var id=demand.ParticipantId;if(remaining[id]==0)continue;deficit[id]+=(Int128)baseQuantum*demand.Weight;var grant=Int128.Min(deficit[id],Int128.Min(remaining[id],capacity));if(grant<=0)continue;var g=(long)grant;remaining[id]-=g;allocated[id]=checked(allocated[id]+g);capacity-=g;deficit[id]-=grant;progress=true;}if(!progress)throw new InvalidOperationException("infrastructure.wdrr-stalled");}
        return Array.AsReadOnly(ordered.Select(x=>new WeightedServiceAllocationV1(x.ParticipantId,allocated[x.ParticipantId])).ToArray());
    }
}

public sealed record JacobiNetworkNodeV1(OpaqueId128 NodeId,FixedQ32_32 Diagonal,FixedQ32_32 RightHandSide){public void Validate(){if(NodeId.IsZero)throw new InvalidDataException("infrastructure.jacobi-node-id-zero");if(Diagonal.Raw==0)throw new InvalidDataException("infrastructure.jacobi-diagonal-zero");}}
public sealed record JacobiNetworkCoefficientV1(OpaqueId128 RowNodeId,OpaqueId128 ColumnNodeId,FixedQ32_32 Coefficient);
public sealed record JacobiNetworkResultV1(IReadOnlyDictionary<OpaqueId128,FixedQ32_32> Values,uint Iterations);
public static class DeterministicJacobiNetworkV1
{
    public const uint MaxIterations=32;
    public static JacobiNetworkResultV1 Solve(IEnumerable<JacobiNetworkNodeV1> nodes,IEnumerable<JacobiNetworkCoefficientV1> coefficients,IReadOnlyDictionary<OpaqueId128,FixedQ32_32>? initialValues=null,uint iterations=MaxIterations)
    {
        if(iterations is 0 or >MaxIterations)throw new ArgumentOutOfRangeException(nameof(iterations));var ordered=nodes.OrderBy(x=>x.NodeId).ToArray();foreach(var x in ordered)x.Validate();if(ordered.Length==0)throw new InvalidDataException("infrastructure.jacobi-node-empty");if(ordered.Select(x=>x.NodeId).Distinct().Count()!=ordered.Length)throw new InvalidDataException("infrastructure.jacobi-node-id-duplicate");var ids=ordered.Select(x=>x.NodeId).ToHashSet();
        var byRow=coefficients.ToArray();foreach(var c in byRow){if(!ids.Contains(c.RowNodeId)||!ids.Contains(c.ColumnNodeId))throw new InvalidDataException("infrastructure.jacobi-coefficient-node-missing");if(c.RowNodeId==c.ColumnNodeId)throw new InvalidDataException("infrastructure.jacobi-diagonal-coefficient-forbidden");}var rows=byRow.GroupBy(x=>x.RowNodeId).ToDictionary(g=>g.Key,g=>g.OrderBy(x=>x.ColumnNodeId).ToArray());
        var previous=ordered.ToDictionary(x=>x.NodeId,x=>initialValues is not null&&initialValues.TryGetValue(x.NodeId,out var v)?v:FixedQ32_32.Zero);
        for(uint iteration=0;iteration<iterations;iteration++){var next=new Dictionary<OpaqueId128,FixedQ32_32>(ordered.Length);foreach(var node in ordered){var sum=FixedQ32_32.Zero;if(rows.TryGetValue(node.NodeId,out var row))foreach(var c in row)sum+=c.Coefficient*previous[c.ColumnNodeId];next[node.NodeId]=Divide(node.RightHandSide-sum,node.Diagonal);}previous=next;}
        return new(new System.Collections.ObjectModel.ReadOnlyDictionary<OpaqueId128,FixedQ32_32>(previous),iterations);
    }
    private static FixedQ32_32 Divide(FixedQ32_32 numerator,FixedQ32_32 denominator){if(denominator.Raw==0)throw new DivideByZeroException();var scaled=(Int128)numerator.Raw<<FixedQ32_32.FractionalBits;var q=scaled/denominator.Raw;var r=scaled%denominator.Raw;var d=denominator.Raw<0?-(Int128)denominator.Raw:denominator.Raw;var ar=r<0?-r:r;var twice=ar*2;if(twice>d||(twice==d&&(q&1)!=0))q+=((scaled<0)^(denominator.Raw<0))?-1:1;if(q<long.MinValue||q>long.MaxValue)throw new OverflowException("simulation.numeric-overflow");return new((long)q);}
}
public static class PowerNetworkJacobiV1{public static JacobiNetworkResultV1 Solve(IEnumerable<JacobiNetworkNodeV1> n,IEnumerable<JacobiNetworkCoefficientV1> c,IReadOnlyDictionary<OpaqueId128,FixedQ32_32>? i=null)=>DeterministicJacobiNetworkV1.Solve(n,c,i,32);}
public static class WaterNetworkJacobiV1{public static JacobiNetworkResultV1 Solve(IEnumerable<JacobiNetworkNodeV1> n,IEnumerable<JacobiNetworkCoefficientV1> c,IReadOnlyDictionary<OpaqueId128,FixedQ32_32>? i=null)=>DeterministicJacobiNetworkV1.Solve(n,c,i,32);}

public sealed record InfrastructureDependencyV1(OpaqueId128 UpstreamId,OpaqueId128 DownstreamId){public void Validate(){if(UpstreamId.IsZero||DownstreamId.IsZero)throw new InvalidDataException("infrastructure.dependency-id-zero");if(UpstreamId==DownstreamId)throw new InvalidDataException("infrastructure.dependency-self");}}
public static class DeterministicOutageCascadeV1{public static IReadOnlyList<OpaqueId128> Propagate(IEnumerable<OpaqueId128> initialFailures,IEnumerable<InfrastructureDependencyV1> dependencies){var failed=new SortedSet<OpaqueId128>(initialFailures);if(failed.Any(x=>x.IsZero))throw new InvalidDataException("infrastructure.outage-id-zero");var deps=dependencies.OrderBy(x=>x.UpstreamId).ThenBy(x=>x.DownstreamId).ToArray();foreach(var x in deps)x.Validate();var changed=true;while(changed){changed=false;foreach(var d in deps)if(failed.Contains(d.UpstreamId)&&failed.Add(d.DownstreamId))changed=true;}return Array.AsReadOnly(failed.ToArray());}}

public enum InformationDeliveryStatusV1:byte{Queued=1,Delivered=2,Failed=3}
public sealed record InformationDeliveryV1(OpaqueId128 DeliveryId,OpaqueId128 SourceRef,OpaqueId128 DestinationRef,StableToken ClaimRef,ulong EligibleStep,int Priority,InformationDeliveryStatusV1 Status){public void Validate(){if(DeliveryId.IsZero||SourceRef.IsZero||DestinationRef.IsZero)throw new InvalidDataException("information.delivery-id-zero");if(!Enum.IsDefined(Status))throw new InvalidDataException("information.delivery-status-invalid");}public InformationDeliveryV1 MarkDelivered(){Validate();if(Status!=InformationDeliveryStatusV1.Queued)throw new InvalidDataException("information.delivery-transition-invalid");return this with{Status=InformationDeliveryStatusV1.Delivered};}}
public static class InformationDeliveryOrderV1{public static IReadOnlyList<InformationDeliveryV1> Canonicalize(IEnumerable<InformationDeliveryV1> deliveries){var a=deliveries.ToArray();foreach(var x in a)x.Validate();if(a.Select(x=>x.DeliveryId).Distinct().Count()!=a.Length)throw new InvalidDataException("information.delivery-id-duplicate");return Array.AsReadOnly(a.OrderBy(x=>x.EligibleStep).ThenBy(x=>x.Priority).ThenBy(x=>x.DeliveryId).ToArray());}}
