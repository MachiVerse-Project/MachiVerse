using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04EnvironmentMaterializationDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04EnvironmentMaterializationDependencyContractV1.ValidateCanonicalContract();

        Require(Qa04EnvironmentMaterializationDependencyContractV1.Blockers.Count == 0,
            "Implemented QA-04 Environment authority bindings must have no remaining subdependency blockers.");
        Require(Qa04EnvironmentMaterializationDependencyContractV1.FailureCodes.Count == 0,
            "Implemented QA-04 Environment authority bindings must expose no pending failure codes.");
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 5,
            "Environment subdependencies must not change the reference-world blocker count.");

        var binding = Qa04EnvironmentReferenceDecompositionV1.BindD1(0);
        var expectedTile = binding.Descriptor.RegionalTileIndex;
        var canonicalScope = Qa04EnvironmentD1PartitionMaterializerV1.ResolveSpatialScope(binding);
        Require(canonicalScope == Qa04SpatialTileScopeAuthorityV1.ScopeRef(expectedTile),
            "Environment D1 must use the canonical TileScope authority in production.");

        ushort? observedTile = null;
        var fixtureScope = Qa04EnvironmentD1PartitionMaterializerV1.ResolveSpatialScope(
            binding,
            tile =>
            {
                observedTile = tile;
                return new PartitionRecordRefV1(
                    new StableToken(Qa04EnvironmentD1PartitionMaterializerV1.SpatialScopePartitionId),
                    Qa04ReferenceLoadV1.Record(new StableToken("resident.persistent-identity"), tile).RecordId);
            });
        Require(observedTile == expectedTile &&
                fixtureScope.PartitionId.Value == Qa04EnvironmentD1PartitionMaterializerV1.SpatialScopePartitionId &&
                !fixtureScope.RecordId.IsZero,
            "Environment D1 fixture seam must preserve descriptor RegionalTileIndex for negative testing.");

        var rejected = false;
        try
        {
            _ = Qa04EnvironmentD1PartitionMaterializerV1.ResolveSpatialScope(
                binding,
                _ => new PartitionRecordRefV1(new StableToken("environment.geology"), fixtureScope.RecordId));
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.environment.d1-spatial-scope-ref-invalid")
        {
            rejected = true;
        }
        Require(rejected, "Environment D1 spatial scope binding must fail closed on a foreign target partition.");

        var lineage = Qa04EnvironmentReferenceDecompositionV1.Get(Qa04EnvironmentLineageAuthorityV1.LineagePartitionId);
        var lineageD0 = Qa04EnvironmentReferenceDecompositionV1.BindD0(lineage.D0StartOrdinal);
        var lineageD1 = Qa04EnvironmentReferenceDecompositionV1.BindD1(lineage.D1StartOrdinal);
        Require(Qa04EnvironmentLineageAuthorityV1.ResolveD0Subject(lineageD0).PartitionId.Value !=
                Qa04EnvironmentLineageAuthorityV1.LineagePartitionId,
            "Environment D0 lineage subject authority must target non-lineage material.");
        Require(Qa04EnvironmentLineageAuthorityV1.ResolveD1Subject(lineageD1).PartitionId.Value !=
                Qa04EnvironmentLineageAuthorityV1.LineagePartitionId,
            "Environment D1 lineage subject authority must target non-lineage material.");
        Require(Qa04EnvironmentLineageAuthorityV1.ResolveD1Parents(lineageD1).Count ==
                Qa04EnvironmentLineageAuthorityV1.D1ParentCount,
            "Environment D1 lineage authority must retain exact four-source provenance.");

        var d0 = Qa04ReferenceWorldDependencyContractV1.Blockers.Single(
            static blocker => blocker.DependencyId.Value == Qa04EnvironmentMaterializationDependencyContractV1.D0ParentWorldDependencyId);
        var d1 = Qa04ReferenceWorldDependencyContractV1.Blockers.Single(
            static blocker => blocker.DependencyId.Value == Qa04EnvironmentMaterializationDependencyContractV1.D1ParentWorldDependencyId);
        Require(d0.FailureCode.Value == Qa04EnvironmentMaterializationDependencyContractV1.D0ParentWorldFailureCode,
            "Environment D0 parent blocker drifted.");
        Require(d1.FailureCode.Value == Qa04EnvironmentMaterializationDependencyContractV1.D1ParentWorldFailureCode,
            "Environment D1 parent blocker drifted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
