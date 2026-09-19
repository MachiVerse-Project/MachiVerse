using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class InfrastructureInformationSnapshotMaterialSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        VerifyTypedOwnerRoots();
        VerifyDeliveryRoundTrip();
    }

    private static void VerifyTypedOwnerRoots()
    {
        var frozen = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1).WorldState;
        var state = InfrastructureInformationDomainStateV1.CreateEmpty();
        var material = state.BindSnapshotMaterial(frozen);
        var providers = InfrastructureInformationDomainSnapshotProviderV1.CreateAll();

        Require(material.Authorities.Count == 14 && material.Authorities.All(static value => value.ActualItemCount == 0),
            "Infrastructure/Information runtime state must own all 14 typed partition roots.");
        Require(providers.Count == 14 &&
                providers.Select(static value => value.SectionId).SequenceEqual(
                    material.Authorities.Select(static value => value.PartitionId.Value)),
            "Infrastructure/Information providers must match all 14 owner partitions in canonical order.");

        foreach (var authority in material.Authorities)
        {
            var provider = providers.Single(value => value.SectionId == authority.PartitionId.Value);
            var section = provider.Create(authority);
            Require(section.LogicalItemCount == 0 && section.Fragments.Count == 1,
                $"Typed empty Infrastructure/Information root must emit one empty fragment: {authority.PartitionId.Value}.");
            var restored = provider.CreateSemanticVerifier(authority.Header).Verify(section.Fragments);
            Require(restored.LogicalItemCount == 0 && restored.LogicalContentDigest.SequenceEqual(authority.Header.CanonicalDigest),
                $"Typed empty Infrastructure/Information recovery must recompute canonical digest: {authority.PartitionId.Value}.");
        }
    }

    private static void VerifyDeliveryRoundTrip()
    {
        var content = Ref("information.record_store", "0000000000000000000000000008a001");
        var sender = Ref("resident.identity_lifecycle", "0000000000000000000000000008a002");
        var recipientA = Ref("resident.identity_lifecycle", "0000000000000000000000000008a003");
        var recipientB = Ref("resident.identity_lifecycle", "0000000000000000000000000008a004");
        var channel = Ref("infrastructure.communication_service", "0000000000000000000000000008a005");
        var digest = Enumerable.Range(0, 32).Select(static value => (byte)(0x40 + value)).ToArray();
        var payload = new InformationDeliveryPayloadV1(
            content,
            sender,
            Array.AsReadOnly(new[] { recipientA, recipientB }),
            channel,
            EligibleStep: 50,
            DeliveredStep: 51,
            Priority: -7,
            new StableToken("delivered"),
            digest);

        var identity = StandardDomainPartitionRegistry.Get(InformationDeliveryPayloadV1.PartitionId);
        var record = new DomainRecordEnvelopeV1<InformationDeliveryPayloadV1>(
            OpaqueId128.Parse("0000000000000000000000000008a101"),
            identity.RecordSchema,
            revision: 1,
            createdStep: 50,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);
        var partition = new DomainPartitionStateV1<InformationDeliveryPayloadV1>(identity, [record]);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 51,
            detailLevel: DetailLevelV1.D0Entity,
            static value => value.CanonicalDigest());
        var authority = new DomainPartitionSnapshotAuthorityV1<InformationDeliveryPayloadV1>(
            partition,
            header,
            static value => value.CanonicalDigest());
        var provider = InfrastructureInformationDomainSnapshotProviderV1.CreateAll()
            .Single(static value => value.SectionId == InformationDeliveryPayloadV1.PartitionId);
        var resolver = new Resolver([content, sender, recipientA, recipientB, channel]);
        var section = provider.Create(authority, resolver);
        var verifier = provider.CreateSemanticVerifier(header);
        Require(verifier.VerifyWithContext is not null,
            "Information delivery verifier must accept recovered reference context.");
        var restored = verifier.VerifyWithContext!(
            section.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(resolver));
        Require(restored.LogicalItemCount == 1 && restored.LogicalContentDigest.SequenceEqual(header.CanonicalDigest),
            "Information delivery recovery must reconstruct canonical partition identity.");

        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
            InformationDeliveryPayloadV1.PartitionId,
            section.Fragments.Single().FragmentPayload,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var restoredPayload = InformationDeliveryPayloadV1.FromStandardPayload(decoded.Records.Single().Payload);
        Require(restoredPayload.ContentRef == payload.ContentRef &&
                restoredPayload.RecipientRefs.SequenceEqual(payload.RecipientRefs) &&
                restoredPayload.DeliveredStep == payload.DeliveredStep &&
                restoredPayload.Priority == payload.Priority &&
                restoredPayload.ContentDigest.SequenceEqual(payload.ContentDigest) &&
                restoredPayload.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()),
            "Information delivery P4-05 payload must round-trip losslessly.");

        ExpectInvalid(
            "information delivery production must reject missing channel target",
            () => _ = provider.Create(authority, new Resolver([content, sender, recipientA, recipientB])));
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
