using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.View.Participation;

public static class ViewPermissionTokens
{
    public const string WorldReadPublic = "view.world.read.public";
    public const string WorldReadParticipant = "view.world.read.participant";
    public const string WorldSubscribe = "view.world.subscribe";
    public const string OperationDiver = "view.operation.diver";
    public const string OperationModeration = "view.operation.moderation";
    public const string OperationAdministration = "view.operation.administration";
    public const string ParticipationBind = "view.participation.bind";
    public const string ParticipationPolicyWrite = "view.participation.policy.write";
    public const string SessionReadSelf = "view.session.read.self";
}

public enum ViewSessionAccessState
{
    Unknown,
    Active,
    ReauthenticationRequired,
    Revoked,
    Expired
}

public sealed record ViewSessionProjection(
    ViewSessionAccessState State,
    string? SessionId,
    string EffectiveRoleSet,
    ulong SessionGeneration,
    string? DiverRef,
    IReadOnlyList<string> EffectivePermissions,
    string? ReasonCode)
{
    public bool HasPermission(string permission)
        => State == ViewSessionAccessState.Active
            && EffectivePermissions.BinarySearchOrdinal(permission);
}

public sealed class ViewSessionProjectionStore
{
    private static readonly HashSet<string> StandardPermissions = new(StringComparer.Ordinal)
    {
        ViewPermissionTokens.WorldReadPublic,
        ViewPermissionTokens.WorldReadParticipant,
        ViewPermissionTokens.WorldSubscribe,
        ViewPermissionTokens.OperationDiver,
        ViewPermissionTokens.OperationModeration,
        ViewPermissionTokens.OperationAdministration,
        ViewPermissionTokens.ParticipationBind,
        ViewPermissionTokens.ParticipationPolicyWrite,
        ViewPermissionTokens.SessionReadSelf
    };

    public ViewSessionProjection Snapshot { get; private set; } = new(
        ViewSessionAccessState.Unknown,
        SessionId: null,
        EffectiveRoleSet: string.Empty,
        SessionGeneration: 0,
        DiverRef: null,
        EffectivePermissions: Array.Empty<string>(),
        ReasonCode: "auth.session-unknown");

    public event Action<ViewSessionProjection>? Changed;

    public bool TryApply(WireEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(envelope.MessageType, "auth.session.changed", StringComparison.Ordinal))
            return false;
        if (!string.Equals(envelope.PayloadSchemaId, "protocol.auth-session-state.v1", StringComparison.Ordinal)
            || envelope.PayloadSchemaVersion is null
            || envelope.PayloadSchemaVersion.Major != 1)
        {
            throw new InvalidDataException("auth.session.changed payload schema mismatch.");
        }

        AuthSessionStateV1 wire;
        try
        {
            wire = AuthSessionStateV1.Parser.ParseFrom(envelope.Payload);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new InvalidDataException("auth.session.changed structural decode failed.", ex);
        }
        Apply(wire);
        return true;
    }

    public void Apply(AuthSessionStateV1 wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        ValidateId128(wire.SessionId, nameof(wire.SessionId));
        if ((int)wire.AuthDomain != 1)
            throw new InvalidDataException("General View accepts only GENERAL_VIEW auth-domain session state.");
        if (wire.SessionGeneration == 0)
            throw new InvalidDataException("Session generation must be non-zero.");
        if (wire.EffectivePermissions.Count > 1024)
            throw new InvalidDataException("Effective permission count exceeds protocol limit.");

        var permissions = wire.EffectivePermissions.ToArray();
        string? previous = null;
        foreach (var permission in permissions)
        {
            ValidateStableToken(permission, "effective permission");
            if (!StandardPermissions.Contains(permission))
                throw new InvalidDataException($"Unknown General View permission '{permission}'.");
            if (previous is not null && string.CompareOrdinal(previous, permission) >= 0)
                throw new InvalidDataException("Effective permissions must be ASCII ascending and duplicate-free.");
            previous = permission;
        }

        var requiresDiverRef = permissions.BinarySearchOrdinal(ViewPermissionTokens.OperationDiver)
            || permissions.BinarySearchOrdinal(ViewPermissionTokens.ParticipationBind);
        if (wire.HasDiverRef)
            ValidateId128(wire.DiverRef, nameof(wire.DiverRef));
        if (requiresDiverRef && !wire.HasDiverRef)
            throw new InvalidDataException("Diver-authorized General View session requires diver_ref.");

        var state = (int)wire.Status switch
        {
            1 => ViewSessionAccessState.Active,
            2 => ViewSessionAccessState.ReauthenticationRequired,
            3 => ViewSessionAccessState.Revoked,
            4 => ViewSessionAccessState.Expired,
            _ => throw new InvalidDataException("Auth session status is unspecified or unsupported.")
        };

        Snapshot = new ViewSessionProjection(
            state,
            Hex(wire.SessionId),
            wire.EffectiveRoleSet,
            wire.SessionGeneration,
            wire.HasDiverRef ? Hex(wire.DiverRef) : null,
            permissions,
            state switch
            {
                ViewSessionAccessState.Active => null,
                ViewSessionAccessState.ReauthenticationRequired => "auth.reauthentication-required",
                ViewSessionAccessState.Revoked => "auth.session-revoked",
                ViewSessionAccessState.Expired => "auth.session-expired",
                _ => "auth.session-unknown"
            });
        Changed?.Invoke(Snapshot);
    }

    private static void ValidateStableToken(string value, string field)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsLowerAlphaNumeric(value[0]))
            throw new InvalidDataException($"{field} must use StableToken grammar.");
        for (var i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!IsLowerAlphaNumeric(ch) && ch is not ('.' or '_' or '/' or '-'))
                throw new InvalidDataException($"{field} must use StableToken grammar.");
        }
    }

    private static bool IsLowerAlphaNumeric(char value)
        => value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static void ValidateId128(ByteString value, string field)
    {
        if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"{field} must be non-zero Id128.");
    }

    private static string Hex(ByteString value) => Convert.ToHexStringLower(value.Span);
}

internal static class ParticipationListExtensions
{
    public static bool BinarySearchOrdinal(this IReadOnlyList<string> values, string value)
    {
        var low = 0;
        var high = values.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var comparison = string.CompareOrdinal(values[mid], value);
            if (comparison == 0) return true;
            if (comparison < 0) low = mid + 1;
            else high = mid - 1;
        }
        return false;
    }
}
