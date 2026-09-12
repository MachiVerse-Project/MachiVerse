using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Persistence;
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
/// BodyRegionStateV1 の旧未決定7項目を、実装済みの exact nested schema に対する回帰 guard として保持する。
/// Blockers は0件であり、schema field/order/scalar/optionality/vocabulary/reference boundary の drift を検出する。
/// </summary>
public static class Qa04BodyRegionStateSchemaDependencyContractV1
{
    public const string ParentWorldDependencyId = "resident.body-health.body-region-states-schema";
    public const string ParentWorldFailureCode = "qa04.material.body-region-state-schema-undefined";
    public const string ParentPartitionId = "resident.body_health";
    public const string ParentFieldName = "body_region_states";

    private static readonly IReadOnlyList<Qa04BodyRegionStateSchemaDependencyV1> BlockersValue =
        Array.Empty<Qa04BodyRegionStateSchemaDependencyV1>();

    public static IReadOnlyList<Qa04BodyRegionStateSchemaDependencyV1> Blockers => BlockersValue;
    public static IReadOnlyList<StableToken> FailureCodes => Array.Empty<StableToken>();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        ValidateParentPayloadBoundary();
        ValidateNestedSchema();
        ValidateVocabularyAndGenesis();
        ValidateParentWorldBlockerRemoved();
    }

    public static IReadOnlyList<ICanonicalDomainNestedValueV1> CreateCanonicalGenesis()
    {
        var values = ResidentBodyRegionStateNestedValueV1.CanonicalRegions
            .Select(static region => (ICanonicalDomainNestedValueV1)new ResidentBodyRegionStateNestedValueV1(
                region,
                IntegrityPpm: 1_000_000,
                FunctionCapacityPpm: 1_000_000,
                PainPpm: 0,
                InjuryLoadPpm: 0,
                DiseaseLoadPpm: 0,
                ImpairmentPpm: 0,
                RecoveryPpm: 1_000_000))
            .ToArray();
        StandardDomainNestedSnapshotCodecRegistryV1.Default.ValidateOrderedList(
            ParentPartitionId,
            ParentFieldName,
            values);
        return Array.AsReadOnly(values);
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

    private static void ValidateNestedSchema()
    {
        var codec = StandardDomainNestedSnapshotCodecRegistryV1.Default.GetForBinding(ParentPartitionId, ParentFieldName);
        var descriptor = codec.Descriptor;
        if (descriptor.Schema.SchemaId.Value != "domain.resident.body-region-state" ||
            descriptor.Schema.Version.Major != 1 || descriptor.Schema.Version.Minor != 0)
            throw new InvalidDataException("qa04.body-region.schema-id-version-drift");

        var expected = new[]
        {
            ("region_token", DomainPayloadFieldKindV1.Token, false),
            ("integrity_ppm", DomainPayloadFieldKindV1.Ratio, false),
            ("function_capacity_ppm", DomainPayloadFieldKindV1.Ratio, false),
            ("pain_ppm", DomainPayloadFieldKindV1.Ratio, false),
            ("injury_load_ppm", DomainPayloadFieldKindV1.Ratio, false),
            ("disease_load_ppm", DomainPayloadFieldKindV1.Ratio, false),
            ("impairment_ppm", DomainPayloadFieldKindV1.Ratio, false),
            ("recovery_ppm", DomainPayloadFieldKindV1.Ratio, false),
        };
        var actual = descriptor.Fields.Select(static field => (field.Name, field.Kind, field.Optional)).ToArray();
        if (!actual.SequenceEqual(expected))
            throw new InvalidDataException("qa04.body-region.nested-schema-drift");
        if (descriptor.Fields.Any(static field => field.Kind is DomainPayloadFieldKindV1.Ref or DomainPayloadFieldKindV1.RefList))
            throw new InvalidDataException("qa04.body-region.reference-boundary-drift");
    }

    private static void ValidateVocabularyAndGenesis()
    {
        var expectedRegions = new[]
        {
            "body.arm.left",
            "body.arm.right",
            "body.head",
            "body.leg.left",
            "body.leg.right",
            "body.systemic",
            "body.torso",
        };
        if (!ResidentBodyRegionStateNestedValueV1.CanonicalRegions
                .Select(static token => token.Value)
                .SequenceEqual(expectedRegions, StringComparer.Ordinal))
            throw new InvalidDataException("qa04.body-region.region-vocabulary-drift");

        var genesis = CreateCanonicalGenesis();
        if (genesis.Count != 7 || genesis.Any(static value =>
                value is not ResidentBodyRegionStateNestedValueV1 region ||
                region.IntegrityPpm != 1_000_000 ||
                region.FunctionCapacityPpm != 1_000_000 ||
                region.PainPpm != 0 ||
                region.InjuryLoadPpm != 0 ||
                region.DiseaseLoadPpm != 0 ||
                region.ImpairmentPpm != 0 ||
                region.RecoveryPpm != 1_000_000))
            throw new InvalidDataException("qa04.body-region.genesis-drift");
    }

    private static void ValidateParentWorldBlockerRemoved()
    {
        if (Qa04ReferenceWorldDependencyContractV1.Blockers.Any(
                static blocker => blocker.DependencyId.Value == ParentWorldDependencyId ||
                                  blocker.FailureCode.Value == ParentWorldFailureCode))
            throw new InvalidDataException("qa04.body-region.parent-world-blocker-still-present");
    }
}
