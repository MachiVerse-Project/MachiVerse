using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim04PayloadValidationSmoke
{
    private sealed class Resolver(IEnumerable<PartitionRecordRefV1> existing) : IDomainRecordReferenceResolverV1
    {
        private readonly HashSet<PartitionRecordRefV1> _existing = existing.ToHashSet();
        public bool Exists(PartitionRecordRefV1 reference) => _existing.Contains(reference);
    }

    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        if (StandardDomainPayloadSchemaRegistry.Entries.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidOperationException("SIM-04 payload schema registry must cover all 97 standard partitions.");

        var resident = new PartitionRecordRefV1(
            "resident.identity_lifecycle",
            OpaqueId128.Parse("00000000000000000000000000000101"));
        var parentA = new PartitionRecordRefV1(
            "resident.identity_lifecycle",
            OpaqueId128.Parse("00000000000000000000000000000102"));
        var parentB = new PartitionRecordRefV1(
            "resident.identity_lifecycle",
            OpaqueId128.Parse("00000000000000000000000000000103"));
        var resolver = new Resolver([resident, parentA, parentB]);
        var validator = new StandardDomainPayloadValidatorV1();

        var physiology = new Dictionary<string, object?>
        {
            ["resident_ref"] = resident,
            ["hunger_ppm"] = 100_000u,
            ["thirst_ppm"] = 200_000u,
            ["fatigue_ppm"] = 300_000u,
            ["sleep_pressure_ppm"] = 400_000u,
            ["thermal_stress_ppm"] = 500_000u,
            ["hygiene_ppm"] = 600_000u,
        };
        validator.Validate("resident.physiology", physiology, resolver);

        var missing = new Dictionary<string, object?>(physiology, StringComparer.Ordinal);
        missing.Remove("hygiene_ppm");
        RequireReject(
            () => validator.Validate("resident.physiology", missing, resolver),
            "domain.payload.required-field:resident.physiology:hygiene_ppm");

        var outOfRange = new Dictionary<string, object?>(physiology, StringComparer.Ordinal)
        {
            ["hunger_ppm"] = 1_000_001u,
        };
        RequireReject(
            () => validator.Validate("resident.physiology", outOfRange, resolver),
            "domain.payload.scalar-range:resident.physiology:hunger_ppm");

        RequireReject(
            () => validator.Validate("resident.physiology", physiology, new Resolver(Array.Empty<PartitionRecordRefV1>())),
            "domain.payload.reference-validation:resident.physiology:resident_ref");

        var lineage = new Dictionary<string, object?>
        {
            ["resident_ref"] = resident,
            ["parent_refs"] = new PartitionRecordRefV1[] { parentA, parentB },
            ["child_refs"] = Array.Empty<PartitionRecordRefV1>(),
            ["family_relation_refs"] = Array.Empty<PartitionRecordRefV1>(),
            ["generation_index"] = 0,
        };
        validator.Validate("resident.family_lineage", lineage, resolver);

        lineage["parent_refs"] = new PartitionRecordRefV1[] { parentB, parentA };
        RequireReject(
            () => validator.Validate("resident.family_lineage", lineage, resolver),
            "domain.payload.canonical-list-order:resident.family_lineage:parent_refs");

        var unknownField = new Dictionary<string, object?>(physiology, StringComparer.Ordinal)
        {
            ["presentation_only"] = 1u,
        };
        RequireReject(
            () => validator.Validate("resident.physiology", unknownField, resolver),
            "domain.payload.unknown-field:resident.physiology:presentation_only");
    }

    private static void RequireReject(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException($"Expected SIM-04 validation rejection: {expectedMessage}");
    }
}
