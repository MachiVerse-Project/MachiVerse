using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.SocietyEconomy;

public enum MarketOrderSideV1 : byte
{
    Buy = 1,
    Sell = 2,
}

public sealed record MarketOrderV1(
    OpaqueId128 OrderId,
    OpaqueId128 OwnerId,
    MarketOrderSideV1 Side,
    long LimitPriceMicrounit,
    long Quantity,
    ulong EligibleStep)
{
    public void Validate()
    {
        if (OrderId.IsZero || OwnerId.IsZero)
            throw new InvalidDataException("society.market.order-id-zero");
        if (!Enum.IsDefined(Side))
            throw new InvalidDataException("society.market.side-invalid");
        if (LimitPriceMicrounit < 0)
            throw new InvalidDataException("society.market.price-negative");
        if (Quantity <= 0)
            throw new InvalidDataException("society.market.quantity-nonpositive");
    }
}

public sealed record MarketTradeV1(
    OpaqueId128 BuyOrderId,
    OpaqueId128 SellOrderId,
    long ClearingPriceMicrounit,
    long Quantity);

public sealed record MarketClearingResultV1(
    long? ClearingPriceMicrounit,
    long ExecutableQuantity,
    IReadOnlyList<MarketTradeV1> Trades)
{
    public bool HasTrade => ExecutableQuantity != 0;
}

