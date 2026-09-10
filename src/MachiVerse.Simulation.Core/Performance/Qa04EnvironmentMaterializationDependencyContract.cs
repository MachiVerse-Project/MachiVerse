using MachiVerse.Simulation.Core.Determinism;

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
/// partition decomposition, four-to-one source coverage, fixed vocabulary, topology, and the common
/// genesis hash source are already implemented. These entries are only the remaining authority
/// bindings that cannot be inferred from those contracts without inventing canonical Ref targets.
/// </summary>
public static class Qa04EnvironmentMaterializationDependencyContractV1
{
    public const string D0ParentWorldDependencyId = "environment.d0-cell-cohort.partition-mapping";
    public const string D0ParentWorldFailureCode = "qa04.material.environment-d0-partition-mapping-undefined";
    public const string D1ParentWorldDependencyId = "environment.d1-aggregate.partition-mapping";
    public const string D1ParentWorldFailureCode = "qa04.material.environment-d1-partition-mapping-undefined";

    private static readonly IReadOnlyList<Qa04EnvironmentMaterializationDependencyV1> BlockersValue = Array.AsReadOnly(new[]
    {
        Blocker(
            "environment.materialization.d0-lineage-subject-binding",
            Qa04EnvironmentMaterializationDependencyKindV1.LineageSubjectBinding,
            "qa04.environment.d0-lineage-subject-binding-pending"),
        Blocker(
            "environment.materialization.d1-lineage-subject-binding",
            Qa04EnvironmentMaterializationDependencyKindV1.LineageSubjectBinding,
            "qa04.environment.d1-lineage-subject-binding-pending"),
        Blocker(
            "environment.materialization.d1-spatial-scope-binding",
            Qa04EnvironmentMaterializationDependencyKindV1.AggregateScopeBinding,
            "qa04.environment.d1-spatial-scope-binding-pending"),
        Blocker(
            "environment.materialization.tile-scope-authority",
            Qa04EnvironmentMaterializationDependencyKindV1.SpatialScopeAuthority,
            "qa04.environment.tile-scope-authority-pending"),
    }
    .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
    .ToArray());

    public static IReadOnlyList<Qa04EnvironmentMaterializationDependencyV1> Blockers => BlockersValue;
    public static IReadOnlyList<StableToken> FailureCodes
        => BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();

        if (BlockersValue.Count != 4)
            throw new InvalidDataException("qa04.environment.materialization-dependency-count-drift");
        if (BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.environment.materialization-dependency-id-duplicate");
        if (BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.environment.materialization-dependency-code-duplicate");
        if (BlockersValue.Any(static blocker => !Enum.IsDefined(blocker.Kind)))
            throw new InvalidDataException("qa04.environment.materialization-dependency-kind-invalid");

        var ids = BlockersValue.Select(static blocker => blocker.DependencyId.Value).ToArray();
        if (!ids.SequenceEqual(ids.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("qa04.environment.materialization-dependency-order");

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

    private static Qa04EnvironmentMaterializationDependencyV1 Blocker(
        string dependencyId,
        Qa04EnvironmentMaterializationDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));
}
