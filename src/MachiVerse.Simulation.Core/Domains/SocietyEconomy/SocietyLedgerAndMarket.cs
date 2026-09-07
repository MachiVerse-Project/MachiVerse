using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.SocietyEconomy;

public enum LedgerEntryKindV1 : byte
{
    Debit = 1,
    Credit = 2,
}

public sealed record LedgerEntryV1(
    OpaqueId128 EntryId,
    OpaqueId128 AccountId,
    StableToken CurrencyToken,
    LedgerEntryKindV1 EntryKind,
    long AmountMicrounit)
{
    public void Validate()
    {
        if (EntryId.IsZero || AccountId.IsZero)
            throw new InvalidDataException("society.ledger-entry-id-zero");
        if (!Enum.IsDefined(EntryKind))
            throw new InvalidDataException("society.ledger-entry-kind-invalid");
        if (AmountMicrounit <= 0)
            throw new InvalidDataException("society.ledger-entry-amount-invalid");
    }
}

public sealed record BalancedLedgerTransactionV1(
    OpaqueId128 TransactionId,
    IReadOnlyList<LedgerEntryV1> Entries)
{
    public IReadOnlyList<LedgerEntryV1> ValidateAndCanonicalize()
    {
        if (TransactionId.IsZero)
            throw new InvalidDataException("society.ledger-transaction-id-zero");
        ArgumentNullException.ThrowIfNull(Entries);
        if (Entries.Count == 0)
            throw new InvalidDataException("society.ledger-entry-empty");

        foreach (var entry in Entries) entry.Validate();
        if (Entries.Select(static entry => entry.EntryId).Distinct().Count() != Entries.Count)
            throw new InvalidDataException("society.ledger-entry-id-duplicate");

        foreach (var currencyGroup in Entries.GroupBy(static entry => entry.CurrencyToken.Value, StringComparer.Ordinal))
        {
            Int128 debits = 0;
            Int128 credits = 0;
            foreach (var entry in currencyGroup)
            {
                if (entry.EntryKind == LedgerEntryKindV1.Debit) debits += entry.AmountMicrounit;
                else credits += entry.AmountMicrounit;
            }
            if (debits != credits)
                throw new InvalidDataException("society.ledger-unbalanced");
        }

        return Array.AsReadOnly(Entries
            .OrderBy(static entry => entry.AccountId)
            .ThenBy(static entry => entry.EntryKind)
            .ThenBy(static entry => entry.EntryId)
            .ToArray());
    }
}

public sealed record FinanceAccountBalanceV1(
    OpaqueId128 AccountId,
    StableToken CurrencyToken,
    long BalanceMicrounit,
    long CreditLimitMicrounit)
{
    public void Validate()
    {
        if (AccountId.IsZero)
            throw new InvalidDataException("society.account-id-zero");
        if (CreditLimitMicrounit < 0)
            throw new InvalidDataException("society.account-credit-limit-negative");
        if ((Int128)BalanceMicrounit < -(Int128)CreditLimitMicrounit)
            throw new InvalidDataException("society.account-below-credit-limit");
    }
}

public enum PaymentApplyStatusV1 : byte
{
    Applied = 1,
    InsufficientFunds = 2,
}

public sealed record PaymentApplyResultV1(
    PaymentApplyStatusV1 Status,
    FinanceAccountBalanceV1 Source,
    FinanceAccountBalanceV1 Destination);

public static class DeterministicPaymentV1
{
    public static PaymentApplyResultV1 Apply(
        FinanceAccountBalanceV1 source,
        FinanceAccountBalanceV1 destination,
        long amountMicrounit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        source.Validate();
        destination.Validate();
        if (source.AccountId == destination.AccountId)
            throw new InvalidDataException("society.payment-same-account");
        if (source.CurrencyToken != destination.CurrencyToken)
            throw new InvalidDataException("society.payment-currency-mismatch");
        if (amountMicrounit <= 0)
            throw new InvalidDataException("society.payment-amount-invalid");

        var sourceNext = (Int128)source.BalanceMicrounit - amountMicrounit;
        if (sourceNext < -(Int128)source.CreditLimitMicrounit)
            return new PaymentApplyResultV1(PaymentApplyStatusV1.InsufficientFunds, source, destination);

        var destinationNext = (Int128)destination.BalanceMicrounit + amountMicrounit;
        if (sourceNext < long.MinValue || sourceNext > long.MaxValue ||
            destinationNext < long.MinValue || destinationNext > long.MaxValue)
            throw new OverflowException("simulation.numeric-overflow");

        var updatedSource = source with { BalanceMicrounit = (long)sourceNext };
        var updatedDestination = destination with { BalanceMicrounit = (long)destinationNext };
        updatedSource.Validate();
        updatedDestination.Validate();
        return new PaymentApplyResultV1(PaymentApplyStatusV1.Applied, updatedSource, updatedDestination);
    }
}

public enum MarketOrderSideV1 : byte
{
    Buy = 1,
    Sell = 2,
}

public sealed record MarketOrderV1(
    OpaqueId128 OrderId,
    OpaqueId128 OwnerRef,
    MarketOrderSideV1 Side,
    long LimitPriceMicrounit,
    long Quantity,
    ulong EligibleStep)
{
    public void Validate()
    {
        if (OrderId.IsZero || OwnerRef.IsZero)
            throw new InvalidDataException("society.market-order-id-zero");
        if (!Enum.IsDefined(Side))
            throw new InvalidDataException("society.market-order-side-invalid");
        if (LimitPriceMicrounit < 0)
            throw new InvalidDataException("society.market-order-price-negative");
        if (Quantity <= 0)
            throw new InvalidDataException("society.market-order-quantity-invalid");
    }
}

