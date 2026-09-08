using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.View.State;

namespace MachiVerse.View.Operations;

public sealed class ViewOperationController
{
    private sealed class Entry(
        StandardOperationV1 request,
        string semanticTarget,
        ByteString predictedPayload)
    {
        public StandardOperationV1 Request { get; } = request;
        public string SemanticTarget { get; } = semanticTarget;
        public ByteString PredictedPayload { get; } = predictedPayload;
        public ViewOperationLifecycleState State { get; set; } = ViewOperationLifecycleState.LocalDraft;
        public ulong? EffectiveStep { get; set; }
        public ResultV1? TerminalResult { get; set; }
        public string? LastReasonCode { get; set; }
        public string? CorrelationId { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConfirmedWorldStore _confirmed;
    private readonly PredictionStore _predictions;
    private readonly ReconciliationCoordinator _reconciliation;
    private readonly ViewOperationCatalog _catalog;

    public ViewOperationController(
        ConfirmedWorldStore confirmed,
        PredictionStore predictions,
        ReconciliationCoordinator reconciliation,
        ViewOperationCatalog catalog)
    {
        _confirmed = confirmed ?? throw new ArgumentNullException(nameof(confirmed));
        _predictions = predictions ?? throw new ArgumentNullException(nameof(predictions));
        _reconciliation = reconciliation ?? throw new ArgumentNullException(nameof(reconciliation));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public ViewMutationAccessState AccessState { get; private set; } = ViewMutationAccessState.Blocked;

    public IReadOnlyList<TrackedViewOperation> Operations => _entries
        .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
        .Select(static pair => ToProjection(pair.Key, pair.Value))
        .ToArray();

    public event Action? Changed;

    public void SetAccessState(ViewMutationAccessState state, string? reasonCode = null)
    {
        AccessState = state;
        if (state != ViewMutationAccessState.Ready)
            _predictions.FreezeAll(reasonCode ?? AccessReason(state));
        Changed?.Invoke();
    }

    public TrackedViewOperation Prepare(ViewOperationDraft draft, ByteString operationId, ByteString immutablePayloadDigest)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateId128(operationId, nameof(operationId));
        ValidateHash256(immutablePayloadDigest, nameof(immutablePayloadDigest));
        ValidateDraft(draft);
        _catalog.Validate(draft);

        var request = BuildRequest(draft, operationId, immutablePayloadDigest);
        var key = Hex(operationId);
        if (_entries.TryGetValue(key, out var existing))
        {
            if (!existing.Request.Equals(request)
                || !string.Equals(existing.SemanticTarget, draft.SemanticTarget, StringComparison.Ordinal)
                || !existing.PredictedPayload.Equals(draft.PredictedPayload))
            {
                throw new InvalidDataException("Same OperationId cannot be reused for a different immutable View request.");
            }
            return ToProjection(key, existing);
        }

        var entry = new Entry(request, draft.SemanticTarget, draft.PredictedPayload);
        _entries.Add(key, entry);

        if (draft.PredictedPayload.Length > 0)
        {
            var source = _confirmed.Current
                ?? throw new InvalidOperationException("Local prediction requires a current confirmed snapshot.");
            _predictions.Add(operationId.Span, draft.SemanticTarget, source, draft.PredictedPayload.Span);
        }

        Changed?.Invoke();
        return ToProjection(key, entry);
    }

    public StandardOperationV1 TakeForSubmission(ByteString operationId)
    {
        RequireReadyForNewSend();
        var entry = RequireEntry(operationId);
        if (entry.State != ViewOperationLifecycleState.LocalDraft)
            throw new InvalidOperationException("Only a local draft can be submitted for the first time.");
        entry.State = ViewOperationLifecycleState.Sent;
        entry.LastReasonCode = null;
        Changed?.Invoke();
        return entry.Request.Clone();
    }

    public void MarkDeliveryUnknown(ByteString operationId, string reasonCode = "operation.delivery-unknown")
    {
        var entry = RequireEntry(operationId);
        if (entry.State != ViewOperationLifecycleState.Sent)
            throw new InvalidOperationException("Delivery can become unknown only after a send without authoritative acknowledgement.");
        entry.State = ViewOperationLifecycleState.DeliveryUnknown;
        entry.LastReasonCode = reasonCode;
        Changed?.Invoke();
    }

    public StandardOperationV1 RetryDelivery(ByteString operationId)
    {
        RequireReadyForNewSend();
        var entry = RequireEntry(operationId);
        if (entry.State != ViewOperationLifecycleState.DeliveryUnknown)
            throw new InvalidOperationException("Retry delivery requires DELIVERY_UNKNOWN state.");
        entry.State = ViewOperationLifecycleState.Sent;
        Changed?.Invoke();
        return entry.Request.Clone();
    }

    public bool TryApplyResult(WireEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(envelope.MessageType, "operation.result", StringComparison.Ordinal)) return false;
        if (!string.Equals(envelope.PayloadSchemaId, "protocol.operation-status-result.v1", StringComparison.Ordinal)
            || envelope.PayloadSchemaVersion is null
            || envelope.PayloadSchemaVersion.Major != 1)
        {
            throw new InvalidDataException("operation.result payload schema mismatch.");
        }
        if (envelope.OperationContext is null || !envelope.OperationContext.HasOperationId)
            throw new InvalidDataException("operation.result requires OperationContext.operation_id.");
        ValidateId128(envelope.OperationContext.OperationId, "operation_context.operation_id");
        if (!envelope.OperationContext.HasOperationPayloadDigest)
            throw new InvalidDataException("operation.result requires OperationContext.operation_payload_digest.");
        ValidateHash256(envelope.OperationContext.OperationPayloadDigest, "operation_context.operation_payload_digest");

        OperationStatusResultV1 wire;
        try
        {
            wire = OperationStatusResultV1.Parser.ParseFrom(envelope.Payload);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new InvalidDataException("operation.result structural decode failed.", ex);
        }

        ValidateId128(wire.OperationId, nameof(wire.OperationId));
        if (!wire.OperationId.Equals(envelope.OperationContext.OperationId))
            throw new InvalidDataException("operation.result payload OperationId does not match OperationContext.");

        var entry = RequireEntry(wire.OperationId);
        if (!entry.Request.ImmutablePayloadDigest.Equals(envelope.OperationContext.OperationPayloadDigest))
            throw new InvalidDataException("operation.result OperationContext digest does not match tracked immutable request.");
        if (wire.HasOperationPayloadDigest)
        {
            ValidateHash256(wire.OperationPayloadDigest, nameof(wire.OperationPayloadDigest));
            if (!wire.OperationPayloadDigest.Equals(entry.Request.ImmutablePayloadDigest))
                throw new InvalidDataException("operation.result payload digest does not match tracked immutable request.");
        }

        entry.CorrelationId = envelope.CorrelationId.Length == 16 ? Hex(envelope.CorrelationId) : null;
        entry.EffectiveStep = wire.HasEffectiveStep ? wire.EffectiveStep : entry.EffectiveStep;

        switch ((int)wire.State)
        {
            case 1: // UNKNOWN
                entry.State = ViewOperationLifecycleState.DeliveryUnknown;
                entry.LastReasonCode = "operation.status-unknown";
                break;
            case 2: // ACCEPTED
                entry.State = ViewOperationLifecycleState.AckedOrAccepted;
                entry.LastReasonCode = null;
                break;
            case 3: // SCHEDULED
                entry.State = ViewOperationLifecycleState.PendingAuthoritative;
                entry.LastReasonCode = null;
                break;
            case 4: // TERMINAL
                ApplyTerminal(entry, wire);
                break;
            default:
                throw new InvalidDataException("operation.result contains an invalid lifecycle state.");
        }

        Changed?.Invoke();
        return true;
    }

    private void ApplyTerminal(Entry entry, OperationStatusResultV1 wire)
    {
        if (wire.TerminalResult is null)
            throw new InvalidDataException("Terminal operation.result requires terminal_result.");
        var status = (int)wire.TerminalResult.Status;
        entry.State = status switch
        {
            1 or 4 or 5 => ViewOperationLifecycleState.Terminal,
            6 => ViewOperationLifecycleState.Rejected,
            7 => ViewOperationLifecycleState.Failed,
            _ => throw new InvalidDataException("Terminal operation.result contains a non-terminal ResultStatus.")
        };
        entry.TerminalResult = wire.TerminalResult.Clone();
        entry.LastReasonCode = wire.TerminalResult.Code;
        _reconciliation.OnTerminal(
            entry.Request.OperationId.Span,
            wire.TerminalResult,
            wire.HasEffectiveStep ? wire.EffectiveStep : null);
    }

    private static StandardOperationV1 BuildRequest(
        ViewOperationDraft draft,
        ByteString operationId,
        ByteString immutablePayloadDigest)
    {
        var admission = new OperationSchedulingAdmissionWireV1
        {
            AdmissionBasisStep = draft.AdmissionBasisStep,
            SchedulingPolicyGeneration = draft.SchedulingPolicyGeneration
        };
        if (draft.RequestedNotBeforeStep.HasValue)
            admission.RequestedNotBeforeStep = draft.RequestedNotBeforeStep.Value;
        if (draft.RequestedDeadlineStep.HasValue)
            admission.RequestedDeadlineStep = draft.RequestedDeadlineStep.Value;

        var request = new StandardOperationV1
        {
            OperationId = operationId,
            ImmutablePayloadDigest = immutablePayloadDigest,
            OperationKind = draft.OperationKind,
            Admission = admission,
            OperationPayloadSchemaId = draft.PayloadSchemaId,
            OperationPayloadSchemaVersion = new SchemaVersionWireV1
            {
                Major = draft.PayloadSchemaMajor,
                Minor = draft.PayloadSchemaMinor
            },
            OperationPayload = draft.Payload
        };
        if (draft.CandidateStep.HasValue)
            request.Candidate = new CandidateSchedulingWireV1 { CandidateStep = draft.CandidateStep.Value };
        return request;
    }

    private Entry RequireEntry(ByteString operationId)
    {
        ValidateId128(operationId, nameof(operationId));
        var key = Hex(operationId);
        return _entries.TryGetValue(key, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Unknown View OperationId '{key}'.");
    }

    private void RequireReadyForNewSend()
    {
        if (AccessState != ViewMutationAccessState.Ready)
            throw new InvalidOperationException($"World-affecting View send is fail-closed while access state is {AccessState}.");
    }

    private static void ValidateDraft(ViewOperationDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.OperationKind))
            throw new InvalidDataException("OperationKind must be non-empty.");
        if (draft.SchedulingPolicyGeneration == 0)
            throw new InvalidDataException("SchedulingPolicyGeneration must be non-zero.");
        if (draft.RequestedDeadlineStep.HasValue
            && draft.RequestedNotBeforeStep.HasValue
            && draft.RequestedDeadlineStep.Value < draft.RequestedNotBeforeStep.Value)
        {
            throw new InvalidDataException("Requested deadline cannot precede requested not-before Step.");
        }
        if (string.IsNullOrWhiteSpace(draft.PayloadSchemaId) || draft.PayloadSchemaMajor == 0)
            throw new InvalidDataException("Operation payload schema identity must be explicit.");
        if (string.IsNullOrWhiteSpace(draft.SemanticTarget))
            throw new InvalidDataException("Semantic target must be non-empty for reconciliation.");
        ArgumentNullException.ThrowIfNull(draft.Payload);
        ArgumentNullException.ThrowIfNull(draft.PredictedPayload);
    }

