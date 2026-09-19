using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ReferenceRecordSchemaRepairV1(
    StableToken PartitionId,
    SchemaRefV1 CurrentRecordSchema,
    SchemaRefV1 RequiredRecordSchema,
    IReadOnlyList<StableToken> RecordKinds);

public sealed record Qa04ReferenceRefClosureV1(
    StableToken DependencyId,
    StableToken SourcePartitionId,
    string SourceFieldName,
    StableToken TargetPartitionId,
    StableToken TargetRecordKind);

/// <summary>
/// Normative ownership decision for the four perf.reference.v1 orphan-reference gaps identified by
/// the Stage 2 audit. This contract deliberately does not mutate the standard v1 registry: each
/// repaired owner requires an explicit record-schema major version before runtime materialization.
/// Existing dependency blockers therefore remain fail-closed until those v2 schemas, codecs,
/// migrations, target-kind validation, and canonical benchmark material are implemented.
/// </summary>
public static class Qa04ReferenceWorldRefOwnershipContractV1
{
    private static readonly IReadOnlyList<Qa04ReferenceRecordSchemaRepairV1> RepairsValue = Array.AsReadOnly(new[]
    {
        Repair("infrastructure.network_topology", "edge", "network", "node"),
        Repair("physical.occupancy", "collision_shape", "occupancy"),
        Repair("society.market_transaction", "market_state", "order_or_offer", "transaction_or_price_fact"),
        Repair("spatial.terrain_geometry", "terrain_brick", "terrain_root"),
    }
    .OrderBy(static repair => repair.PartitionId.Value, StringComparer.Ordinal)
    .ToArray());

    private static readonly IReadOnlyList<Qa04ReferenceRefClosureV1> ClosuresValue = Array.AsReadOnly(new[]
    {
        Closure(
            "infrastructure.network-topology.edge-refs-target",
            "infrastructure.network_topology",
            "edge_refs",
            "infrastructure.network_topology",
            "edge"),
        Closure(
            "infrastructure.network-topology.node-refs-target",
            "infrastructure.network_topology",
            "node_refs",
            "infrastructure.network_topology",
            "node"),
        Closure(
            "physical.presence.shape-ref-target",
            "physical.presence",
            "shape_ref",
            "physical.occupancy",
            "collision_shape"),
        Closure(
            "society.market-transaction.market-ref-target",
            "society.market_transaction",
            "market_ref",
            "society.market_transaction",
            "market_state"),
        Closure(
            "spatial.terrain-geometry.root-brick-target",
            "spatial.terrain_geometry",
            "root_brick_ref",
            "spatial.terrain_geometry",
            "terrain_brick"),
    }
    .OrderBy(static closure => closure.DependencyId.Value, StringComparer.Ordinal)
    .ToArray());

    public static IReadOnlyList<Qa04ReferenceRecordSchemaRepairV1> RecordSchemaRepairs => RepairsValue;
    public static IReadOnlyList<Qa04ReferenceRefClosureV1> RefClosures => ClosuresValue;

