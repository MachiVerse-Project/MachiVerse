using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04InfrastructureReferenceDecompositionSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();
        Require(Qa04InfrastructureReferenceDecompositionV1.Slices.Count == 16,
            "Infrastructure decomposition must contain exactly 16 material classes.");
        Require(Qa04InfrastructureReferenceDecompositionV1.Slices.Sum(static slice => checked((long)slice.Count)) == 500_000,
            "Infrastructure decomposition must total exactly 500,000 records.");

        var first = Qa04InfrastructureReferenceDecompositionV1.Bind(0);
        var lastNetwork = Qa04InfrastructureReferenceDecompositionV1.Bind(99);
        var firstNode = Qa04InfrastructureReferenceDecompositionV1.Bind(100);
        var lastNode = Qa04InfrastructureReferenceDecompositionV1.Bind(20_099);
        var firstEdge = Qa04InfrastructureReferenceDecompositionV1.Bind(20_100);
        var lastEdge = Qa04InfrastructureReferenceDecompositionV1.Bind(120_099);
        var firstQueue = Qa04InfrastructureReferenceDecompositionV1.Bind(195_100);
        var last = Qa04InfrastructureReferenceDecompositionV1.Bind(499_999);

        Require(first.MaterialClass.Value == "network" && first.LocalOrdinal == 0 &&
                lastNetwork.MaterialClass.Value == "network" && lastNetwork.LocalOrdinal == 99,
            "Infrastructure network range drifted.");
        Require(firstNode.MaterialClass.Value == "node" && firstNode.LocalOrdinal == 0 &&
                lastNode.MaterialClass.Value == "node" && lastNode.LocalOrdinal == 19_999,
            "Infrastructure node range drifted.");
        Require(firstEdge.MaterialClass.Value == "edge" && firstEdge.LocalOrdinal == 0 &&
                lastEdge.MaterialClass.Value == "edge" && lastEdge.LocalOrdinal == 99_999,
            "Infrastructure edge range drifted.");
        Require(firstQueue.MaterialClass.Value == "service_queue" && firstQueue.LocalOrdinal == 0,
            "Infrastructure service queue range drifted.");
        Require(last.MaterialClass.Value == "lineage" && last.LocalOrdinal == 4_899,
            "Infrastructure final lineage range drifted.");
        Require(new[] { first, firstNode, firstEdge, firstQueue }.All(static binding => binding.UsesSpecializedIdentity),
            "Infrastructure specialized identity markers drifted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
