using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04EnvironmentD0PartitionMaterializationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04EnvironmentD0PartitionMaterializerV1.ValidateCanonicalContract();
        VerifyAtmosphereEnvelopeAndPartition();
        VerifyGroundwaterTopologyRefValidation();
        VerifyMissingReferenceFailsClosed();
    }

    private static void VerifyAtmosphereEnvelopeAndPartition()
    {
        var scope = Ref("spatial.scope_registry", "00000000000000000000000000e00001");
        var resolver = new Resolver([scope]);
        var slice = Qa04EnvironmentReferenceDecompositionV1.Get(EnvironmentAtmospherePayloadV1.PartitionId);
        var partition = Qa04EnvironmentD0PartitionMaterializerV1.MaterializePartition(
            EnvironmentAtmospherePayloadV1.PartitionId,
            recordCount: 2,
            binding => Atmosphere(binding, scope),
            static payload => payload.ToStandardPayload(),
            resolver);

        Require(partition.ItemCount == 2,
            "QA-04 Environment D0 atmosphere partition count drifted.");
        var records = partition.RecordsCanonical.ToArray();
        for (var index = 0; index < records.Length; index++)
        {
            Require(records[index].RecordSchema == StandardDomainPartitionRegistry.Get(EnvironmentAtmospherePayloadV1.PartitionId).RecordSchema,
                "Environment D0 materializer must use owner record schema.");
            Require(records[index].Revision == 1 && records[index].CreatedStep == 0 && records[index].RetiredStep is null &&
                    records[index].DetailLevel == DetailLevelV1.D0Entity && records[index].LineageRef is null,
                "Environment D0 genesis envelope drifted.");
        }
        var expectedIds = Enumerable.Range(0, records.Length)
            .Select(index => Qa04EnvironmentReferenceDecompositionV1.BindD0(
                checked(slice.D0StartOrdinal + (ulong)index)).Descriptor.RecordId)
            .OrderBy(static id => id)
            .ToArray();
        Require(records.Select(static record => record.RecordId).SequenceEqual(expectedIds),
            "Environment D0 partition must preserve descriptor identities in canonical RecordId order.");
    }

    private static void VerifyGroundwaterTopologyRefValidation()
    {
        var scope = Ref("spatial.scope_registry", "00000000000000000000000000e00002");
        var binding = Qa04EnvironmentReferenceDecompositionV1.BindD0(
            Qa04EnvironmentReferenceDecompositionV1.Get(EnvironmentGroundwaterPayloadV1.PartitionId).D0StartOrdinal);
        var neighbor = Qa04EnvironmentReferenceDecompositionV1.NextD0RecordRef(
            EnvironmentGroundwaterPayloadV1.PartitionId,
            binding.Descriptor.RecordId);
        var resolver = new Resolver([scope, neighbor]);

        var record = Qa04EnvironmentD0PartitionMaterializerV1.CreateRecord(
            binding.GlobalOrdinal,
            current => Groundwater(current, scope),
            static payload => payload.ToStandardPayload(),
            resolver);
        Require(record.RecordId == binding.Descriptor.RecordId,
            "Environment groundwater D0 descriptor identity drifted.");
        Require(record.Payload.NeighborRefs.Count == 1 && record.Payload.NeighborRefs[0] == neighbor,
            "Environment groundwater must use canonical next-record topology Ref.");
    }

    private static void VerifyMissingReferenceFailsClosed()
    {
        var scope = Ref("spatial.scope_registry", "00000000000000000000000000e00003");
        var slice = Qa04EnvironmentReferenceDecompositionV1.Get(EnvironmentAtmospherePayloadV1.PartitionId);
        ExpectInvalid(() => _ = Qa04EnvironmentD0PartitionMaterializerV1.CreateRecord(
            slice.D0StartOrdinal,
            binding => Atmosphere(binding, scope),
            static payload => payload.ToStandardPayload(),
            new Resolver(Array.Empty<PartitionRecordRefV1>())));
    }

    private static EnvironmentAtmospherePayloadV1 Atmosphere(
        Qa04EnvironmentD0BindingV1 binding,
        PartitionRecordRefV1 scope)
    {
        var id = binding.Descriptor.RecordId;
        return new EnvironmentAtmospherePayloadV1(
            scope,
            PressurePa: checked((int)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "pressure_pa")),
            TemperatureMilliKelvin: checked((int)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "temperature_mk")),
            HumidityPpm: Qa04ReferenceGenesisValueSourceV1.BoundedPpm(id, "humidity_ppm"),
            new Vec3Int64V1(
                Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(id, "wind_um_s.x"),
                Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(id, "wind_um_s.y"),
                Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(id, "wind_um_s.z")),
            VaporMassGram: checked((long)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "vapor_mass_g")),
            LiquidMassGram: checked((long)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "liquid_mass_g")),
            Array.AsReadOnly(new[]
            {
                new KeyValuePair<string, uint>("perf.gas-a", 780_000_000u),
                new KeyValuePair<string, uint>("perf.gas-b", 210_000_000u),
                new KeyValuePair<string, uint>("perf.gas-c", 10_000_000u),
            }));
    }

    private static EnvironmentGroundwaterPayloadV1 Groundwater(
        Qa04EnvironmentD0BindingV1 binding,
        PartitionRecordRefV1 scope)
    {
        var id = binding.Descriptor.RecordId;
        return new EnvironmentGroundwaterPayloadV1(
            scope,
            WaterVolumeMl: checked((long)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "water_volume_ml")),
            HydraulicHeadMm: Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(id, "hydraulic_head_mm"),
            QualityPpm: Qa04ReferenceGenesisValueSourceV1.BoundedPpm(id, "quality_ppm"),
            TemperatureMilliKelvin: checked((int)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "temperature_mk")),
            Array.AsReadOnly(new[]
            {
                Qa04EnvironmentReferenceDecompositionV1.NextD0RecordRef(
                    EnvironmentGroundwaterPayloadV1.PartitionId,
                    id),
            }));
    }

    private static PartitionRecordRefV1 Ref(string partitionId, string id)
        => new(partitionId, OpaqueId128.Parse(id));

    private static void ExpectInvalid(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("Expected Environment D0 missing Ref rejection.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

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
}
