using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;

internal static class Sim12InfrastructureInformationSmoke
{
    internal static void Run()
    {
        VerifyDijkstraTie();
        VerifyQueueOrder();
        VerifyWdrr();
        VerifyJacobi();
        VerifyOutageCascade();
        VerifyDeliveryBeliefSeparation();
        Sim12RuntimeGateSmoke.RunAsync().GetAwaiter().GetResult();
    }

    private static void VerifyDijkstraTie()
    {
        var a = Id("00000000000000000000000000012601");
        var b = Id("00000000000000000000000000012602");
        var c = Id("00000000000000000000000000012603");
        var d = Id("00000000000000000000000000012604");
        var edges = new[]
        {
            new InfrastructureEdgeV1(Id("00000000000000000000000000012611"), a, c, 1),
            new InfrastructureEdgeV1(Id("00000000000000000000000000012612"), a, b, 1),
            new InfrastructureEdgeV1(Id("00000000000000000000000000012613"), b, d, 1),
            new InfrastructureEdgeV1(Id("00000000000000000000000000012614"), c, d, 1),
        };
        var forward = DeterministicDijkstraV1.FindRoute(a, d, edges);
        var reverse = DeterministicDijkstraV1.FindRoute(a, d, edges.Reverse());
        Require(forward.Found && forward.TotalCost == 2 && forward.NodeIds.SequenceEqual(new[] { a, b, d }) &&
                forward.NodeIds.SequenceEqual(reverse.NodeIds) && forward.EdgeIds.SequenceEqual(reverse.EdgeIds),
            "domain.infrastructure.dijkstra.tie: canonical (distance,node_id) tie failed.");
    }

    private static void VerifyQueueOrder()
    {
        var first = new InfrastructureQueueRequestV1(Id("00000000000000000000000000012701"), 10, -1, 1);
        var second = new InfrastructureQueueRequestV1(Id("00000000000000000000000000012702"), 10, 0, 1);
        var later = new InfrastructureQueueRequestV1(Id("00000000000000000000000000012703"), 11, -99, 1);
        var ordered = InfrastructureQueueOrderV1.Canonicalize(new[] { later, second, first });
        Require(ordered.Select(static x => x.RequestId).SequenceEqual(new[] { first.RequestId, second.RequestId, later.RequestId }),
            "domain.infrastructure.queue.order: eligible_step/semantic_priority/request_id order failed.");
    }

    private static void VerifyWdrr()
    {
        var a = Id("00000000000000000000000000012801");
        var b = Id("00000000000000000000000000012802");
        var demands = new[] { new WeightedServiceDemandV1(b, 1, 8), new WeightedServiceDemandV1(a, 2, 8) };
        var forward = DeterministicWeightedDeficitRoundRobinV1.Allocate(demands, 9, 1);
        var reverse = DeterministicWeightedDeficitRoundRobinV1.Allocate(demands.Reverse(), 9, 1);
        Require(forward.SequenceEqual(reverse) && forward.Sum(static x => x.Allocated) == 9 &&
                forward.Single(x => x.ParticipantId == a).Allocated > forward.Single(x => x.ParticipantId == b).Allocated,
            "domain.infrastructure.wdrr: stable weighted allocation failed.");
    }

    private static void VerifyJacobi()
    {
        var a = Id("00000000000000000000000000012901");
        var b = Id("00000000000000000000000000012902");
        var nodes = new[]
        {
            new JacobiNetworkNodeV1(a, FixedQ32_32.FromInteger(2), FixedQ32_32.FromInteger(2)),
            new JacobiNetworkNodeV1(b, FixedQ32_32.FromInteger(2), FixedQ32_32.FromInteger(2)),
        };
        var coefficients = new[]
        {
            new JacobiNetworkCoefficientV1(a, b, FixedQ32_32.One),
            new JacobiNetworkCoefficientV1(b, a, FixedQ32_32.One),
        };
        var power = PowerNetworkJacobiV1.Solve(nodes, coefficients);
        var water = WaterNetworkJacobiV1.Solve(nodes.Reverse(), coefficients.Reverse());
        Require(power.Iterations == 32 && water.Iterations == 32 &&
                power.Values.OrderBy(static x => x.Key).SequenceEqual(water.Values.OrderBy(static x => x.Key)),
            "domain.infrastructure.power.jacobi/domain.infrastructure.water.jacobi: fixed full-vector iteration failed.");
    }

    private static void VerifyOutageCascade()
    {
        var a = Id("00000000000000000000000000012a01");
        var b = Id("00000000000000000000000000012a02");
        var c = Id("00000000000000000000000000012a03");
        var deps = new[] { new InfrastructureDependencyV1(b, c), new InfrastructureDependencyV1(a, b) };
        var result = DeterministicOutageCascadeV1.Propagate(new[] { a }, deps);
        Require(result.SequenceEqual(new[] { a, b, c }),
            "domain.infrastructure.outage-cascade: deterministic dependency propagation failed.");

        RequireReject(
            () => DeterministicOutageCascadeV1.Propagate(
                new[] { a },
                new[]
                {
                    new InfrastructureDependencyV1(a, b),
                    new InfrastructureDependencyV1(b, a),
                }),
            "infrastructure.dependency-cycle-requires-coupled-policy",
            "domain.infrastructure.outage-cascade");
    }

    private static void VerifyDeliveryBeliefSeparation()
    {
        var delivery = new InformationDeliveryV1(
            Id("00000000000000000000000000012b01"),
            Id("00000000000000000000000000012b02"),
            Id("00000000000000000000000000012b03"),
            new StableToken("claim.weather.report"), 20, 0, InformationDeliveryStatusV1.Queued);
        var delivered = delivery.MarkDelivered();
        Require(delivered.Status == InformationDeliveryStatusV1.Delivered &&
                delivered.ClaimRef == delivery.ClaimRef &&
                delivered.SourceRef == delivery.SourceRef &&
                delivered.DestinationRef == delivery.DestinationRef,
            "domain.information.delivery-belief-separation: delivery must preserve claim/participant identity and only change delivery lifecycle.");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void RequireReject(Action action, string expected, string acceptance)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expected)
        {
            return;
        }

        throw new InvalidOperationException($"{acceptance}: expected SIM-12 rejection {expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
