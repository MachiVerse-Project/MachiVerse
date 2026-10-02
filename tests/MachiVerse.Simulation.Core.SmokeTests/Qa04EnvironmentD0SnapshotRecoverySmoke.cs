using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04EnvironmentD0SnapshotRecoverySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var scopeA = Ref("spatial.scope_registry", "00000000000000000000000000e10001");
        var scopeB = Ref("spatial.scope_registry", "00000000000000000000000000e10002");
        var scopes = new[] { scopeA, scopeB };
        var resolver = new Resolver(scopes);
        var slice = Qa04EnvironmentReferenceDecompositionV1.Get(EnvironmentAtmospherePayloadV1.PartitionId);

        var partition = Qa04EnvironmentD0PartitionMaterializerV1.MaterializePartition(
            EnvironmentAtmospherePayloadV1.PartitionId,
            recordCount: 2,
            binding => CreateAtmosphere(
                binding,
                Qa04EnvironmentD0PartitionMaterializerV1.ResolveSpatialScope(
                    binding,
                    tile => scopes[tile % scopes.Length])),
            static payload => payload.ToStandardPayload(),
            resolver);

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D0Entity,
            static payload => payload.CanonicalDigest());
        var authority = new DomainPartitionSnapshotAuthorityV1<EnvironmentAtmospherePayloadV1>(
            partition,
            header,
            static payload => payload.CanonicalDigest());
        var provider = EnvironmentDomainSnapshotProviderV1.CreateAll()
            .Single(static value => value.SectionId == EnvironmentAtmospherePayloadV1.PartitionId);

        var section = provider.Create(authority, resolver);
        Require(section.LogicalItemCount == 2,
            "QA-04 Environment D0 snapshot must contain both canonical descriptor records.");
        var verifier = provider.CreateSemanticVerifier(header);
        Require(verifier.VerifyWithContext is not null,
            "Environment D0 production verifier must expose reference-aware recovery.");
        var restored = verifier.VerifyWithContext!(
            section.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(resolver));
        Require(restored.LogicalItemCount == 2 &&
                restored.LogicalContentDigest.SequenceEqual(header.CanonicalDigest),
            "QA-04 Environment D0 recovery must reproduce the authoritative partition digest.");

        var decoded = section.Fragments
            .Select(fragment => DomainPartitionSnapshotWireCodecV1.DecodeFragment(
                EnvironmentAtmospherePayloadV1.PartitionId,
                fragment.FragmentPayload,
                StandardDomainNestedSnapshotCodecRegistryV1.Default))
            .SelectMany(static fragment => fragment.Records)
            .OrderBy(static record => record.RecordId)
            .ToArray();
        var authoritative = partition.RecordsCanonical.ToArray();
        Require(decoded.Length == authoritative.Length,
            "QA-04 Environment D0 decoded record count drifted.");
        for (var index = 0; index < decoded.Length; index++)
        {
            var restoredPayload = EnvironmentAtmospherePayloadV1.FromStandardPayload(decoded[index].Payload);
            Require(decoded[index].RecordId == authoritative[index].RecordId &&
                    decoded[index].RecordSchema == authoritative[index].RecordSchema &&
                    restoredPayload.CanonicalDigest().SequenceEqual(authoritative[index].Payload.CanonicalDigest()),
                "QA-04 Environment D0 snapshot record identity/schema/payload drifted through recovery.");
        }

        ExpectInvalid(() =>
        {
            var missing = new Resolver(Array.Empty<PartitionRecordRefV1>());
            _ = verifier.VerifyWithContext!(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(missing));
        });

        var expectedIds = new[]
        {
            Qa04EnvironmentReferenceDecompositionV1.BindD0(slice.D0StartOrdinal).Descriptor.RecordId,
            Qa04EnvironmentReferenceDecompositionV1.BindD0(slice.D0StartOrdinal + 1).Descriptor.RecordId,
        }.OrderBy(static id => id).ToArray();
        Require(authoritative.Select(static record => record.RecordId).SequenceEqual(expectedIds),
            "QA-04 Environment D0 snapshot proof must use canonical reference descriptors.");
    }

    private static EnvironmentAtmospherePayloadV1 CreateAtmosphere(
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
            Qa04EnvironmentGenesisContractV1.AtmosphereGasPpb);
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
        throw new InvalidOperationException("Expected Environment D0 recovery reference rejection.");
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
