using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim04PayloadValidationSmoke
{
    private sealed class Resolver(IEnumerable<PartitionRecordRefV1> existing) : IDomainRecordSchemaResolverV1
    {
        private readonly HashSet<PartitionRecordRefV1> _existing = existing.ToHashSet();

        public bool Exists(PartitionRecordRefV1 reference) => _existing.Contains(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            if (!_existing.Contains(reference))
            {
                schema = default;
                return false;
            }

            schema = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value).RecordSchema;
            return true;
        }
    }

    private sealed class WrongSchemaResolver(IEnumerable<PartitionRecordRefV1> existing) : IDomainRecordSchemaResolverV1
    {
        private readonly HashSet<PartitionRecordRefV1> _existing = existing.ToHashSet();

        public bool Exists(PartitionRecordRefV1 reference) => _existing.Contains(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = new SchemaRefV1("domain.spatial.world_frame.record");
            return _existing.Contains(reference);
        }
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
        var validator = new StandardDomainPayloadCodecValidatorV1();

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

        RequireReject(
            () => validator.Validate("resident.physiology", physiology, new WrongSchemaResolver([resident])),
            "domain.payload.reference-schema:resident.physiology:resident_ref");

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

        var detailRequirement = new Dictionary<string, object?>
        {
            ["resident_ref"] = resident,
            ["minimum_detail"] = (byte)4,
            ["scope_ref"] = parentA,
            ["reason"] = "binding",
            ["effective_from"] = 10UL,
        };
        RequireReject(
            () => validator.Validate("participation.detail_requirement", detailRequirement, resolver),
            "domain.payload.scalar-range:participation.detail_requirement:minimum_detail");

        var contaminant = new Dictionary<string, object?>
        {
            ["spatial_scope"] = parentA,
            ["contaminant_kind"] = "smoke",
            ["stock_mass_g"] = 1L,
            ["concentration_ppb"] = 1_000_000_001u,
            ["source_refs"] = Array.Empty<PartitionRecordRefV1>(),
            ["sink_refs"] = Array.Empty<PartitionRecordRefV1>(),
        };
        RequireReject(
            () => validator.Validate("environment.contaminant", contaminant, resolver),
            "domain.payload.scalar-range:environment.contaminant:concentration_ppb");

        var negativeMass = new Dictionary<string, object?>(contaminant, StringComparer.Ordinal)
        {
            ["concentration_ppb"] = 1u,
            ["stock_mass_g"] = -1L,
        };
        RequireReject(
            () => validator.Validate("environment.contaminant", negativeMass, resolver),
            "domain.payload.scalar-range:environment.contaminant:stock_mass_g");
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
