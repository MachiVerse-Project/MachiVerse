using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed partial class SqlitePersistenceStore
{
    /// <summary>
    /// Resolves a retry/query against the authoritative compact QA-04 closed prefix. Terminal
    /// generated rows may already have been removed from operation_state; the canonical generator
    /// and durable prefix remain the source of truth.
    /// </summary>
    public async Task<Qa04OperationDescriptorV1?> ResolveQa04ClosedPrefixOperationAsync(
        OpaqueId128 operationId,
        CancellationToken cancellationToken = default)
    {
        var prefix = await ReadQa04OperationClosedPrefixAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("persistence.qa04-operation-prefix-missing");
        return Qa04ClosedPrefixOperationResolverV1.Resolve(
            prefix,
            ClosedPrefixStateStep(prefix),
            operationId);
    }

    public async Task<Qa04OperationDescriptorV1?> ResolveQa04ClosedPrefixRetryAsync(
        OpaqueId128 operationId,
        byte[] expectedPayloadDigest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedPayloadDigest);
        var prefix = await ReadQa04OperationClosedPrefixAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("persistence.qa04-operation-prefix-missing");
        return Qa04ClosedPrefixOperationResolverV1.ResolveRetry(
            prefix,
            ClosedPrefixStateStep(prefix),
            operationId,
            expectedPayloadDigest);
    }

    private static ulong ClosedPrefixStateStep(Qa04OperationClosedPrefixV1 prefix)
        => prefix.LastClosedInjectionStep is { } last
            ? checked(last + 2UL)
            : 1UL;
}