public static class DeterministicCallAuctionV1
{
    public static MarketClearingResultV1 Clear(IEnumerable<MarketOrderV1> orders, ulong clearingStep)
    {
        ArgumentNullException.ThrowIfNull(orders);
        var materialized = orders.ToArray();
        foreach (var order in materialized) order.Validate();
        if (materialized.Select(static order => order.OrderId).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("society.market.order-id-duplicate");

        var eligible = materialized.Where(order => order.EligibleStep <= clearingStep).ToArray();
        var buys = eligible
            .Where(static order => order.Side == MarketOrderSideV1.Buy)
            .OrderByDescending(static order => order.LimitPriceMicrounit)
            .ThenBy(static order => order.EligibleStep)
            .ThenBy(static order => order.OrderId)
            .ToArray();
        var sells = eligible
            .Where(static order => order.Side == MarketOrderSideV1.Sell)
            .OrderBy(static order => order.LimitPriceMicrounit)
            .ThenBy(static order => order.EligibleStep)
            .ThenBy(static order => order.OrderId)
            .ToArray();

        if (buys.Length == 0 || sells.Length == 0)
            return Empty();

        long? clearingPrice = null;
        long bestExecutable = 0;
        Int128 bestImbalance = Int128.MaxValue;
        foreach (var price in eligible.Select(static order => order.LimitPriceMicrounit).Distinct().Order())
        {
            var buyQuantity = CheckedQuantitySum(buys.Where(order => order.LimitPriceMicrounit >= price));
            var sellQuantity = CheckedQuantitySum(sells.Where(order => order.LimitPriceMicrounit <= price));
            var executable = Math.Min(buyQuantity, sellQuantity);
            var imbalance = Int128.Abs((Int128)buyQuantity - sellQuantity);

            if (executable > bestExecutable ||
                (executable == bestExecutable && executable > 0 && imbalance < bestImbalance) ||
                (executable == bestExecutable && executable > 0 && imbalance == bestImbalance &&
                 (clearingPrice is null || price < clearingPrice.Value)))
            {
                bestExecutable = executable;
                bestImbalance = imbalance;
                clearingPrice = price;
            }
        }

        if (bestExecutable == 0 || clearingPrice is null)
            return Empty();

        var activeBuys = buys.Where(order => order.LimitPriceMicrounit >= clearingPrice.Value).ToArray();
        var activeSells = sells.Where(order => order.LimitPriceMicrounit <= clearingPrice.Value).ToArray();
        var trades = Allocate(activeBuys, activeSells, clearingPrice.Value, bestExecutable);
        return new MarketClearingResultV1(
            clearingPrice,
            bestExecutable,
            Array.AsReadOnly(trades));
    }

    private static MarketClearingResultV1 Empty()
        => new(null, 0, Array.Empty<MarketTradeV1>());

    private static long CheckedQuantitySum(IEnumerable<MarketOrderV1> orders)
    {
        Int128 total = 0;
        foreach (var order in orders)
        {
            total += order.Quantity;
            if (total > long.MaxValue)
                throw new OverflowException("simulation.numeric-overflow");
        }
        return (long)total;
    }

    private static MarketTradeV1[] Allocate(
        IReadOnlyList<MarketOrderV1> buys,
        IReadOnlyList<MarketOrderV1> sells,
        long price,
        long executableQuantity)
    {
        var trades = new List<MarketTradeV1>();
        var buyIndex = 0;
        var sellIndex = 0;
        var buyRemaining = buys[0].Quantity;
        var sellRemaining = sells[0].Quantity;
        long allocated = 0;

        while (allocated < executableQuantity)
        {
            var remainingTotal = executableQuantity - allocated;
            var quantity = Math.Min(remainingTotal, Math.Min(buyRemaining, sellRemaining));
            if (quantity <= 0)
                throw new InvalidDataException("society.market.allocation-invalid");

            trades.Add(new MarketTradeV1(
                buys[buyIndex].OrderId,
                sells[sellIndex].OrderId,
                price,
                quantity));
            allocated = checked(allocated + quantity);
            buyRemaining -= quantity;
            sellRemaining -= quantity;

            if (buyRemaining == 0 && allocated < executableQuantity)
            {
                buyIndex++;
                if (buyIndex >= buys.Count)
                    throw new InvalidDataException("society.market.buy-allocation-exhausted");
                buyRemaining = buys[buyIndex].Quantity;
            }
            if (sellRemaining == 0 && allocated < executableQuantity)
            {
                sellIndex++;
                if (sellIndex >= sells.Count)
                    throw new InvalidDataException("society.market.sell-allocation-exhausted");
                sellRemaining = sells[sellIndex].Quantity;
            }
        }

        return trades.ToArray();
    }
}

public enum LedgerEntryKindV1 : byte
{
    Debit = 1,
    Credit = 2,
}

public sealed record LedgerPostingV1(
    OpaqueId128 EntryId,
    OpaqueId128 AccountId,
    StableToken Currency,
    LedgerEntryKindV1 Kind,
    long AmountMicrounit)
{
    public void Validate()
    {
        if (EntryId.IsZero || AccountId.IsZero)
            throw new InvalidDataException("society.ledger.id-zero");
        if (!Enum.IsDefined(Kind))
            throw new InvalidDataException("society.ledger.entry-kind-invalid");
        if (AmountMicrounit <= 0)
            throw new InvalidDataException("society.ledger.amount-nonpositive");
    }
}

public sealed class BalancedLedgerTransactionV1
{
    public BalancedLedgerTransactionV1(OpaqueId128 TransactionId, IEnumerable<LedgerPostingV1> postings)
    {
        if (TransactionId.IsZero)
            throw new InvalidDataException("society.ledger.transaction-id-zero");
        ArgumentNullException.ThrowIfNull(postings);

        var canonical = postings
            .Select(posting =>
            {
                ArgumentNullException.ThrowIfNull(posting);
                posting.Validate();
                return posting;
            })
            .OrderBy(static posting => posting.AccountId)
            .ThenBy(static posting => posting.Kind)
            .ThenBy(static posting => posting.EntryId)
            .ToArray();
        if (canonical.Length == 0)
            throw new InvalidDataException("society.ledger.empty");
        if (canonical.Select(static posting => posting.EntryId).Distinct().Count() != canonical.Length)
            throw new InvalidDataException("society.ledger.entry-id-duplicate");

        foreach (var currencyGroup in canonical.GroupBy(static posting => posting.Currency))
        {
            Int128 debits = 0;
            Int128 credits = 0;
            foreach (var posting in currencyGroup)
            {
                if (posting.Kind == LedgerEntryKindV1.Debit) debits += posting.AmountMicrounit;
                else credits += posting.AmountMicrounit;
            }
            if (debits != credits)
                throw new InvalidDataException("society.ledger.unbalanced");
        }

        TransactionId = TransactionId;
        Postings = Array.AsReadOnly(canonical);
    }