    public static void ValidateCanonicalContract()
    {
        if (RepairsValue.Count != 4)
            throw new InvalidDataException("qa04.material.ref-ownership-repair-count-drift");
        if (ClosuresValue.Count != 5)
            throw new InvalidDataException("qa04.material.ref-ownership-closure-count-drift");

        RequireCanonicalUnique(
            RepairsValue.Select(static repair => repair.PartitionId.Value),
            "qa04.material.ref-ownership-repair-order");
        RequireCanonicalUnique(
            ClosuresValue.Select(static closure => closure.DependencyId.Value),
            "qa04.material.ref-ownership-closure-order");

        foreach (var repair in RepairsValue)
        {
            var identity = StandardDomainPartitionRegistry.Get(repair.PartitionId.Value);
            if (repair.CurrentRecordSchema != identity.RecordSchema)
                throw new InvalidDataException($"qa04.material.ref-ownership-current-schema-drift:{repair.PartitionId.Value}");
            if (repair.RequiredRecordSchema.SchemaId != identity.RecordSchema.SchemaId ||
                repair.RequiredRecordSchema.Version != new SchemaVersionV1(2, 0))
                throw new InvalidDataException($"qa04.material.ref-ownership-required-schema-drift:{repair.PartitionId.Value}");
            if (repair.RecordKinds.Count < 2)
                throw new InvalidDataException($"qa04.material.ref-ownership-record-kind-count:{repair.PartitionId.Value}");
            RequireCanonicalUnique(
                repair.RecordKinds.Select(static kind => kind.Value),
                $"qa04.material.ref-ownership-record-kind-order:{repair.PartitionId.Value}");
        }

        foreach (var closure in ClosuresValue)
        {
            var source = StandardDomainPartitionRegistry.Get(closure.SourcePartitionId.Value);
            var target = StandardDomainPartitionRegistry.Get(closure.TargetPartitionId.Value);
            if (source.OwnerDomain != target.OwnerDomain)
                throw new InvalidDataException($"qa04.material.ref-ownership-cross-owner:{closure.DependencyId.Value}");

            var field = StandardDomainPayloadSchemaRegistry.Get(source.PartitionId.Value).Fields.SingleOrDefault(
                candidate => string.Equals(candidate.Name, closure.SourceFieldName, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"qa04.material.ref-ownership-source-field-missing:{closure.DependencyId.Value}");
            if (field.Kind is not (DomainPayloadFieldKindV1.Ref or DomainPayloadFieldKindV1.RefList))
                throw new InvalidDataException($"qa04.material.ref-ownership-source-field-not-ref:{closure.DependencyId.Value}");

            var targetRepair = RepairsValue.SingleOrDefault(
                repair => repair.PartitionId == closure.TargetPartitionId)
                ?? throw new InvalidDataException($"qa04.material.ref-ownership-target-repair-missing:{closure.DependencyId.Value}");
            if (!targetRepair.RecordKinds.Contains(closure.TargetRecordKind))
                throw new InvalidDataException($"qa04.material.ref-ownership-target-kind-missing:{closure.DependencyId.Value}");
        }

        RequireClosure(
            "physical.presence.shape-ref-target",
            "physical.presence",
            "shape_ref",
            "physical.occupancy",
            "collision_shape");
        RequireClosure(
            "spatial.terrain-geometry.root-brick-target",
            "spatial.terrain_geometry",
            "root_brick_ref",
            "spatial.terrain_geometry",
            "terrain_brick");
        RequireClosure(
            "society.market-transaction.market-ref-target",
            "society.market_transaction",
            "market_ref",
            "society.market_transaction",
            "market_state");
    }

    private static Qa04ReferenceRecordSchemaRepairV1 Repair(string partitionId, params string[] recordKinds)
    {
        var identity = StandardDomainPartitionRegistry.Get(partitionId);
        return new Qa04ReferenceRecordSchemaRepairV1(
            identity.PartitionId,
            identity.RecordSchema,
            new SchemaRefV1(identity.RecordSchema.SchemaId, new SchemaVersionV1(2, 0)),
            Array.AsReadOnly(recordKinds
                .Select(static kind => new StableToken(kind))
                .OrderBy(static kind => kind.Value, StringComparer.Ordinal)
                .ToArray()));
    }

    private static Qa04ReferenceRefClosureV1 Closure(
        string dependencyId,
        string sourcePartitionId,
        string sourceFieldName,
        string targetPartitionId,
        string targetRecordKind)
        => new(
            new StableToken(dependencyId),
            new StableToken(sourcePartitionId),
            sourceFieldName,
            new StableToken(targetPartitionId),
            new StableToken(targetRecordKind));

    private static void RequireClosure(
        string dependencyId,
        string sourcePartitionId,
        string sourceFieldName,
        string targetPartitionId,
        string targetRecordKind)
    {
        var closure = ClosuresValue.SingleOrDefault(value => value.DependencyId.Value == dependencyId)
            ?? throw new InvalidDataException($"qa04.material.ref-ownership-closure-missing:{dependencyId}");
        if (closure.SourcePartitionId.Value != sourcePartitionId ||
            !string.Equals(closure.SourceFieldName, sourceFieldName, StringComparison.Ordinal) ||
            closure.TargetPartitionId.Value != targetPartitionId ||
            closure.TargetRecordKind.Value != targetRecordKind)
            throw new InvalidDataException($"qa04.material.ref-ownership-closure-drift:{dependencyId}");
    }

    private static void RequireCanonicalUnique(IEnumerable<string> values, string failureCode)
    {
        string? previous = null;
        foreach (var value in values)
        {
            if (previous is not null && string.CompareOrdinal(previous, value) >= 0)
                throw new InvalidDataException(failureCode);
            previous = value;
        }
    }
}
