using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04MarketOrderApplicationSmoke
{
    public static void Run()
    {
        var descriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value == "society-market-payment-contract");
        var binding = Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1);
        var scopeOrdinal = checked((uint)(descriptor.FamilyOrdinal % (ulong)Qa04ReferenceScenariosV1.MarketScopeCount));
        var market = Qa04MarketMaterializerV1.CreateMarketState(
            scopeOrdinal,
            Qa04SpatialTileScopeAuthorityV1.ScopeRef,
            out _);
        var nonTarget = Qa04MarketMaterializerV1.CreateOrder(0, out _);
        var current = new SocietyMarketTransactionPartitionStateV2(new[] { market, nonTarget });

        var resident = Qa04ReferenceLoadV1.Record(
            new StableToken("resident.persistent-identity"),
            descriptor.FamilyOrdinal);
        var ownerRef = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            resident.RecordId);
        var references = Resolver.ForOwner(ownerRef);

        var result = Qa04MarketOrderApplicationV1.Apply(binding, current, references);
        var effectiveStep = checked(descriptor.InjectionStep + 1UL);
        var expectedId = DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            new StableToken("society_economy"),
            descriptor.OperationId,
            new StableToken("perf.market-order-operation"),
            0);
        var expectedSide = new StableToken((descriptor.FamilyOrdinal & 1UL) == 0UL ? "buy" : "sell");
        var expectedPrice = expectedSide.Value == "buy"
            ? checked(100_000L + (long)(descriptor.FamilyOrdinal % 1_000UL))
            : checked(99_500L + (long)(descriptor.FamilyOrdinal % 1_000UL));
        var expectedQuantity = checked(1L + (long)(descriptor.FamilyOrdinal % 20UL));
        var marketPayload = (SocietyMarketStatePayloadV2)market.Payload;
        var createdPayload = RequireOrderPayload(result.CreatedOrder);

        Require(result.MarketState.State.ItemCount == current.State.ItemCount + 1UL,
            "market order must add exactly one v2 record");
        Require(result.CreatedOrder.RecordId == expectedId,
            "market order created RecordId drift");
        Require(result.CreatedOrder.RecordSchema == SocietyMarketTransactionRecordSchemaV2.RecordSchema,
            "market order record schema drift");
        Require(result.CreatedOrder.Revision == 1 && result.CreatedOrder.CreatedStep == effectiveStep,
            "market order envelope revision/created_step drift");
        Require(result.CreatedOrder.RetiredStep is null &&
                result.CreatedOrder.DetailLevel == DetailLevelV1.D2RegionalAggregate &&
                result.CreatedOrder.LineageRef is null,
            "market order envelope lifecycle/detail drift");
        Require(createdPayload.MarketRef == binding.PrimaryTarget,
            "market order market_ref drift");
        Require(createdPayload.OwnerRef == ownerRef,
            "market order owner_ref drift");
        Require(createdPayload.InstrumentToken == marketPayload.InstrumentToken,
            "market order instrument must come from target market state");
        Require(createdPayload.Side == expectedSide &&
                createdPayload.LimitPriceMicrounit == expectedPrice &&
                createdPayload.Quantity == expectedQuantity &&
                createdPayload.RemainingQuantity == expectedQuantity,
            "market order bound side/price/quantity drift");
        Require(createdPayload.EligibleStep == effectiveStep && createdPayload.Status == new StableToken("open"),
            "market order eligible/status drift");
        Require(result.MarketState.RecordSet.TryGet(market.RecordId, out var afterMarket) &&
                ReferenceEquals(afterMarket, market),
            "market order must preserve target market state");
        Require(result.MarketState.RecordSet.TryGet(nonTarget.RecordId, out var afterNonTarget) &&
                ReferenceEquals(afterNonTarget, nonTarget),
            "market order must preserve non-target records");

        var replay = Qa04MarketOrderApplicationV1.Apply(binding, current, references);
        Require(replay.CreatedOrder.RecordId == result.CreatedOrder.RecordId,
            "market order replay from same pre-state must derive same RecordId");
        Require(EquivalentOrder(
                RequireOrderPayload(replay.CreatedOrder),
                RequireOrderPayload(result.CreatedOrder)),
            "market order replay from same pre-state must derive same payload");

        ExpectInvalid(
            () => Qa04MarketOrderApplicationV1.Apply(binding, result.MarketState, references),
            "qa04.market.order-record-id-collision");

        var otherDescriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value == "physical-item-movement-work");
        var otherBinding = Qa04CanonicalOperationBindingV1.Bind(otherDescriptor, schedulingPolicyGeneration: 1);
        ExpectInvalid(
            () => Qa04MarketOrderApplicationV1.Apply(otherBinding, current, references),
            "qa04.market.order-family");

        var tamperedOperation = binding.Operation.Clone();
        var tamperedPayload = tamperedOperation.OperationPayload.ToByteArray();
        tamperedPayload[^1] ^= 0x01;
        tamperedOperation.OperationPayload = ByteString.CopyFrom(tamperedPayload);
        var tamperedBinding = binding with { Operation = tamperedOperation };
        ExpectInvalid(
            () => Qa04MarketOrderApplicationV1.Apply(tamperedBinding, current, references),
            "qa04.market.order-binding-drift");

        var missingTarget = new SocietyMarketTransactionPartitionStateV2(new[] { nonTarget });
        ExpectInvalid(
            () => Qa04MarketOrderApplicationV1.Apply(binding, missingTarget, references),
            "qa04.market.order-target-missing");

        var haltedPayload = new SocietyMarketStatePayloadV2(
            marketPayload.ScopeRef,
            marketPayload.InstrumentToken,
            marketPayload.CurrencyToken,
            new StableToken("halted"),
            marketPayload.ClearingCadenceSteps,
            marketPayload.LastClearingStep,
            marketPayload.LastClearingPriceMicrounit);
        var haltedMarket = new SocietyMarketTransactionRecordMaterialV2(
            market.RecordId,
            market.Revision,
            market.CreatedStep,
            market.RetiredStep,
            market.DetailLevel,
            market.LineageRef,
            haltedPayload);
        var driftedTarget = new SocietyMarketTransactionPartitionStateV2(new[] { haltedMarket, nonTarget });
        ExpectInvalid(
            () => Qa04MarketOrderApplicationV1.Apply(binding, driftedTarget, references),
            "qa04.market.order-target-drift");

        ExpectInvalid(
            () => Qa04MarketOrderApplicationV1.Apply(binding, current, new Resolver()),
            "qa04.market.order-owner-ref");

        var wrongOwnerSchema = Resolver.ForOwner(ownerRef);
        wrongOwnerSchema.Set(
            ownerRef,
            StandardDomainPartitionRegistry.Get("participation.control_mode").RecordSchema);
        ExpectInvalid(
            () => Qa04MarketOrderApplicationV1.Apply(binding, current, wrongOwnerSchema),
            "qa04.market.order-owner-ref");
    }

    private static SocietyMarketOrderPayloadV2 RequireOrderPayload(SocietyMarketTransactionRecordMaterialV2 record)
        => record.Payload as SocietyMarketOrderPayloadV2
           ?? throw new InvalidOperationException("market application did not create an order payload");

    private static bool EquivalentOrder(SocietyMarketOrderPayloadV2 left, SocietyMarketOrderPayloadV2 right)
        => left.MarketRef == right.MarketRef &&
           left.OwnerRef == right.OwnerRef &&
           left.InstrumentToken == right.InstrumentToken &&
           left.Side == right.Side &&
           left.LimitPriceMicrounit == right.LimitPriceMicrounit &&
           left.Quantity == right.Quantity &&
           left.RemainingQuantity == right.RemainingQuantity &&
           left.EligibleStep == right.EligibleStep &&
           left.Status == right.Status;

    private static void ExpectInvalid(Action action, string expectedMessage)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected market order application to reject invalid input.");
        }
        catch (InvalidDataException ex)
        {
            if (ex.Message != expectedMessage)
                throw new InvalidOperationException($"Unexpected rejection code: {ex.Message}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Resolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public static Resolver ForOwner(PartitionRecordRefV1 ownerRef)
        {
            var resolver = new Resolver();
            resolver.Set(
                ownerRef,
                StandardDomainPartitionRegistry.Get(ownerRef.PartitionId.Value).RecordSchema);
            return resolver;
        }

        public void Set(PartitionRecordRefV1 reference, SchemaRefV1 schema)
            => _records[reference] = schema;

        public bool Exists(PartitionRecordRefV1 reference)
            => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
