using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04BodyRegionStateSchemaDependencyKindV1 : byte
{
    FieldSet = 1,
    FieldOrder = 2,
    ScalarSemantics = 3,
    Optionality = 4,
    ConditionRepresentation = 5,
    RegionVocabulary = 6,
    ReferenceClosure = 7,
}

public sealed record Qa04BodyRegionStateSchemaDependencyV1(
    StableToken DependencyId,
    Qa04BodyRegionStateSchemaDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// resident.body_health.body_region_states の exact nested schema を確定するために残っている
/// 正本依存を fail-closed で列挙する。
///
/// この契約は Qa04ReferenceWorldDependencyContractV1 の NestedPayloadSchema blocker 1件の
/// 下位診断契約であり、reference-world blocker 数や互換 failure code を変更しない。
/// Phase 3 の概念的な身体・健康 semantics や whole-resident の ResidentHealthStateV1 から、
/// BodyRegionStateV1 の field を推測して schema 化してはならない。
/// </summary>
public static class Qa04BodyRegionStateSchemaDependencyContractV1
{
    public const string ParentWorldDependencyId = "resident.body-health.body-region-states-schema";
    public const string ParentWorldFailureCode = "qa04.material.body-region-state-schema-undefined";
    public const string ParentPartitionId = "resident.body_health";
    public const string ParentFieldName = "body_region_states";

    private static readonly IReadOnlyList<Qa04BodyRegionStateSchemaDependencyV1> BlockersValue = Array.AsReadOnly(new[]
    {
        Blocker(
            "body-region.schema.condition-representation",
            Qa04BodyRegionStateSchemaDependencyKindV1.ConditionRepresentation,
            "qa04.body-region.condition-representation-undefined"),
        Blocker(
            "body-region.schema.field-order",
            Qa04BodyRegionStateSchemaDependencyKindV1.FieldOrder,
            "qa04.body-region.field-order-undefined"),
        Blocker(
            "body-region.schema.field-set",
            Qa04BodyRegionStateSchemaDependencyKindV1.FieldSet,
            "qa04.body-region.field-set-undefined"),
        Blocker(
            "body-region.schema.optionality",
            Qa04BodyRegionStateSchemaDependencyKindV1.Optionality,
            "qa04.body-region.optionality-undefined"),
        Blocker(
            "body-region.schema.reference-closure",
            Qa04BodyRegionStateSchemaDependencyKindV1.ReferenceClosure,
            "qa04.body-region.reference-closure-undefined"),
        Blocker(
            "body-region.schema.region-vocabulary",
            Qa04BodyRegionStateSchemaDependencyKindV1.RegionVocabulary,
            "qa04.body-region.region-vocabulary-undefined"),
        Blocker(
            "body-region.schema.scalar-semantics",
            Qa04BodyRegionStateSchemaDependencyKindV1.ScalarSemantics,
            "qa04.body-region.scalar-semantics-undefined"),
    }
    .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
    .ToArray());

    public static IReadOnlyList<Qa04BodyRegionStateSchemaDependencyV1> Blockers => BlockersValue;

    public static IReadOnlyList<StableToken> FailureCodes
        => BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();

        if (BlockersValue.Count != 7)
            throw new InvalidDataException("qa04.body-region.dependency-blocker-count-drift");
        if (BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.body-region.dependency-blocker-id-duplicate");
        if (BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.body-region.dependency-blocker-code-duplicate");
        if (BlockersValue.Any(static blocker => !Enum.IsDefined(blocker.Kind)))
            throw new InvalidDataException("qa04.body-region.dependency-blocker-kind-invalid");

        var ordered = BlockersValue.Select(static blocker => blocker.DependencyId.Value).ToArray();
        if (!ordered.SequenceEqual(ordered.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("qa04.body-region.dependency-blocker-order");

        ValidateParentPayloadBoundary();
        ValidateParentWorldBlocker();
    }

    private static void ValidateParentPayloadBoundary()
    {
        var descriptor = StandardDomainPayloadSchemaRegistry.Get(ParentPartitionId);
        var expected = new[]
        {
            ("resident_ref", DomainPayloadFieldKindV1.Ref, false),
            ("development_ppm", DomainPayloadFieldKindV1.Ratio, false),
            ("health_capacity_ppm", DomainPayloadFieldKindV1.Ratio, false),
            (ParentFieldName, DomainPayloadFieldKindV1.OrderedNestedList, false),
            ("injury_refs", DomainPayloadFieldKindV1.RefList, false),
            ("disease_refs", DomainPayloadFieldKindV1.RefList, false),
            ("recovery_ppm", DomainPayloadFieldKindV1.Ratio, false),
        };
        var actual = descriptor.Fields
            .Select(static field => (field.Name, field.Kind, field.Optional))
            .ToArray();

        if (!actual.SequenceEqual(expected))
            throw new InvalidDataException("qa04.body-region.parent-payload-boundary-drift");
    }

    private static void ValidateParentWorldBlocker()
    {
        var parent = Qa04ReferenceWorldDependencyContractV1.Blockers.SingleOrDefault(
            static blocker => blocker.DependencyId.Value == ParentWorldDependencyId)
            ?? throw new InvalidDataException("qa04.body-region.parent-world-blocker-missing");

        if (parent.Kind != Qa04ReferenceDependencyBlockerKindV1.NestedPayloadSchema ||
            parent.PartitionId?.Value != ParentPartitionId ||
            !string.Equals(parent.FieldName, ParentFieldName, StringComparison.Ordinal) ||
            parent.FailureCode.Value != ParentWorldFailureCode)
            throw new InvalidDataException("qa04.body-region.parent-world-blocker-drift");

        var worldCodes = Qa04ReferenceWorldDependencyContractV1.FailureCodes
            .Select(static code => code.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (FailureCodes.Any(code => worldCodes.Contains(code.Value)))
            throw new InvalidDataException("qa04.body-region.subdependency-code-collides-with-world-blocker");
    }

    private static Qa04BodyRegionStateSchemaDependencyV1 Blocker(
        string dependencyId,
        Qa04BodyRegionStateSchemaDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));
}