    public OpaqueId128 TransactionId { get; }
    public IReadOnlyList<LedgerPostingV1> Postings { get; }
}

public sealed record FinanceAccountBalanceV1(
    OpaqueId128 AccountId,
    StableToken Currency,
    long BalanceMicrounit)
{
    public void Validate()
    {
        if (AccountId.IsZero) throw new InvalidDataException("society.account-id-zero");
        if (BalanceMicrounit < 0) throw new InvalidDataException("society.account-balance-negative");
    }
}

public enum PaymentSettlementStatusV1 : byte
{
    Settled = 1,
    InsufficientFunds = 2,
}

public sealed record PaymentSettlementResultV1(
    PaymentSettlementStatusV1 Status,
    FinanceAccountBalanceV1 Source,
    FinanceAccountBalanceV1 Destination);

public static class DeterministicPaymentV1
{
    public static PaymentSettlementResultV1 Settle(
        FinanceAccountBalanceV1 source,
        FinanceAccountBalanceV1 destination,
        long amountMicrounit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        source.Validate();
        destination.Validate();
        if (source.AccountId == destination.AccountId)
            throw new InvalidDataException("society.payment.same-account");
        if (source.Currency != destination.Currency)
            throw new InvalidDataException("society.payment.currency-mismatch");
        if (amountMicrounit <= 0)
            throw new InvalidDataException("society.payment.amount-nonpositive");

        if (source.BalanceMicrounit < amountMicrounit)
            return new PaymentSettlementResultV1(PaymentSettlementStatusV1.InsufficientFunds, source, destination);

        try
        {
            var nextSource = source with { BalanceMicrounit = checked(source.BalanceMicrounit - amountMicrounit) };
            var nextDestination = destination with { BalanceMicrounit = checked(destination.BalanceMicrounit + amountMicrounit) };
            nextSource.Validate();
            nextDestination.Validate();
            return new PaymentSettlementResultV1(PaymentSettlementStatusV1.Settled, nextSource, nextDestination);
        }
        catch (OverflowException ex)
        {
            throw new OverflowException("simulation.numeric-overflow", ex);
        }
    }
}

public sealed record ProductionMaterialV1(StableToken Material, long Quantity)
{
    public void Validate()
    {
        if (Quantity <= 0) throw new InvalidDataException("society.production.quantity-nonpositive");
    }
}

public sealed class ProductionRecipeV1
{
    public ProductionRecipeV1(
        StableToken RecipeId,
        IEnumerable<ProductionMaterialV1> inputs,
        IEnumerable<ProductionMaterialV1> outputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);
        Inputs = Canonicalize(inputs, "society.production.input-duplicate");
        Outputs = Canonicalize(outputs, "society.production.output-duplicate");
        if (Inputs.Count == 0 || Outputs.Count == 0)
            throw new InvalidDataException("society.production.recipe-empty-side");
        this.RecipeId = RecipeId;
    }

    public StableToken RecipeId { get; }
    public IReadOnlyList<ProductionMaterialV1> Inputs { get; }
    public IReadOnlyList<ProductionMaterialV1> Outputs { get; }

    private static IReadOnlyList<ProductionMaterialV1> Canonicalize(
        IEnumerable<ProductionMaterialV1> materials,
        string duplicateCode)
    {
        var canonical = materials
            .Select(material =>
            {
                ArgumentNullException.ThrowIfNull(material);
                material.Validate();
                return material;
            })
            .OrderBy(static material => material.Material.Value, StringComparer.Ordinal)
            .ToArray();
        if (canonical.Select(static material => material.Material.Value).Distinct(StringComparer.Ordinal).Count() != canonical.Length)
            throw new InvalidDataException(duplicateCode);
        return Array.AsReadOnly(canonical);
    }
}

public static class DeterministicProductionV1
{
    public static IReadOnlyDictionary<StableToken, long> Apply(
        IReadOnlyDictionary<StableToken, long> stock,
        ProductionRecipeV1 recipe,
        long batches)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(recipe);
        if (batches <= 0) throw new InvalidDataException("society.production.batch-nonpositive");

        var next = new SortedDictionary<StableToken, long>(stock);
        foreach (var pair in next)
        {
            if (pair.Value < 0) throw new InvalidDataException("society.production.stock-negative");
        }

        try
        {
            foreach (var input in recipe.Inputs)
            {
                var required = checked(input.Quantity * batches);
                var available = next.GetValueOrDefault(input.Material, 0);
                if (available < required)
                    throw new InvalidDataException("society.production.insufficient-input");
                next[input.Material] = checked(available - required);
            }
            foreach (var output in recipe.Outputs)
            {
                var produced = checked(output.Quantity * batches);
                next[output.Material] = checked(next.GetValueOrDefault(output.Material, 0) + produced);
            }
        }
        catch (OverflowException ex)
        {
            throw new OverflowException("simulation.numeric-overflow", ex);
        }

        return next;
    }
}

public sealed record SocietyPropertyRightV1(
    OpaqueId128 RightId,
    OpaqueId128 SubjectId,
    OpaqueId128 HolderId,
    long Quantity)
{
    public void Validate()
    {
        if (RightId.IsZero || SubjectId.IsZero || HolderId.IsZero)
            throw new InvalidDataException("society.property-id-zero");
        if (Quantity <= 0) throw new InvalidDataException("society.property-quantity-nonpositive");
    }

    public SocietyPropertyRightV1 TransferTo(OpaqueId128 newHolderId)
    {
        Validate();
        if (newHolderId.IsZero) throw new InvalidDataException("society.property-holder-zero");
        return this with { HolderId = newHolderId };
    }
}
