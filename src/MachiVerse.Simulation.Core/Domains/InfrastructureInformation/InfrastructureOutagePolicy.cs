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

        var initial = initialFailures.ToArray();
        if (initial.Distinct().Count() > maximumAffected)
            throw new InvalidDataException("infrastructure.outage-cascade-budget-exceeded");

        var result = DeterministicOutageCascadeV1.Propagate(initial, dependencies);
        if (result.Count > maximumAffected)
            throw new InvalidDataException("infrastructure.outage-cascade-budget-exceeded");
        return result;
    }
}
