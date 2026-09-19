using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Exact retry/query resolver for Operations already covered by the canonical QA-04 closed prefix.
/// The generator is the authority; no probabilistic membership structure or deleted terminal row is
/// consulted. A full scan is deliberately allowed on this cold retry/query boundary so the hot
/// production Step path remains compact.
/// </summary>
public static class Qa04ClosedPrefixOperationResolverV1
{
    public static Qa04OperationDescriptorV1? Resolve(
        Qa04OperationClosedPrefixV1 prefix,
        ulong stateStep,
        OpaqueId128 operationId)
        => ResolveCore(prefix, stateStep, operationId, expectedPayloadDigest: default, validatePayload: false);

    public static Qa04OperationDescriptorV1? ResolveRetry(
        Qa04OperationClosedPrefixV1 prefix,
        ulong stateStep,
        OpaqueId128 operationId,
        ReadOnlySpan<byte> expectedPayloadDigest)
    {
        if (expectedPayloadDigest.Length != 32)
            throw new ArgumentException("Expected Operation payload digest must be 32 bytes.", nameof(expectedPayloadDigest));
        return ResolveCore(prefix, stateStep, operationId, expectedPayloadDigest, validatePayload: true);
    }

    private static Qa04OperationDescriptorV1? ResolveCore(
        Qa04OperationClosedPrefixV1 prefix,
        ulong stateStep,
        OpaqueId128 operationId,
        ReadOnlySpan<byte> expectedPayloadDigest,
        bool validatePayload)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (operationId.IsZero)
            throw new ArgumentException("OperationId ZERO is invalid.", nameof(operationId));

        prefix.Validate(stateStep);
        if (prefix.LastClosedInjectionStep is not { } lastClosed)
            return null;

        Qa04OperationDescriptorV1? match = null;
        for (ulong injectionStep = prefix.FirstInjectionStep; injectionStep <= lastClosed; injectionStep++)
        {
            foreach (var descriptor in Qa04ReferenceLoadV1.OperationsForStep(injectionStep))
            {
                if (descriptor.OperationId != operationId) continue;

                if (match is not null)
                {
                    if (!CryptographicOperations.FixedTimeEquals(match.PayloadDigest, descriptor.PayloadDigest))
                        throw new InvalidDataException("qa04.operation-prefix.identity-payload-collision");
                    throw new InvalidDataException("qa04.operation-prefix.identity-collision");
                }

                match = descriptor;
            }

            if (injectionStep == ulong.MaxValue)
                throw new OverflowException("QA-04 closed-prefix scan cannot advance beyond uint64 max.");
        }

        if (match is not null &&
            validatePayload &&
            !CryptographicOperations.FixedTimeEquals(match.PayloadDigest, expectedPayloadDigest))
            throw new InvalidDataException("protocol.operation-payload-mismatch");

        return match;
    }
}
