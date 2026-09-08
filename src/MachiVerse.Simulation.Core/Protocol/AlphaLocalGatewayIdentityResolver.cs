using Grpc.Core;
using MachiVerse.Protocol.V1;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Protocol;

public interface IRegisterScopedGatewayIdentityResolverV1
{
    OpaqueId128 ResolveRegisteredGatewayLogicalId(ServerCallContext context, GatewayRegisterV1 register);
}

/// <summary>
/// Loopback Alpha/CI seam used by INT-02 to exercise multiple real Gateway processes against one Core.
/// This is not a release authentication mechanism and is only installed when MACHIVERSE_ALPHA_MULTI_GATEWAY=1.
/// The claimed Gateway identity is accepted only at gateway.register and is then bound to that gRPC session.
/// </summary>
public sealed class AlphaLocalRegisterGatewayIdentityResolverV1
    : IAuthenticatedGatewayIdentityResolverV1, IRegisterScopedGatewayIdentityResolverV1
{
    private static readonly OpaqueId128 PreRegistrationIdentity =
        OpaqueId128.Parse("ffffffffffffffffffffffffffffffff");

    public OpaqueId128 ResolveAuthenticatedGatewayLogicalId(ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return PreRegistrationIdentity;
    }

    public OpaqueId128 ResolveRegisteredGatewayLogicalId(ServerCallContext context, GatewayRegisterV1 register)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(register);
        return OpaqueId128.FromBytes(
            CoreGatewayWireValidatorV1.ValidateId128(register.GatewayLogicalId, "gateway_logical_id", allowZero: false));
    }
}
