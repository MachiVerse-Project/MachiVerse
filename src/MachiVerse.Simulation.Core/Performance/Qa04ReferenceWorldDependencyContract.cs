using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04ReferenceDependencyBlockerKindV1 : byte
{
    AuthorityTarget = 1,
    PartitionMapping = 2,
    PersistentAuthority = 3,
    NestedPayloadSchema = 4,
    RecordSchema = 5,
    CanonicalMaterial = 6,
}

public sealed record Qa04ReferenceDependencyBlockerV1(
    StableToken DependencyId,
    Qa04ReferenceDependencyBlockerKindV1 Kind,
    StableToken? PartitionId,
    string? FieldName,
    StableToken FailureCode);

/// <summary>
/// Exact fail-closed list of implementation dependencies that still prevent perf.reference.v1 from
/// becoming authoritative material. Normative design may already exist; each entry remains until
/// its production schema/mapping/authority/material path and recovery proof are implemented.
/// </summary>
public static class Qa04ReferenceWorldDependencyContractV1
{
    private static readonly IReadOnlyList<Qa04ReferenceDependencyBlockerV1> BlockersValue = Array.AsReadOnly(new[]
    {
        PartitionMapping(
            "environment.d0-cell-cohort.partition-mapping",
            "qa04.material.environment-d0-partition-mapping-undefined"),
        PartitionMapping(
            "environment.d1-aggregate.partition-mapping",
            "qa04.material.environment-d1-partition-mapping-undefined"),
        PartitionMapping(
            "society-governance.active-record.partition-mapping",
            "qa04.material.society-governance-partition-mapping-undefined"),
        RecordSchema(
            "society.market-transaction.market-ref-target",
            "society.market_transaction",
            "market_ref",
            "qa04.material.market-ref-authority-undefined"),
        RecordSchema(
            "infrastructure.network-topology.node-edge-targets",
            "infrastructure.network_topology",
            "node_refs/edge_refs",
            "qa04.material.infrastructure-node-edge-authority-undefined"),
        CanonicalMaterial(
            "spatial.terrain-geometry.root-brick-target",
            "spatial.terrain_geometry",
            "root_brick_ref",
            "qa04.material.terrain-brick-authority-undefined"),
        PersistentAuthority(
            "transaction.active-cross-domain.persistent-authority",
            "qa04.material.cross-domain-transaction-authority-undefined"),
    }
    .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
    .ToArray());

    public static IReadOnlyList<Qa04ReferenceDependencyBlockerV1> Blockers => BlockersValue;

