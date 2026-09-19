using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Semantic authority for core.operation-state /2.0. The section remains one of the six
/// canonical Core Snapshot sections and combines durable Operation custody with persistent
/// CrossDomainTransaction custody without hashing protobuf bytes.
/// </summary>
public sealed class CoreOperationStateSnapshotAuthorityV2
{
    public static readonly SchemaRefV1 Schema = new("core.operation-state", 2, 0);

    private CoreOperationStateSnapshotAuthorityV2(
        IReadOnlyList<DurableOperationStateV1> operations,
        IReadOnlyList<CrossDomainTransactionStateV1> transactions,
        byte[] operationDigest,
        byte[] transactionDigest,
        byte[] canonicalDigest)
    {
        Operations = operations;
        Transactions = transactions;
        OperationDigest = operationDigest;
        TransactionDigest = transactionDigest;
        CanonicalDigest = canonicalDigest;
    }

    public IReadOnlyList<DurableOperationStateV1> Operations { get; }
    public IReadOnlyList<CrossDomainTransactionStateV1> Transactions { get; }
    public byte[] OperationDigest { get; }
    public byte[] TransactionDigest { get; }
    public byte[] CanonicalDigest { get; }
    public ulong LogicalItemCount => checked((ulong)Operations.Count + (ulong)Transactions.Count);

    public static CoreOperationStateSnapshotAuthorityV2 Create(
        IEnumerable<DurableOperationStateV1> operations,
        IEnumerable<CrossDomainTransactionStateV1> transactions,
        ulong basisStep)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(transactions);

        var orderedOperations = operations
            .Select(static value => value ?? throw new ArgumentNullException(nameof(operations)))
            .OrderBy(static value => value.OperationId)
            .ToArray();
        if (orderedOperations.Select(static value => value.OperationId).Distinct().Count() != orderedOperations.Length)
            throw new InvalidDataException("snapshot-core.operation-v2.duplicate-operation-id");
        var operationAuthority = DurableOperationSubstateV1.Canonicalize(orderedOperations);

        var orderedTransactions = transactions
            .Select(static value => value ?? throw new ArgumentNullException(nameof(transactions)))
            .OrderBy(static value => value.TransactionId)
            .ToArray();
        if (orderedTransactions.Select(static value => value.TransactionId).Distinct().Count() != orderedTransactions.Length)
            throw new InvalidDataException("snapshot-core.operation-v2.duplicate-transaction-id");
        foreach (var state in orderedTransactions)
        {
            if (state.CreatedStep > state.UpdatedStep || state.UpdatedStep > basisStep)
                throw new InvalidDataException("snapshot-core.operation-v2.transaction-step-invalid");
            if (state.RootCausality.BasisStep is { } rootBasis && rootBasis > basisStep)
                throw new InvalidDataException("snapshot-core.operation-v2.transaction-root-causality-future");
            foreach (var reference in state.InvariantResults.SelectMany(static result => result.ParticipantRefs))
            {
                if (reference.BasisStep is { } referenceBasis && referenceBasis > basisStep)
                    throw new InvalidDataException("snapshot-core.operation-v2.transaction-causality-future");
            }
        }

        var transactionDigest = HashSuite.DomainHash("mv.cross-domain-transaction-state-set.v1", writer =>
        {
            writer.WriteArrayStart((ulong)orderedTransactions.Length);
            foreach (var state in orderedTransactions) writer.WriteBytes(state.CanonicalDigest());
        });
        var canonicalDigest = HashSuite.DomainHash("mv.core-operation-state.v2", writer =>
        {
            writer.WriteArrayStart(2);
            writer.WriteBytes(operationAuthority.CanonicalDigest);
            writer.WriteBytes(transactionDigest);
        });

        return new CoreOperationStateSnapshotAuthorityV2(
            Array.AsReadOnly(orderedOperations),
            Array.AsReadOnly(orderedTransactions),
            operationAuthority.CanonicalDigest.ToArray(),
            transactionDigest,
            canonicalDigest);
    }
}
