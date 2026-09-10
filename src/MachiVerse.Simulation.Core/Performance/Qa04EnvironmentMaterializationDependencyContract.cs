using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04EnvironmentMaterializationDependencyKindV1 : byte
{
    SpatialScopeAuthority = 1,
    LineageSubjectBinding = 2,
    AggregateScopeBinding = 3,
}

public sealed record Qa04EnvironmentMaterializationDependencyV1(
    StableToken DependencyId,
    Qa04EnvironmentMaterializationDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Fail-closed implementation audit beneath the two Environment world blockers. Exact D0/D1
/// decomposition, four-to-one source coverage, genesis vocabulary/topology, canonical TileScope,
/// and D0/D1 Environment lineage subject/parent authority are implemented. No internal authority
/// subdependency remains; the parent world blockers stay until full canonical materialization,
/// Ref closure and Snapshot/recovery proof are complete.
/// </summary>
public static class Qa04EnvironmentMaterializationDependencyContractV1
{
    public const string D0ParentWorldDependencyId = "environment.d0-cell-cohort.partition-mapping";
    public const string D0ParentWorldFailureCode = "qa04.material.environment-d0-partition-mapping-undefined";
    public const string D1ParentWorldDependencyId = "environment.d1-aggregate.partition-mapping";
    public const string D1ParentWorldFailureCode = "qa04.material.environment-d1-partition-mapping-undefined";

    private static readonly IReadOnlyList<Qa04EnvironmentMaterializationDependencyV1> BlockersValue =
        Array.AsReadOnly(Array.Empty<Qa04EnvironmentMaterializationDependencyV1>());

    public static IReadOnlyList<Qa04EnvironmentMaterializationDependencyV1> Blockers => BlockersValue;
    public static IReadOnlyList<StableToken> FailureCodes
        => BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04EnvironmentD0PartitionMaterializerV1.ValidateCanonicalContract();
        Qa04EnvironmentD1PartitionMaterializerV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();
        Qa04EnvironmentLineageAuthorityV1.ValidateCanonicalContract();

        if (BlockersValue.Count != 0 || FailureCodes.Count != 0)
            throw new InvalidDataException("qa04.environment.materialization-dependency-retained");

        RequireParent(D0ParentWorldDependencyId, D0ParentWorldFailureCode);
        RequireParent(D1ParentWorldDependencyId, D1ParentWorldFailureCode);

        if (Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count != 1_000_000 ||
            Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count != 250_000 ||
            Qa04EnvironmentReferenceDecompositionV1.Partitions.Count != 13)
            throw new InvalidDataException("qa04.environment.materialization-fixed-decomposition-drift");

        var sample = Qa04EnvironmentReferenceDecompositionV1.BindD0(0).Descriptor;
        var hash = Qa04ReferenceGenesisValueSourceV1.Hash(sample.RecordId, "environment.contract-probe");
        if (hash.Length != 32)
            throw new InvalidDataException("qa04.environment.materialization-genesis-source-drift");

        var d0 = Qa04EnvironmentReferenceDecompositionV1.BindD0(0);
        var d1 = Qa04EnvironmentReferenceDecompositionV1.BindD1(0);
        if (Qa04EnvironmentD0PartitionMaterializerV1.ResolveSpatialScope(d0) !=
                Qa04SpatialTileScopeAuthorityV1.ScopeRef(d0.Descriptor.RegionalTileIndex) ||
            Qa04EnvironmentD1PartitionMaterializerV1.ResolveSpatialScope(d1) !=
                Qa04SpatialTileScopeAuthorityV1.ScopeRef(d1.Descriptor.RegionalTileIndex))
            throw new InvalidDataException("qa04.environment.canonical-tile-scope-binding-drift");

        var lineageSlice = Qa04EnvironmentReferenceDecompositionV1.Get(Qa04EnvironmentLineageAuthorityV1.LineagePartitionId);
        var lineageD0 = Qa04EnvironmentReferenceDecompositionV1.BindD0(lineageSlice.D0StartOrdinal);
        var lineageD1 = Qa04EnvironmentReferenceDecompositionV1.BindD1(lineageSlice.D1StartOrdinal);
        var d0Subject = Qa04EnvironmentLineageAuthorityV1.ResolveD0Subject(lineageD0);
        var d1Subject = Qa04EnvironmentLineageAuthorityV1.ResolveD1Subject(lineageD1);
        if (d0Subject.PartitionId.Value == Qa04EnvironmentLineageAuthorityV1.LineagePartitionId ||
            d1Subject.PartitionId.Value == Qa04EnvironmentLineageAuthorityV1.LineagePartitionId ||
            Qa04EnvironmentLineageAuthorityV1.ResolveD1Parents(lineageD1).Count !=
                Qa04EnvironmentLineageAuthorityV1.D1ParentCount)
            throw new InvalidDataException("qa04.environment.lineage-binding-drift");
    }

    private static void RequireParent(string dependencyId, string failureCode)
    {
        var parent = Qa04ReferenceWorldDependencyContractV1.Blockers.SingleOrDefault(
            blocker => blocker.DependencyId.Value == dependencyId)
            ?? throw new InvalidDataException($"qa04.environment.parent-world-blocker-missing:{dependencyId}");
        if (parent.Kind != Qa04ReferenceDependencyBlockerKindV1.PartitionMapping ||
            parent.PartitionId is not null ||
            parent.FieldName is not null ||
            parent.FailureCode.Value != failureCode)
            throw new InvalidDataException($"qa04.environment.parent-world-blocker-drift:{dependencyId}");
    }
}