    public static IReadOnlyList<StableToken> FailureCodes
        => BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        if (BlockersValue.Count != 7)
            throw new InvalidDataException("qa04.material.dependency-blocker-count-drift");
        if (BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.material.dependency-blocker-id-duplicate");
        if (BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.material.dependency-blocker-code-duplicate");

        string? previous = null;
        foreach (var blocker in BlockersValue)
        {
            if (!Enum.IsDefined(blocker.Kind))
                throw new InvalidDataException("qa04.material.dependency-blocker-kind-invalid");
            if (previous is not null && string.CompareOrdinal(previous, blocker.DependencyId.Value) >= 0)
                throw new InvalidDataException("qa04.material.dependency-blocker-order");
            previous = blocker.DependencyId.Value;

            if (blocker.PartitionId is { } partition)
            {
                _ = StandardDomainPartitionRegistry.Get(partition.Value);
                if (string.IsNullOrWhiteSpace(blocker.FieldName))
                    throw new InvalidDataException("qa04.material.dependency-blocker-field-required");
            }
            else if (blocker.FieldName is not null)
            {
                throw new InvalidDataException("qa04.material.dependency-blocker-field-without-partition");
            }
        }

        Require(
            "society.market-transaction.market-ref-target",
            Qa04ReferenceDependencyBlockerKindV1.RecordSchema,
            "society.market_transaction",
            "market_ref",
            "qa04.material.market-ref-authority-undefined");
        Require(
            "spatial.terrain-geometry.root-brick-target",
            Qa04ReferenceDependencyBlockerKindV1.CanonicalMaterial,
            "spatial.terrain_geometry",
            "root_brick_ref",
            "qa04.material.terrain-brick-authority-undefined");
        RequireUnscoped(
            "transaction.active-cross-domain.persistent-authority",
            Qa04ReferenceDependencyBlockerKindV1.PersistentAuthority,
            "qa04.material.cross-domain-transaction-authority-undefined");
    }

    private static void Require(
        string dependencyId,
        Qa04ReferenceDependencyBlockerKindV1 kind,
        string partitionId,
        string fieldName,
        string failureCode)
    {
        var blocker = BlockersValue.SingleOrDefault(value => value.DependencyId.Value == dependencyId)
            ?? throw new InvalidDataException($"qa04.material.dependency-blocker-missing:{dependencyId}");
        if (blocker.Kind != kind ||
            blocker.PartitionId?.Value != partitionId ||
            !string.Equals(blocker.FieldName, fieldName, StringComparison.Ordinal) ||
            blocker.FailureCode.Value != failureCode)
            throw new InvalidDataException($"qa04.material.dependency-blocker-drift:{dependencyId}");
    }

    private static void RequireUnscoped(
        string dependencyId,
        Qa04ReferenceDependencyBlockerKindV1 kind,
        string failureCode)
    {
        var blocker = BlockersValue.SingleOrDefault(value => value.DependencyId.Value == dependencyId)
            ?? throw new InvalidDataException($"qa04.material.dependency-blocker-missing:{dependencyId}");
        if (blocker.Kind != kind ||
            blocker.PartitionId is not null ||
            blocker.FieldName is not null ||
            blocker.FailureCode.Value != failureCode)
            throw new InvalidDataException($"qa04.material.dependency-blocker-drift:{dependencyId}");
    }

    private static Qa04ReferenceDependencyBlockerV1 PartitionScoped(
        string dependencyId,
        Qa04ReferenceDependencyBlockerKindV1 kind,
        string partitionId,
        string fieldName,
        string failureCode)
        => new(
            new StableToken(dependencyId),
            kind,
            new StableToken(partitionId),
            fieldName,
            new StableToken(failureCode));

    private static Qa04ReferenceDependencyBlockerV1 RecordSchema(
        string dependencyId,
        string partitionId,
        string fieldName,
        string failureCode)
        => PartitionScoped(
            dependencyId,
            Qa04ReferenceDependencyBlockerKindV1.RecordSchema,
            partitionId,
            fieldName,
            failureCode);

    private static Qa04ReferenceDependencyBlockerV1 CanonicalMaterial(
        string dependencyId,
        string partitionId,
        string fieldName,
        string failureCode)
        => PartitionScoped(
            dependencyId,
            Qa04ReferenceDependencyBlockerKindV1.CanonicalMaterial,
            partitionId,
            fieldName,
            failureCode);

    private static Qa04ReferenceDependencyBlockerV1 PartitionMapping(
        string dependencyId,
        string failureCode)
        => new(
            new StableToken(dependencyId),
            Qa04ReferenceDependencyBlockerKindV1.PartitionMapping,
            null,
            null,
            new StableToken(failureCode));

    private static Qa04ReferenceDependencyBlockerV1 PersistentAuthority(
        string dependencyId,
        string failureCode)
        => new(
            new StableToken(dependencyId),
            Qa04ReferenceDependencyBlockerKindV1.PersistentAuthority,
            null,
            null,
            new StableToken(failureCode));

    private static Qa04ReferenceDependencyBlockerV1 NestedPayloadSchema(
        string dependencyId,
        string partitionId,
        string fieldName,
        string failureCode)
        => PartitionScoped(
            dependencyId,
            Qa04ReferenceDependencyBlockerKindV1.NestedPayloadSchema,
            partitionId,
            fieldName,
            failureCode);
}
