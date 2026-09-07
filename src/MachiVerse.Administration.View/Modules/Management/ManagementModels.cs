using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Modules.Management;

public enum ManagementAccessState
{
    Unavailable,
    Available,
    Unauthorized,
    CapabilityMissing,
}

public sealed record ManagementChannelState(
    ManagementAccessState State,
    string? ReasonCode = null);

public sealed record ManagementResultProjection(
    int StatusValue,
    string Status,
    string Code,
    int RetryAdviceValue,
    string RetryAdvice,
    string Diagnostic)
{
    public static ManagementResultProjection FromWire(ResultV1? result)
        => result is null
            ? new(0, "Unspecified", "protocol.missing-result", 0, "Unspecified", string.Empty)
            : new(
                (int)result.Status,
                result.Status.ToString(),
                result.Code,
                (int)result.RetryAdvice,
                result.RetryAdvice.ToString(),
                result.Diagnostic);
}

public sealed record ConfigEntryProjection(
    string Key,
    string? EffectiveValueJson,
    string Impact,
    string Mutability,
    bool Sensitive,
    bool Redacted);

public sealed record ConfigTargetProjection(
    string TargetKey,
    string ComponentKind,
    string? LogicalInstanceId,
    ulong ConfigGeneration,
    string? ConfigDigest,
    IReadOnlyList<ConfigEntryProjection> Entries,
    ManagementResultProjection Result);

public sealed record ConfigChangeEdit(
    string Key,
    ConfigValueWireV1 Value);

public sealed record ConfigChangeDraft(
    ComponentTargetV1 Target,
    ulong BaseConfigGeneration,
    IReadOnlyList<ConfigChangeEdit> Edits)
{
    public ConfigChangeDraft WithEdits(IEnumerable<ConfigChangeEdit> edits)
        => new(Target.Clone(), BaseConfigGeneration, edits.ToArray());
}

public enum ManagementMutationKind
{
    ConfigChange,
    OperationalCommand,
}

public enum ManagementMutationState
{
    Prepared,
    Submitted,
    Accepted,
    Pending,
    Terminal,
    DeliveryUnknown,
    Rejected,
    Failed,
    StaleGeneration,
}

public sealed record TrackedManagementMutation(
    ManagementMutationKind Kind,
    string OperationId,
    string ImmutablePayloadDigest,
    string TargetKey,
    string RequestKind,
    ManagementMutationState State,
    string? ResultCode,
    int RetryAdviceValue,
    string RetryAdvice,
    ulong? ExpectedBaseGeneration,
    ulong? ResultingGeneration,
    ulong? EffectiveStep);

public sealed record CommandDescriptor(
    string CommandKind,
    IReadOnlyList<ComponentKindV1> AllowedTargetKinds,
    string PayloadSchemaId,
    uint PayloadSchemaMajor,
    uint PayloadSchemaMinor,
    string RequiredPermission,
    string ImpactClassification,
    bool StateChanging);

public sealed record ManagementSnapshot(
    IReadOnlyList<ConfigTargetProjection> ConfigTargets,
    IReadOnlyList<TrackedManagementMutation> Mutations,
    ManagementChannelState ConfigChannel,
    ManagementChannelState CommandChannel);

internal static class ManagementIdentity
{
    public static string TargetKey(ComponentTargetV1 target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var component = target.ComponentKind.ToString();
        return target.HasLogicalInstanceId
            ? $"{component}:{Hex(target.LogicalInstanceId)}"
            : component;
    }

    public static string Hex(ByteString value)
        => Convert.ToHexString(value.ToByteArray()).ToLowerInvariant();
}