    private static TrackedViewOperation ToProjection(string key, Entry entry)
        => new(
            key,
            Hex(entry.Request.ImmutablePayloadDigest),
            entry.Request.OperationKind,
            entry.SemanticTarget,
            entry.State,
            entry.Request.Admission.AdmissionBasisStep,
            entry.Request.Candidate?.CandidateStep,
            entry.EffectiveStep,
            entry.TerminalResult?.Clone(),
            entry.LastReasonCode,
            entry.CorrelationId,
            entry.Request.Clone());

    private static void ValidateId128(ByteString value, string field)
    {
        if (value.Length != 16) throw new InvalidDataException($"{field} must be Id128.");
        if (value.Span.ToArray().All(static octet => octet == 0))
            throw new InvalidDataException($"{field} must not be ZERO.");
    }

    private static void ValidateHash256(ByteString value, string field)
    {
        if (value.Length != 32) throw new InvalidDataException($"{field} must be Hash256.");
    }

    private static string Hex(ByteString value) => Convert.ToHexStringLower(value.Span);

    private static string AccessReason(ViewMutationAccessState state) => state switch
    {
        ViewMutationAccessState.Resyncing => "view.resyncing",
        ViewMutationAccessState.Reconnecting => "view.reconnecting",
        ViewMutationAccessState.SessionRevoked => "auth.session-revoked",
        ViewMutationAccessState.Unauthorized => "auth.unauthorized",
        ViewMutationAccessState.Incompatible => "protocol.incompatible",
        _ => "view.mutation-blocked"
    };
}
