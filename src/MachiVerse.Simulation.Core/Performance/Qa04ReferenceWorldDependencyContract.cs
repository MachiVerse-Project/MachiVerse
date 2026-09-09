using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04ReferenceDependencyBlockerKindV1 : byte
{
    AuthorityTarget = 1,
    PartitionMapping = 2,
    PersistentAuthority = 3,
    NestedPayloadSchema = 4,
}

public sealed record Qa04ReferenceDependencyBlockerV1(
    StableToken DependencyId,
    Qa04ReferenceDependencyBlockerKindV1 Kind,
    StableToken? PartitionId,
    string? FieldName,
    StableToken FailureCode);

/// <summary>
/// Exact fail-closed list of unresolved normative dependencies that prevent perf.reference.v1 from
/// becoming authoritative material. These are not implementation TODO placeholders: each entry is
/// a missing authority target, benchmark-to-partition mapping, persistent authority, or exact nested
/// payload schema that cannot be inferred safely from CLR shape or benchmark counts.
/// </summary>
public static class Qa04ReferenceWorldDependencyContractV1
{
    private static readonly IReadOnlyList<Qa04ReferenceDependencyBlockerV1> BlockersValue = Array.AsReadOnly(new[]
    {
        AuthorityTarget(
            "physical.presence.shape-ref-target",
            "physical.presence",
            "shape_ref",
            "qa04.material.physical-presence-shape-authority-undefined"),
        PartitionMapping(
            "environment.d0-cell-cohort.partition-mapping",
            "qa04.material.environment-d0-partition-mapping-undefined"),
        PartitionMapping(
            "environment.d1-aggregate.partition-mapping",
            "qa04.material.environment-d1-partition-mapping-undefined"),
        PartitionMapping(
            "society-governance.active-record.partition-mapping",
            "qa04.material.society-governance-partition-mapping-undefined"),
        AuthorityTarget(
            "society.market-transaction.market-ref-target",
            "society.market_transaction",
            "market_ref",
            "qa04.material.market-ref-authority-undefined"),
        AuthorityTarget(
            "infrastructure.network-topology.node-edge-targets",
            "infrastructure.network_topology",
            "node_refs/edge_refs",
            "qa04.material.infrastructure-node-edge-authority-undefined"),
        AuthorityTarget(
            "spatial.terrain-geometry.root-brick-target",
            "spatial.terrain_geometry",
            "root_brick_ref",
            "qa04.material.terrain-brick-authority-undefined"),
        PersistentAuthority(
            "transaction.active-cross-domain.persistent-authority",
            "qa04.material.cross-domain-transaction-authority-undefined"),
        NestedPayloadSchema(
            "resident.body-health.body-region-states-schema",
            "resident.body_health",
            "body_region_states",
            "qa04.material.body-region-state-schema-undefined"),
    }
    .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
    .ToArray());

    public static IReadOnlyList<Qa04ReferenceDependencyBlockerV1> Blockers => BlockersValue;

    public static IReadOnlyList<StableToken> FailureCodes
        => BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        if (BlockersValue.Count != 9)
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
            Qa04ReferenceDependencyBlockerKindV1.AuthorityTarget,
            "society.market_transaction",
            "market_ref",
            "qa04.material.market-ref-authority-undefined");
        Require(
            "physical.presence.shape-ref-target",
            Qa04ReferenceDependencyBlockerKindV1.AuthorityTarget,
            "physical.presence",
            "shape_ref",
            "qa04.material.physical-presence-shape-authority-undefined");
        Require(
            "spatial.terrain-geometry.root-brick-target",
            Qa04ReferenceDependencyBlockerKindV1.AuthorityTarget,
            "spatial.terrain_geometry",
            "root_brick_ref",
            "qa04.material.terrain-brick-authority-undefined");
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

    private static Qa04ReferenceDependencyBlockerV1 AuthorityTarget(
        string dependencyId,
        string partitionId,
        string fieldName,
        string failureCode)
        => new(
            new StableToken(dependencyId),
            Qa04ReferenceDependencyBlockerKindV1.AuthorityTarget,
            new StableToken(partitionId),
            fieldName,
            new StableToken(failureCode));

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
        => new(
            new StableToken(dependencyId),
            Qa04ReferenceDependencyBlockerKindV1.NestedPayloadSchema,
            new StableToken(partitionId),
            fieldName,
            new StableToken(failureCode));
}
