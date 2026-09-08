using Grpc.Core;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Protocol;

/// <summary>
/// Loopback Alpha/CI seam used by INT-02 to exercise multiple real Gateway processes against one Core.
/// This is not a release authentication mechanism and is only installed when MACHIVERSE_ALPHA_MULTI_GATEWAY=1.
/// </summary>
public sealed class AlphaLocalMetadataGatewayIdentityResolverV1 : IAuthenticatedGatewayIdentityResolverV1
{
    public const string HeaderName = "x-machiverse-gateway-logical-id";

    public OpaqueId128 ResolveAuthenticatedGatewayLogicalId(ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var entry = context.RequestHeaders.FirstOrDefault(static entry =>
            string.Equals(entry.Key, HeaderName, StringComparison.Ordinal));
        if (entry is null || string.IsNullOrWhiteSpace(entry.Value))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "auth.unauthenticated"));

        try
        {
            var logicalId = OpaqueId128.Parse(entry.Value);
            if (logicalId.IsZero)
                throw new FormatException("ZERO Gateway identity is forbidden.");
            return logicalId;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "auth.unauthenticated"));
        }
    }
}
