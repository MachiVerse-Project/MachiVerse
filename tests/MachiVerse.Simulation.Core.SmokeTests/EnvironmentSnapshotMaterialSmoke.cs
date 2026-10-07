using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class EnvironmentSnapshotMaterialSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        VerifyTypedOwnerRoots();
        VerifyAtmosphereRoundTrip();
    }

    private static void VerifyTypedOwnerRoots()
    {
        var frozen = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1).WorldState;
        var state = EnvironmentDomainStateV1.CreateEmpty();
        var material = state.BindSnapshotMaterial(frozen);
        var providers = EnvironmentDomainSnapshotProviderV1.CreateAll();

        Require(material.Authorities.Count == 13 && material.Authorities.All(static value => value.ActualItemCount == 0),
            "Environment runtime state must own all 13 typed partition roots.");
        Require(providers.Count == 13 &&
                providers.Select(static value => value.SectionId).SequenceEqual(
                    material.Authorities.Select(static value => value.PartitionId.Value)),
            "Environment provider set must match all 13 owner partitions in canonical order.");

        foreach (var authority in material.Authorities)
        {
            var provider = providers.Single(value => value.SectionId == authority.PartitionId.Value);
            var section = provider.Create(authority);
            Require(section.LogicalItemCount == 0 && section.Fragments.Count == 1,
                $"Typed empty Environment root must emit one empty fragment: {authority.PartitionId.Value}.");
            var restored = provider.CreateSemanticVerifier(authority.Header).Verify(section.Fragments);
            Require(restored.LogicalItemCount == 0 && restored.LogicalContentDigest.SequenceEqual(authority.Header.CanonicalDigest),
                $"Typed empty Environment recovery must recompute canonical digest: {authority.PartitionId.Value}.");
        }
    }

    private static void VerifyAtmosphereRoundTrip()
    {
        var scope = new PartitionRecordRefV1(
            "spatial.scope_registry",
            MachiVerse.Simulation.Core.Determinism.OpaqueId128.Parse("0000000000000000000000000006a001"));
        IReadOnlyList<KeyValuePair<string, uint>> gasPpb = Array.AsReadOnly(new[]
        {
            new KeyValuePair<string, uint>("co2", 420_000u),
            new KeyValuePair<string, uint>("n2", 780_000_000u),
            new KeyValuePair<string, uint>("o2", 209_000_000u),
        });
        var payload = new EnvironmentAtmospherePayloadV1(
            scope,
            PressurePa: 101_325,
            TemperatureMilliKelvin: 293_150,
            HumidityPpm: 500_000,
            new global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1(1_000, -2_000, 3_000),
            VaporMassGram: 20_000,
            LiquidMassGram: 100,
            gasPpb);

        var identity = StandardDomainPartitionRegistry.Get(EnvironmentAtmospherePayloadV1.PartitionId);
        var record = new DomainRecordEnvelopeV1<EnvironmentAtmospherePayloadV1>(
            MachiVerse.Simulation.Core.Determinism.OpaqueId128.Parse("0000000000000000000000000006a101"),
            identity.RecordSchema,
            revision: 1,
            createdStep: 5,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);
        var partition = new DomainPartitionStateV1<EnvironmentAtmospherePayloadV1>(identity, [record]);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 5,
            detailLevel: DetailLevelV1.D0Entity,
            static value => value.CanonicalDigest());
        var authority = new DomainPartitionSnapshotAuthorityV1<EnvironmentAtmospherePayloadV1>(
            partition,
            header,
            static value => value.CanonicalDigest());
        var provider = EnvironmentDomainSnapshotProviderV1.CreateAll()
            .Single(static value => value.SectionId == EnvironmentAtmospherePayloadV1.PartitionId);
        var resolver = new Resolver([scope]);
        var section = provider.Create(authority, resolver);
        var verifier = provider.CreateSemanticVerifier(header);
        Require(verifier.VerifyWithContext is not null,
            "Environment atmosphere verifier must accept recovered reference context.");
        var restored = verifier.VerifyWithContext!(
            section.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(resolver));
        Require(restored.LogicalItemCount == 1 && restored.LogicalContentDigest.SequenceEqual(header.CanonicalDigest),
            "Environment atmosphere recovery must reconstruct canonical partition identity.");

        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
            EnvironmentAtmospherePayloadV1.PartitionId,
            section.Fragments.Single().FragmentPayload,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var restoredPayload = EnvironmentAtmospherePayloadV1.FromStandardPayload(decoded.Records.Single().Payload);
        Require(restoredPayload.PressurePa == payload.PressurePa &&
                restoredPayload.TemperatureMilliKelvin == payload.TemperatureMilliKelvin &&
                restoredPayload.WindUmPerSecond == payload.WindUmPerSecond &&
                restoredPayload.GasPpb.SequenceEqual(payload.GasPpb) &&
                restoredPayload.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()),
            "Environment atmosphere P4-05 payload must round-trip losslessly.");

        ExpectInvalid(
            "environment atmosphere production must reject missing spatial scope target",
            () => _ = provider.Create(authority, new Resolver(Array.Empty<PartitionRecordRefV1>())));
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
