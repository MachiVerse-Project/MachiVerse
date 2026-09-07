using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.InfrastructureInformation;

public static class DeterministicBoundedOutageCascadeV1
{
    public static IReadOnlyList<OpaqueId128> Propagate(
        IEnumerable<OpaqueId128> initialFailures,
        IEnumerable<InfrastructureDependencyV1> dependencies,
        int maximumAffected)
    {
        ArgumentNullException.ThrowIfNull(initialFailures);
        ArgumentNullException.ThrowIfNull(dependencies);
        if (maximumAffected <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumAffected));

        var failed = new SortedSet<OpaqueId128>(initialFailures);
        if (failed.Any(static id => id.IsZero))
            throw new InvalidDataException("infrastructure.outage-id-zero");
        if (failed.Count > maximumAffected)
            throw new InvalidDataException("infrastructure.outage-cascade-budget-exceeded");

        var edges = dependencies
            .OrderBy(static edge => edge.UpstreamId)
            .ThenBy(static edge => edge.DownstreamId)
            .ToArray();
        foreach (var edge in edges) edge.Validate();
        EnsureDag(edges);

        var outgoing = edges
            .GroupBy(static edge => edge.UpstreamId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static edge => edge.DownstreamId)
                    .OrderBy(static id => id)
                    .ToArray());

        var queue = new Queue<OpaqueId128>(failed);
        while (queue.Count != 0)
        {
            var upstream = queue.Dequeue();
            if (!outgoing.TryGetValue(upstream, out var downstreamIds))
                continue;

            foreach (var downstream in downstreamIds)
            {
                if (!failed.Add(downstream))
                    continue;
                if (failed.Count > maximumAffected)
                    throw new InvalidDataException("infrastructure.outage-cascade-budget-exceeded");
                queue.Enqueue(downstream);
            }
        }

        return Array.AsReadOnly(failed.ToArray());
    }

    private static void EnsureDag(IReadOnlyList<InfrastructureDependencyV1> edges)
    {
        var nodes = edges
            .SelectMany(static edge => new[] { edge.UpstreamId, edge.DownstreamId })
            .Distinct()
            .ToArray();

        var indegree = nodes.ToDictionary(static id => id, static _ => 0);
        var outgoing = nodes.ToDictionary(static id => id, static _ => new List<OpaqueId128>());
        foreach (var edge in edges)
        {
            outgoing[edge.UpstreamId].Add(edge.DownstreamId);
            indegree[edge.DownstreamId]++;
        }

        var ready = new SortedSet<OpaqueId128>(
            indegree.Where(static pair => pair.Value == 0).Select(static pair => pair.Key));
        var visited = 0;
        while (ready.Count != 0)
        {
            var node = ready.Min;
            ready.Remove(node);
            visited++;
            foreach (var downstream in outgoing[node].OrderBy(static id => id))
            {
                indegree[downstream]--;
                if (indegree[downstream] == 0)
                    ready.Add(downstream);
            }
        }

        if (visited != nodes.Length)
            throw new InvalidDataException("infrastructure.dependency-cycle-requires-coupled-policy");
    }
}
