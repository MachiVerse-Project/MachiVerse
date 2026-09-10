using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04BodyRegionStateSchemaDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04BodyRegionStateSchemaDependencyContractV1.ValidateCanonicalContract();

        Require(Qa04BodyRegionStateSchemaDependencyContractV1.Blockers.Count == 0,
            "Resolved BodyRegionState schema must have no remaining subdependency blockers.");
        Require(Qa04BodyRegionStateSchemaDependencyContractV1.FailureCodes.Count == 0,
            "Resolved BodyRegionState schema must expose no unresolved failure codes.");
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 7,
            "Resolved BodyRegionState schema must remain absent from the current reference-world blocker set.");
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.All(blocker =>
                blocker.DependencyId.Value != Qa04BodyRegionStateSchemaDependencyContractV1.ParentWorldDependencyId &&
                blocker.FailureCode.Value != Qa04BodyRegionStateSchemaDependencyContractV1.ParentWorldFailureCode),
            "Resolved BodyRegionState compatibility blocker must be absent from the world contract.");

        var genesis = Qa04BodyRegionStateSchemaDependencyContractV1.CreateCanonicalGenesis();
        Require(genesis.Count == 7 && genesis.All(static value => value is ResidentBodyRegionStateNestedValueV1),
            "QA-04 BodyRegionState genesis must contain all seven typed canonical region states.");
        var regions = genesis.Cast<ResidentBodyRegionStateNestedValueV1>().ToArray();
        Require(regions.Select(static region => region.RegionToken.Value)
                .SequenceEqual(ResidentBodyRegionStateNestedValueV1.CanonicalRegions.Select(static token => token.Value), StringComparer.Ordinal),
            "QA-04 BodyRegionState genesis must be in canonical region-token order.");
        Require(regions.All(static region =>
                region.IntegrityPpm == 1_000_000 &&
                region.FunctionCapacityPpm == 1_000_000 &&
                region.PainPpm == 0 &&
                region.InjuryLoadPpm == 0 &&
                region.DiseaseLoadPpm == 0 &&
                region.ImpairmentPpm == 0 &&
                region.RecoveryPpm == 1_000_000),
            "QA-04 BodyRegionState genesis values drifted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
