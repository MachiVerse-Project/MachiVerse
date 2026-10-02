using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class SpatialSnapshotMaterialSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        VerifyTypedOwnerRoots();
        VerifyWorldFrameRoundTrip();
    }

    private static void VerifyTypedOwnerRoots()
    {
        var frozen = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1).WorldState;
        var state = SpatialDomainStateV1.CreateEmpty();
        var material = state.BindSnapshotMaterial(frozen);
        var providers = SpatialDomainSnapshotProviderV1.CreateAll();

        Require(material.Authorities.Count == 8 && material.Authorities.All(static value => value.ActualItemCount == 0),
            "Spatial runtime state must own all eight typed partition roots.");
        Require(providers.Count == 8 &&
                providers.Select(static value => value.SectionId).SequenceEqual(
                    material.Authorities.Select(static value => value.PartitionId.Value)),
            "Spatial provider set must match all eight owner partitions in canonical order.");

        foreach (var authority in material.Authorities)
        {
            var provider = providers.Single(value => value.SectionId == authority.PartitionId.Value);
            var section = provider.Create(authority);
            Require(section.LogicalItemCount == 0 && section.Fragments.Count == 1,
                $"Typed empty Spatial root must emit one empty fragment: {authority.PartitionId.Value}.");
            Require(section.Fragments[0].ItemCount == 0 &&
                    section.Fragments[0].FirstRecordId is null &&
                    section.Fragments[0].LastRecordId is null,
                $"Typed empty Spatial root must not fabricate record ranges: {authority.PartitionId.Value}.");
            var restored = provider.CreateSemanticVerifier(authority.Header).Verify(section.Fragments);
            Require(restored.LogicalItemCount == 0 && restored.LogicalContentDigest.SequenceEqual(authority.Header.CanonicalDigest),
                $"Typed empty Spatial recovery must recompute canonical digest: {authority.PartitionId.Value}.");
        }
    }

    private static void VerifyWorldFrameRoundTrip()
    {
        var validScope = Ref("spatial.scope_registry", "0000000000000000000000000005a001");
        var payload = new SpatialWorldFramePayloadV1(
            new StableToken("world"),
            ParentFrame: null,
            new global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1(1_000, -2_000, 3_000),
            new global::MachiVerse.Simulation.Core.WorldState.QuaternionQ30V1(0, 0, 0, 1 << 30),
            validScope,
            TransformRevision: 7);

        var identity = StandardDomainPartitionRegistry.Get(SpatialWorldFramePayloadV1.PartitionId);
        var record = new DomainRecordEnvelopeV1<SpatialWorldFramePayloadV1>(
            OpaqueId128.Parse("0000000000000000000000000005a101"),
            identity.RecordSchema,
            revision: 1,
            createdStep: 5,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);
        var partition = new DomainPartitionStateV1<SpatialWorldFramePayloadV1>(identity, [record]);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 5,
            detailLevel: DetailLevelV1.D0Entity,
            static value => value.CanonicalDigest());
        var authority = new DomainPartitionSnapshotAuthorityV1<SpatialWorldFramePayloadV1>(
            partition,
            header,
            static value => value.CanonicalDigest());
        var provider = SpatialDomainSnapshotProviderV1.CreateWorldFrame();

        var structureOnly = provider.Create(authority);
        var source = new DomainSnapshotRecoveredReferenceSourceV1(
            SpatialWorldFramePayloadV1.PartitionId,
            structureOnly.Fragments);
        Require(source.ActualItemCount == 1 && source.RecordIdsCanonical.Single() == record.RecordId,
            "Spatial world-frame recovered pre-pass must preserve actual record identity.");

        var resolver = new Resolver([validScope]);
        var production = provider.Create(authority, resolver);
        var verifier = provider.CreateSemanticVerifier(header);
        Require(verifier.VerifyWithContext is not null,
            "Spatial world-frame verifier must accept recovered reference context.");
        var restored = verifier.VerifyWithContext!(
            production.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(resolver));
        Require(restored.LogicalItemCount == 1 && restored.LogicalContentDigest.SequenceEqual(header.CanonicalDigest),
            "Spatial world-frame recovery must reconstruct canonical partition identity.");

        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
            SpatialWorldFramePayloadV1.PartitionId,
            production.Fragments.Single().FragmentPayload,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var restoredPayload = SpatialWorldFramePayloadV1.FromStandardPayload(decoded.Records.Single().Payload);
        Require(restoredPayload == payload && restoredPayload.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()),
            "Spatial world-frame P4-05 payload must round-trip losslessly.");

        ExpectInvalid(
            "spatial world-frame production must reject missing valid scope target",
            () => _ = provider.Create(authority, new Resolver(Array.Empty<PartitionRecordRefV1>())));
    }

    private static PartitionRecordRefV1 Ref(string partitionId, string recordId)
        => new(partitionId, OpaqueId128.Parse(recordId));

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
