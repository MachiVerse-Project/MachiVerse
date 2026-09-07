using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;

internal static class Sim10MarketLedgerSmoke
{
    internal static void Run()
    {
        VerifyCallAuction();
        VerifyTiePrice();
        VerifyLedger();
        VerifyPayment();
        VerifyProduction();
        VerifyProperty();
    }

    private static void VerifyCallAuction()
    {
        var buyLowId = Id("00000000000000000000000000011001");
        var buyHighId = Id("00000000000000000000000000011002");
        var sellLowId = Id("00000000000000000000000000011003");
        var sellHighId = Id("00000000000000000000000000011004");
        var ownerA = Id("00000000000000000000000000011101");
        var ownerB = Id("00000000000000000000000000011102");
        var ownerC = Id("00000000000000000000000000011103");
        var ownerD = Id("00000000000000000000000000011104");
        var orders = new[]
        {
            new MarketOrderV1(buyHighId, ownerB, MarketOrderSideV1.Buy, 120, 5, 4),
            new MarketOrderV1(sellHighId, ownerD, MarketOrderSideV1.Sell, 110, 4, 4),
            new MarketOrderV1(buyLowId, ownerA, MarketOrderSideV1.Buy, 120, 5, 3),
            new MarketOrderV1(sellLowId, ownerC, MarketOrderSideV1.Sell, 100, 6, 3),
        };

        var forward = DeterministicCallAuctionV1.Clear(orders, 4);
        var reverse = DeterministicCallAuctionV1.Clear(orders.Reverse(), 4);
        Require(forward.Status == MarketClearingStatusV1.Cleared &&
                forward.ClearingPriceMicrounit == 110 &&
                forward.ExecutedQuantity == 10,
            "domain.market.clearing: maximum executable quantity clearing mismatch.");
        Require(forward.ClearingPriceMicrounit == reverse.ClearingPriceMicrounit &&
                forward.ExecutedQuantity == reverse.ExecutedQuantity &&
                forward.Trades.SequenceEqual(reverse.Trades),
            "domain.market.arrival-independence: input order changed deterministic call-auction result.");
        Require(forward.Trades[0].BuyOrderId == buyLowId &&
                forward.Trades[0].SellOrderId == sellLowId &&
                forward.Trades[0].BuyerRef == ownerA &&
                forward.Trades[0].SellerRef == ownerC,
            "domain.market.clearing: eligible-step/order-id canonical allocation mismatch.");
    }

    private static void VerifyTiePrice()
    {
        var buyer = Id("00000000000000000000000000011501");
        var seller = Id("00000000000000000000000000011502");
        var orders = new[]
        {
            new MarketOrderV1(
                Id("00000000000000000000000000011510"), buyer,
                MarketOrderSideV1.Buy, 100, 5, 1),
            new MarketOrderV1(
                Id("00000000000000000000000000011511"), seller,
                MarketOrderSideV1.Sell, 90, 5, 1),
        };

        var result = DeterministicCallAuctionV1.Clear(orders, 1);
        Require(result.Status == MarketClearingStatusV1.Cleared &&
                result.ExecutedQuantity == 5 &&
                result.ClearingPriceMicrounit == 90,
            "domain.market.tie-price: equal executable quantity/imbalance must choose lowest candidate price.");
    }

    private static void VerifyLedger()
    {
        var transaction = Id("00000000000000000000000000011200");
        var currency = new StableToken("currency.test");
        var accountA = Id("00000000000000000000000000011201");
        var accountB = Id("00000000000000000000000000011202");
        var debit = new LedgerEntryV1(
            Id("00000000000000000000000000011210"), accountB, currency,
            LedgerEntryKindV1.Debit, 1_000);
        var credit = new LedgerEntryV1(
            Id("00000000000000000000000000011211"), accountA, currency,
            LedgerEntryKindV1.Credit, 1_000);

        var forward = new BalancedLedgerTransactionV1(transaction, [debit, credit]).ValidateAndCanonicalize();
        var reverse = new BalancedLedgerTransactionV1(transaction, [credit, debit]).ValidateAndCanonicalize();
        Require(forward.SequenceEqual(reverse),
            "domain.ledger.double-entry: input order changed canonical ledger posting order.");
        RequireReject(
            () => _ = new BalancedLedgerTransactionV1(transaction, [debit]).ValidateAndCanonicalize(),
            "society.ledger-unbalanced",
            "domain.ledger.unbalanced");
    }

    private static void VerifyPayment()
    {
        var currency = new StableToken("currency.test");
        var source = new FinanceAccountBalanceV1(
            Id("00000000000000000000000000011301"), currency, 500, 0);
        var destination = new FinanceAccountBalanceV1(
            Id("00000000000000000000000000011302"), currency, 100, 0);

        var insufficient = DeterministicPaymentV1.Apply(source, destination, 501);
        Require(insufficient.Status == PaymentApplyStatusV1.InsufficientFunds &&
                insufficient.Source == source && insufficient.Destination == destination,
            "domain.payment.insufficient: rejected payment must not mutate account balances.");
        var settled = DeterministicPaymentV1.Apply(source, destination, 300);
        Require(settled.Status == PaymentApplyStatusV1.Applied &&
                settled.Source.BalanceMicrounit == 200 &&
                settled.Destination.BalanceMicrounit == 400,
            "domain.ledger.double-entry: deterministic payment settlement mismatch.");
    }

    private static void VerifyProduction()
    {
        var ore = new StableToken("material.ore");
        var ingot = new StableToken("material.ingot");
        var recipe = new ProductionRecipeV1(
            new StableToken("recipe.smelting"),
            [new ProductionMaterialV1(ore, 2)],
            [new ProductionMaterialV1(ingot, 1)]);
        var stock = new Dictionary<StableToken, long> { [ore] = 6, [ingot] = 1 };
        var next = DeterministicProductionV1.Apply(stock, recipe, 2);
        Require(next[ore] == 2 && next[ingot] == 3 && stock[ore] == 6 && stock[ingot] == 1,
            "domain.production.conservation: recipe application must consume/produce exact integer quantities without mutating basis stock.");
        RequireReject(
            () => _ = DeterministicProductionV1.Apply(stock, recipe, 4),
            "society.production.insufficient-input",
            "domain.production.conservation");
    }

    private static void VerifyProperty()
    {
        var right = new SocietyPropertyRightV1(
            Id("00000000000000000000000000011401"),
            Id("00000000000000000000000000011402"),
            Id("00000000000000000000000000011403"),
            1);
        var transferred = right.TransferTo(Id("00000000000000000000000000011404"));
        Require(transferred.SubjectId == right.SubjectId &&
                transferred.RightId == right.RightId &&
                transferred.Quantity == right.Quantity &&
                transferred.HolderId != right.HolderId,
            "domain.property.physical-separation: ownership transfer changes only economic holder semantics and does not replace the physical subject identity.");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void RequireReject(Action action, string expected, string acceptance)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expected)
        {
            return;
        }
        throw new InvalidOperationException($"{acceptance}: expected SIM-10 rejection {expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
