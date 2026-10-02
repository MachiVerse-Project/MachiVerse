using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class SocietyEconomySnapshotMaterialSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        VerifyTypedOwnerRoots();
        VerifyMarketTransactionRoundTrip();
    }

    private static void VerifyTypedOwnerRoots()
    {
        var frozen = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1).WorldState;
        var state = SocietyEconomyDomainStateV1.CreateEmpty();
        var material = state.BindSnapshotMaterial(frozen);
        var providers = SocietyEconomyDomainSnapshotProviderV1.CreateAll();

        Require(material.Authorities.Count == 16 && material.Authorities.All(static value => value.ActualItemCount == 0),
            "Society/Economy runtime state must own all 16 typed partition roots.");
        Require(providers.Count == 16 &&
                providers.Select(static value => value.SectionId).SequenceEqual(
                    material.Authorities.Select(static value => value.PartitionId.Value)),
            "Society/Economy provider set must match all 16 owner partitions in canonical order.");

        foreach (var authority in material.Authorities)
        {
            var provider = providers.Single(value => value.SectionId == authority.PartitionId.Value);
            var section = provider.Create(authority);
            Require(section.LogicalItemCount == 0 && section.Fragments.Count == 1,
                $"Typed empty Society/Economy root must emit one empty fragment: {authority.PartitionId.Value}.");
            var restored = provider.CreateSemanticVerifier(authority.Header).Verify(section.Fragments);
            Require(restored.LogicalItemCount == 0 && restored.LogicalContentDigest.SequenceEqual(authority.Header.CanonicalDigest),
                $"Typed empty Society/Economy recovery must recompute canonical digest: {authority.PartitionId.Value}.");
        }
    }

    private static void VerifyMarketTransactionRoundTrip()
    {
        var market = Ref("society.organization", "0000000000000000000000000007a001");
        var buyer = Ref("resident.identity_lifecycle", "0000000000000000000000000007a002");
        var seller = Ref("resident.identity_lifecycle", "0000000000000000000000000007a003");
        var payload = new SocietyMarketTransactionPayloadV1(
            market,
            new StableToken("grain"),
            new StableToken("buy"),
            LimitPrice: 12_500,
            Quantity: 40,
            ClearingPrice: 12_000,
            buyer,
            seller,
            EligibleStep: 25,
            new StableToken("cleared"));

        var identity = StandardDomainPartitionRegistry.Get(SocietyMarketTransactionPayloadV1.PartitionId);
        var record = new DomainRecordEnvelopeV1<SocietyMarketTransactionPayloadV1>(
            OpaqueId128.Parse("0000000000000000000000000007a101"),
            identity.RecordSchema,
            revision: 1,
            createdStep: 25,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);
        var partition = new DomainPartitionStateV1<SocietyMarketTransactionPayloadV1>(identity, [record]);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 25,
            detailLevel: DetailLevelV1.D0Entity,
            static value => value.CanonicalDigest());
        var authority = new DomainPartitionSnapshotAuthorityV1<SocietyMarketTransactionPayloadV1>(
            partition,
            header,
            static value => value.CanonicalDigest());
        var provider = SocietyEconomyDomainSnapshotProviderV1.CreateAll()
            .Single(static value => value.SectionId == SocietyMarketTransactionPayloadV1.PartitionId);
        var resolver = new Resolver([market, buyer, seller]);
        var section = provider.Create(authority, resolver);
        var verifier = provider.CreateSemanticVerifier(header);
        Require(verifier.VerifyWithContext is not null,
            "Society market verifier must accept recovered reference context.");
        var restored = verifier.VerifyWithContext!(
            section.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(resolver));
        Require(restored.LogicalItemCount == 1 && restored.LogicalContentDigest.SequenceEqual(header.CanonicalDigest),
            "Society market recovery must reconstruct canonical partition identity.");

        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
            SocietyMarketTransactionPayloadV1.PartitionId,
            section.Fragments.Single().FragmentPayload,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var restoredPayload = SocietyMarketTransactionPayloadV1.FromStandardPayload(decoded.Records.Single().Payload);
        Require(restoredPayload.MarketRef == payload.MarketRef &&
                restoredPayload.OrderSide == payload.OrderSide &&
                restoredPayload.LimitPrice == payload.LimitPrice &&
                restoredPayload.ClearingPrice == payload.ClearingPrice &&
                restoredPayload.BuyerRef == payload.BuyerRef &&
                restoredPayload.SellerRef == payload.SellerRef &&
                restoredPayload.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()),
            "Society market P4-05 payload must round-trip losslessly.");

        ExpectInvalid(
            "society market production must reject missing seller target",
            () => _ = provider.Create(authority, new Resolver([market, buyer])));
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
