using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class CanonicalResidentBodyRegionNestedSnapshotInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    private static void Run()
    {
        VerifySchemaAndCanonicalVocabulary();
        VerifyBodyHealthRoundTrip();
        VerifyFailClosedValidation();
    }

    private static void VerifySchemaAndCanonicalVocabulary()
    {
        var codec = StandardDomainNestedSnapshotCodecRegistryV1.Default.GetForBinding(
            ResidentBodyHealthPayloadV1.PartitionId,
            "body_region_states");
        var schema = codec.Descriptor;
        Require(schema.Schema.SchemaId.Value == "domain.resident.body-region-state",
            "BodyRegionState nested schema id drifted.");
        Require(schema.Schema.Version.Major == 1 && schema.Schema.Version.Minor == 0,
            "BodyRegionState nested schema version must be 1.0.");

        var expectedFields = new[]
        {
            "region_token",
            "integrity_ppm",
            "function_capacity_ppm",
            "pain_ppm",
            "injury_load_ppm",
            "disease_load_ppm",
            "impairment_ppm",
            "recovery_ppm",
        };
        Require(schema.Fields.Select(static field => field.Name).SequenceEqual(expectedFields, StringComparer.Ordinal),
            "BodyRegionState field order drifted.");
        Require(schema.Fields.All(static field => !field.Optional),
            "BodyRegionState 1.0 fields must all be required.");

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
        Require(ResidentBodyRegionStateNestedValueV1.CanonicalRegions
                .Select(static token => token.Value)
                .SequenceEqual(expectedRegions, StringComparer.Ordinal),
            "BodyRegionState canonical region vocabulary drifted.");
    }

    private static void VerifyBodyHealthRoundTrip()
    {
        var regions = ResidentBodyRegionStateNestedValueV1.CanonicalRegions
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

        var payload = new ResidentBodyHealthPayloadV1(
            new PartitionRecordRefV1(
                ResidentIdentityLifecyclePayloadV1.PartitionId,
                OpaqueId128.Parse("000000000000000000000000000bb001")),
            DevelopmentPpm: 1_000_000,
            HealthCapacityPpm: 1_000_000,
            BodyRegionStates: Array.AsReadOnly(regions),
            InjuryRefs: Array.Empty<PartitionRecordRefV1>(),
            DiseaseRefs: Array.Empty<PartitionRecordRefV1>(),
            RecoveryPpm: 1_000_000);

        var wire = DomainPartitionSnapshotWireCodecV1.EncodePayload(
            ResidentBodyHealthPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var decoded = DomainPartitionSnapshotWireCodecV1.DecodePayload(
            ResidentBodyHealthPayloadV1.PartitionId,
            wire,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var round = ResidentBodyHealthPayloadV1.FromStandardPayload(decoded);

        Require(round.BodyRegionStates.Count == 7,
            "BodyRegionState round-trip must preserve all seven canonical regions.");
        for (var index = 0; index < regions.Length; index++)
        {
            Require(round.BodyRegionStates[index] is ResidentBodyRegionStateNestedValueV1 actual &&
                    actual == (ResidentBodyRegionStateNestedValueV1)regions[index],
                $"BodyRegionState round-trip mismatch at index {index}.");
        }
        Require(round.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()),
            "BodyRegionState semantic digest must survive wire round-trip.");
    }

    private static void VerifyFailClosedValidation()
    {
        var valid = ResidentBodyRegionStateNestedValueV1.CanonicalRegions
            .Select(static region => (ICanonicalDomainNestedValueV1)new ResidentBodyRegionStateNestedValueV1(
                region, 1_000_000, 1_000_000, 0, 0, 0, 0, 1_000_000))
            .ToArray();

        ExpectInvalid("reversed body-region list must be rejected", () =>
            StandardDomainNestedSnapshotCodecRegistryV1.Default.ValidateOrderedList(
                ResidentBodyHealthPayloadV1.PartitionId,
                "body_region_states",
                valid.Reverse().ToArray()));

        ExpectInvalid("duplicate body-region token must be rejected", () =>
            StandardDomainNestedSnapshotCodecRegistryV1.Default.ValidateOrderedList(
                ResidentBodyHealthPayloadV1.PartitionId,
                "body_region_states",
                new[] { valid[0], valid[0] }));

        ExpectInvalid("unknown body-region token must be rejected", () =>
            new ResidentBodyRegionStateNestedValueV1(
                new StableToken("body.unknown"), 1_000_000, 1_000_000, 0, 0, 0, 0, 1_000_000)
                .ValidateCanonical());

        ExpectInvalid("body-region ppm above one million must be rejected", () =>
            new ResidentBodyRegionStateNestedValueV1(
                new StableToken("body.head"), 1_000_001, 1_000_000, 0, 0, 0, 0, 1_000_000)
                .ValidateCanonical());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectInvalid(string name, Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected rejection: {name}.");
    }
}
