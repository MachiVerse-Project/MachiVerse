using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Canonical SQLite state_wire adapter for persistent CrossDomainTransaction authority.
/// It deliberately reuses the exact core.operation-state /2.0 transaction wire instead of
/// defining a second transaction serialization contract. The wrapper basis_step is the state's
/// UpdatedStep and the fragment must contain exactly one transaction and no Operation items.
/// </summary>
public static class CrossDomainTransactionPersistentWireV1
{
    public static byte[] Encode(CrossDomainTransactionStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return CoreOperationStateSnapshotWireCodecV2.Encode(
            state.UpdatedStep,
            Array.Empty<DurableOperationStateV1>(),
            [state]);
    }

    public static CrossDomainTransactionStateV1 Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.IsEmpty)
            throw new InvalidDataException("persistence.cross-domain-transaction-wire-empty");
        var fragment = CoreOperationStateSnapshotWireCodecV2.Decode(encoded);
        if (fragment.Operations.Count != 0 || fragment.Transactions.Count != 1)
            throw new InvalidDataException("persistence.cross-domain-transaction-wire-shape");
        var state = fragment.Transactions[0];
        if (fragment.BasisStep != state.UpdatedStep)
            throw new InvalidDataException("persistence.cross-domain-transaction-wire-step-mismatch");
        var canonical = Encode(state);
        if (!canonical.AsSpan().SequenceEqual(encoded))
            throw new InvalidDataException("persistence.cross-domain-transaction-wire-noncanonical");
        return state;
    }

    public static CrossDomainTransactionStateV1 DecodeAndValidate(
        DurableCrossDomainTransactionStateV1 row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var state = Decode(row.StateWire);
        if (state.TransactionId != row.TransactionId ||
            state.Lifecycle != row.Lifecycle ||
            state.CreatedStep != row.CreatedStep ||
            state.UpdatedStep != row.UpdatedStep ||
            state.TerminalStep != row.TerminalStep)
            throw new InvalidDataException("persistence.cross-domain-transaction-wire-row-mismatch");
        if (!state.CanonicalDigest().AsSpan().SequenceEqual(row.StateDigest))
            throw new InvalidDataException("persistence.cross-domain-transaction-wire-digest-mismatch");
        return state;
    }
}
