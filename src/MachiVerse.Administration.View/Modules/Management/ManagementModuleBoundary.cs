using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Modules.Management;

// ADMIN-03/04 boundary. Only canonical Gateway/Admin protocol payloads enter here;
// direct Config files, component internals and arbitrary command invocation are forbidden.
public interface IManagementModuleBoundary
{
    event Action? Changed;

    ManagementSnapshot Snapshot { get; }

    ConfigReadRequestV1 BuildConfigRead(ComponentTargetV1 target, IEnumerable<string>? keys = null);

    ConfigChangeDraft CreateDraft(ConfigTargetProjection current);

    ConfigChangeDraft CreateValueReturnDraft(
        ConfigTargetProjection current,
        IEnumerable<ConfigChangeEdit> desiredValues);

    ConfigChangeRequestV1 PrepareConfigChange(
        ConfigChangeDraft draft,
        ByteString operationId,
        ByteString immutablePayloadDigest,
        ulong? requestedEffectiveStep = null);

    OperationalCommandV1 PrepareOperationalCommand(
        string commandKind,
        ComponentTargetV1 target,
        ByteString payload,
        ByteString? operationId = null,
        ByteString? immutablePayloadDigest = null);

    ConfigChangeRequestV1 RetryConfigChange(ByteString operationId);

    OperationalCommandV1 RetryOperationalCommand(ByteString operationId);

    void MarkSubmitted(ByteString operationId);

    void MarkDeliveryUnknown(ByteString operationId);

    void SetAccess(ManagementAccessState config, ManagementAccessState command, string? reasonCode = null);

    bool TryApply(WireEnvelopeV1 envelope);
}