public sealed record MarketTradeV1(
    OpaqueId128 BuyOrderId,
    OpaqueId128 SellOrderId,
    OpaqueId128 BuyerRef,
    OpaqueId128 SellerRef,
    long Quantity,
    long ClearingPriceMicrounit);

public enum MarketClearingStatusV1 : byte
{
    Cleared = 1,
    NoCross = 2,
}

public sealed record MarketClearingResultV1(
    MarketClearingStatusV1 Status,
    long? ClearingPriceMicrounit,
    long ExecutedQuantity,
    IReadOnlyList<MarketTradeV1> Trades);

public static class DeterministicCallAuctionV1
{
    public static MarketClearingResultV1 Clear(IEnumerable<MarketOrderV1> orders, ulong clearingStep)
    {
        ArgumentNullException.ThrowIfNull(orders);
        var materialized = orders.ToArray();
        foreach (var order in materialized) order.Validate();
        if (materialized.Select(static order => order.OrderId).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("society.market-order-id-duplicate");

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
            return NoCross();

        PriceCandidate? best = null;
        foreach (var price in eligible.Select(static order => order.LimitPriceMicrounit).Distinct().Order())
        {
            var buyQuantity = SumQuantity(buys.Where(order => order.LimitPriceMicrounit >= price));
            var sellQuantity = SumQuantity(sells.Where(order => order.LimitPriceMicrounit <= price));
            var executable = Int128.Min(buyQuantity, sellQuantity);
            var imbalance = Int128.Abs(buyQuantity - sellQuantity);
            var candidate = new PriceCandidate(price, executable, imbalance);
            if (best is null || IsBetter(candidate, best.Value))
                best = candidate;
        }

        if (best is null || best.Value.ExecutableQuantity == 0)
            return NoCross();
        if (best.Value.ExecutableQuantity > long.MaxValue)
            throw new OverflowException("simulation.numeric-overflow");

        var clearingPrice = best.Value.PriceMicrounit;
        var quantityToAllocate = (long)best.Value.ExecutableQuantity;
        var eligibleBuys = buys.Where(order => order.LimitPriceMicrounit >= clearingPrice).ToArray();
        var eligibleSells = sells.Where(order => order.LimitPriceMicrounit <= clearingPrice).ToArray();
        var trades = Allocate(eligibleBuys, eligibleSells, quantityToAllocate, clearingPrice);
        long executed = 0;
        foreach (var trade in trades) executed = checked(executed + trade.Quantity);
        if (executed != quantityToAllocate)
            throw new InvalidOperationException("society.market-allocation-incomplete");

        return new MarketClearingResultV1(
            MarketClearingStatusV1.Cleared,
            clearingPrice,
            executed,
            Array.AsReadOnly(trades.ToArray()));
    }

    private static Int128 SumQuantity(IEnumerable<MarketOrderV1> orders)
    {
        Int128 total = 0;
        foreach (var order in orders) total += order.Quantity;
        return total;
    }

    private static bool IsBetter(PriceCandidate candidate, PriceCandidate existing)
    {
        if (candidate.ExecutableQuantity != existing.ExecutableQuantity)
            return candidate.ExecutableQuantity > existing.ExecutableQuantity;
        if (candidate.AbsoluteImbalance != existing.AbsoluteImbalance)
            return candidate.AbsoluteImbalance < existing.AbsoluteImbalance;
        return candidate.PriceMicrounit < existing.PriceMicrounit;
    }

    private static List<MarketTradeV1> Allocate(
        IReadOnlyList<MarketOrderV1> buys,
        IReadOnlyList<MarketOrderV1> sells,
        long totalQuantity,
        long clearingPrice)
    {
        var trades = new List<MarketTradeV1>();
        var buyIndex = 0;
        var sellIndex = 0;
        var buyRemaining = buys.Count == 0 ? 0 : buys[0].Quantity;
        var sellRemaining = sells.Count == 0 ? 0 : sells[0].Quantity;
        var remaining = totalQuantity;

        while (remaining > 0 && buyIndex < buys.Count && sellIndex < sells.Count)
        {
            var quantity = Math.Min(remaining, Math.Min(buyRemaining, sellRemaining));
            if (quantity <= 0)
                throw new InvalidOperationException("society.market-allocation-stalled");
            var buy = buys[buyIndex];
            var sell = sells[sellIndex];
            trades.Add(new MarketTradeV1(
                buy.OrderId,
                sell.OrderId,
                buy.OwnerRef,
                sell.OwnerRef,
                quantity,
                clearingPrice));
            remaining -= quantity;
            buyRemaining -= quantity;
            sellRemaining -= quantity;

            if (buyRemaining == 0)
            {
                buyIndex++;
                if (buyIndex < buys.Count) buyRemaining = buys[buyIndex].Quantity;
            }
            if (sellRemaining == 0)
            {
                sellIndex++;
                if (sellIndex < sells.Count) sellRemaining = sells[sellIndex].Quantity;
            }
        }

        if (remaining != 0)
            throw new InvalidOperationException("society.market-allocation-incomplete");
        return trades;
    }

    private static MarketClearingResultV1 NoCross()
        => new(MarketClearingStatusV1.NoCross, null, 0, Array.Empty<MarketTradeV1>());

    private readonly record struct PriceCandidate(
        long PriceMicrounit,
        Int128 ExecutableQuantity,
        Int128 AbsoluteImbalance);
}
