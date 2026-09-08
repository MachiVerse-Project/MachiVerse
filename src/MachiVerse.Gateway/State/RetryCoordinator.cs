using System.Security.Cryptography;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.State;

public enum RetryConvergenceAction
{
    None = 0,
    QueryAuthoritativeStatus = 1,
    RetrySameIdentity = 2,
}

public static class RetryCoordinator
{
    public static RetryConvergenceAction PlanAfterFailover(GatewayCustodyState state)
        => state == GatewayCustodyState.Terminal
            ? RetryConvergenceAction.None
            : RetryConvergenceAction.QueryAuthoritativeStatus;

    public static RetryConvergenceAction PlanAfterAuthoritativeUnknown(GatewayCustodyState state)
        => (int)state < (int)GatewayCustodyState.CoreAccepted
            ? RetryConvergenceAction.RetrySameIdentity
            : RetryConvergenceAction.QueryAuthoritativeStatus;

    public static OperationStatusQueryV1 CreateStatusQuery(PersistedCustodyOperation record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new OperationStatusQueryV1
        {
            OperationId = Google.Protobuf.ByteString.CopyFrom(record.OperationId),
        };
    }

    public static StandardOperationV1 CreateRetryOperation(PersistedCustodyOperation record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateIdentity(record, record.Operation);
        return record.Operation.Clone();
    }

    public static void ValidateIdentity(PersistedCustodyOperation record, StandardOperationV1 operation)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(operation);
        if (!operation.OperationId.Span.SequenceEqual(record.OperationId))
            throw new InvalidDataException("protocol.operation-identity-changed");
        if (!CryptographicOperations.FixedTimeEquals(operation.ImmutablePayloadDigest.Span, record.ImmutablePayloadDigest))
            throw new InvalidDataException("protocol.operation-payload-mismatch");
    }
}
