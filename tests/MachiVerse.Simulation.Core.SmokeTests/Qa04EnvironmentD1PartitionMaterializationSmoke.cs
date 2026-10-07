using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04EnvironmentD1PartitionMaterializationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04EnvironmentD1PartitionMaterializerV1.ValidateCanonicalContract();

        var scope = new PartitionRecordRefV1(
            "spatial.scope_registry",
            OpaqueId128.Parse("00000000000000000000000000e30001"));
        var resolver = new Resolver([scope]);
        var slice = Qa04EnvironmentReferenceDecompositionV1.Get(EnvironmentAtmospherePayloadV1.PartitionId);
        var partition = Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
            EnvironmentAtmospherePayloadV1.PartitionId,
            recordCount: 2,
            binding => AggregateAtmosphere(binding, scope),
            static payload => payload.ToStandardPayload(),
            resolver);

        Require(partition.ItemCount == 2,
            "QA-04 Environment D1 reduced partition count drifted.");
        var records = partition.RecordsCanonical.ToArray();
        Require(records.All(static record =>
                record.Revision == 1 &&
                record.CreatedStep == 0 &&
                record.RetiredStep is null &&
                record.DetailLevel == DetailLevelV1.D1LocalAggregate &&
                record.LineageRef is null),
            "Environment D1 genesis envelope drifted.");

        var expectedIds = new[]
        {
            Qa04EnvironmentReferenceDecompositionV1.BindD1(slice.D1StartOrdinal).Descriptor.RecordId,
            Qa04EnvironmentReferenceDecompositionV1.BindD1(slice.D1StartOrdinal + 1).Descriptor.RecordId,
        }.OrderBy(static id => id).ToArray();
        Require(records.Select(static record => record.RecordId).SequenceEqual(expectedIds),
            "Environment D1 materializer must preserve canonical descriptor identities.");

        for (ulong offset = 0; offset < 2; offset++)
        {
            var binding = Qa04EnvironmentReferenceDecompositionV1.BindD1(slice.D1StartOrdinal + offset);
            Require(binding.SourceD0GlobalOrdinals.Count == 4,
                "Environment D1 materializer must retain exact four-source binding.");
            Require(binding.SourceD0GlobalOrdinals.All(source =>
                    Qa04EnvironmentReferenceDecompositionV1.BindD0(source).PartitionId == binding.PartitionId),
                "Environment D1 sources must remain in the same partition.");
        }
    }

    private static EnvironmentAtmospherePayloadV1 AggregateAtmosphere(
        Qa04EnvironmentD1BindingV1 binding,
        PartitionRecordRefV1 scope)
    {
        var sourceIds = binding.SourceD0GlobalOrdinals
            .Select(source => Qa04EnvironmentReferenceDecompositionV1.BindD0(source).Descriptor.RecordId)
            .ToArray();
        var pressure = Qa04EnvironmentD1AggregationV1.MeanRoundToEven(
            sourceIds.Select(id => checked((int)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "pressure_pa"))).ToArray());
        var temperature = Qa04EnvironmentD1AggregationV1.MeanRoundToEven(
            sourceIds.Select(id => checked((int)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "temperature_mk"))).ToArray());
        var humidity = Qa04EnvironmentD1AggregationV1.MeanRoundToEven(
            sourceIds.Select(id => Qa04ReferenceGenesisValueSourceV1.BoundedPpm(id, "humidity_ppm")).ToArray());
        var wind = Qa04EnvironmentD1AggregationV1.MeanRoundToEven(
            sourceIds.Select(id => new Vec3Int64V1(
                Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(id, "wind_um_s.x"),
                Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(id, "wind_um_s.y"),
                Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(id, "wind_um_s.z"))).ToArray());
        var vapor = Qa04EnvironmentD1AggregationV1.CheckedSum(
            sourceIds.Select(id => checked((long)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "vapor_mass_g"))).ToArray());
        var liquid = Qa04EnvironmentD1AggregationV1.CheckedSum(
            sourceIds.Select(id => checked((long)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "liquid_mass_g"))).ToArray());

        return new EnvironmentAtmospherePayloadV1(
            scope,
            pressure,
            temperature,
            humidity,
            wind,
            vapor,
            liquid,
            Qa04EnvironmentGenesisContractV1.AtmosphereGasPpb);
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
