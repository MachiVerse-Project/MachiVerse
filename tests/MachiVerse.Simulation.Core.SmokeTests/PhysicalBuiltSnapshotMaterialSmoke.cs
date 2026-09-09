using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class PhysicalBuiltSnapshotMaterialSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        var subject = Ref("resident.identity_lifecycle", "0000000000000000000000000004a001");
        var frame = Ref("spatial.world_frame", "0000000000000000000000000004a002");
        var shape = Ref("built.structure", "0000000000000000000000000004a003");
        var containment = Ref("physical.container_location", "0000000000000000000000000004a004");
        var payload = new PhysicalPresencePayloadV1(
            subject,
            frame,
            new Vec3Int64V1(1_000, -2_000, 3_000),
            new QuaternionQ30V1(0, 0, 0, 1 << 30),
            new Vec3Int64V1(10, 20, 30),
            new Vec3Int64V1(-1, 2, -3),
            shape,
            containment,
            new StableToken("dynamic"));

        var identity = StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId);
        var record = new DomainRecordEnvelopeV1<PhysicalPresencePayloadV1>(
            OpaqueId128.Parse("0000000000000000000000000004a101"),
            identity.RecordSchema,
            revision: 1,
            createdStep: 5,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);
        var partition = new DomainPartitionStateV1<PhysicalPresencePayloadV1>(identity, [record]);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 5,
            detailLevel: DetailLevelV1.D0Entity,
            static value => value.CanonicalDigest());
        var authority = new DomainPartitionSnapshotAuthorityV1<PhysicalPresencePayloadV1>(
            partition,
            header,
            static value => value.CanonicalDigest());
        var provider = PhysicalBuiltDomainSnapshotProviderV1.CreatePresence();

        // Standalone structure serialization remains possible before the all-97 resolver is composed.
        var section = provider.Create(authority);
        var source = new DomainSnapshotRecoveredReferenceSourceV1(
            PhysicalPresencePayloadV1.PartitionId,
            section.Fragments);
        Require(source.ActualItemCount == 1 && source.RecordIdsCanonical.Single() == record.RecordId,
            "Physical presence recovered pre-pass must preserve actual record identity.");

        var resolver = new Resolver([subject, frame, shape, containment]);
        var production = provider.Create(authority, resolver);
        var verifier = provider.CreateSemanticVerifier(header);
        Require(verifier.VerifyWithContext is not null,
            "Physical presence semantic verifier must accept recovered reference context.");
        var restored = verifier.VerifyWithContext!(
            production.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(resolver));
        Require(restored.LogicalItemCount == 1 && restored.LogicalContentDigest.SequenceEqual(header.CanonicalDigest),
            "Physical presence recovery must reconstruct canonical partition identity.");

        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
            PhysicalPresencePayloadV1.PartitionId,
            production.Fragments.Single().FragmentPayload,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var restoredPayload = PhysicalPresencePayloadV1.FromStandardPayload(decoded.Records.Single().Payload);
        Require(restoredPayload == payload && restoredPayload.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()),
            "Physical presence P4-05 payload must round-trip losslessly.");

        ExpectInvalid(
            "physical presence production must reject missing shape target",
            () => _ = provider.Create(authority, new Resolver([subject, frame, containment])));
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
