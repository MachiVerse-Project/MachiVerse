using Google.Protobuf;
using MachiVerse.Administration.View.Modules.Monitoring;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Modules.Management;

public sealed class SimulationAdminOperationController
{
    private readonly AdminSessionProjectionStore _session;
    private readonly HighImpactConfirmationController _confirmation;
    private readonly AdminOperationCatalog _catalog;
    private readonly Dictionary<string, StandardOperationV1> _requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackedAdminOperation> _operations = new(StringComparer.Ordinal);

    public SimulationAdminOperationController(
        AdminSessionProjectionStore session,
        HighImpactConfirmationController confirmation,
        AdminOperationCatalog catalog)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _session.Changed += OnSessionChanged;
    }

    public event Action? Changed;

    public IReadOnlyList<TrackedAdminOperation> Operations
        => _operations.Values.OrderBy(static value => value.OperationId, StringComparer.Ordinal).ToArray();

    public HighImpactConfirmationSnapshot Confirmation => _confirmation.Snapshot;

    public StandardOperationV1 Prepare(
        AdminOperationDraft draft,
        ByteString operationId,
        ByteString immutablePayloadDigest)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Payload);
        var descriptor = _catalog.Require(draft.OperationKind);
        _session.EnsurePermission(descriptor.RequiredPermission);

        var operationIdHex = AdminSessionProjectionStore.ValidateId128(operationId, nameof(operationId));
        var digestHex = AdminSessionProjectionStore.ValidateHash256(immutablePayloadDigest, nameof(immutablePayloadDigest));
        ValidateScheduling(draft);

        var request = new StandardOperationV1
        {
            OperationId = operationId,
            ImmutablePayloadDigest = immutablePayloadDigest,
            OperationKind = descriptor.OperationKind,
            Admission = new OperationSchedulingAdmissionWireV1
            {
                AdmissionBasisStep = draft.AdmissionBasisStep,
                SchedulingPolicyGeneration = draft.SchedulingPolicyGeneration,
            },
            OperationPayloadSchemaId = descriptor.PayloadSchemaId,
            OperationPayloadSchemaVersion = new SchemaVersionWireV1
            {
                Major = descriptor.PayloadSchemaMajor,
                Minor = descriptor.PayloadSchemaMinor,
            },
            OperationPayload = draft.Payload,
        };
        if (draft.RequestedNotBeforeStep is { } notBefore)
        {
            request.Admission.RequestedNotBeforeStep = notBefore;
        }
        if (draft.RequestedDeadlineStep is { } deadline)
        {
            request.Admission.RequestedDeadlineStep = deadline;
        }
        if (draft.CandidateStep is { } candidate)
        {
            request.Candidate = new CandidateSchedulingWireV1 { CandidateStep = candidate };
        }

        if (_requests.TryGetValue(operationIdHex, out var existing))
        {
            if (!existing.Equals(request))
            {
                throw new InvalidDataException(
                    "Same OperationId cannot be prepared with different immutable Admin Operation content.");
            }
            return existing.Clone();
        }

        var sessionGeneration = _session.Snapshot.SessionGeneration;
        var fingerprint = new AdminRequestFingerprint(operationIdHex, digestHex, descriptor.OperationKind);
        var initialState = descriptor.HighImpact
            ? AdminOperationLifecycleState.AwaitingConfirmation
            : AdminOperationLifecycleState.ReadyForSubmit;
        _requests.Add(operationIdHex, request.Clone());
        _operations.Add(operationIdHex, new TrackedAdminOperation(
            operationIdHex,
            digestHex,
            descriptor.OperationKind,
            descriptor.HighImpact,
            sessionGeneration,
            draft.AdmissionBasisStep,
            draft.CandidateStep,
            initialState,
            EffectiveStep: null,
            TerminalResult: null,
            CorrelationId: null,
            LastReasonCode: null));

        if (descriptor.HighImpact)
        {
            _confirmation.Require(fingerprint, sessionGeneration);
        }
        Changed?.Invoke();
        return request;
    }

    public void BeginConfirmation(ByteString operationId)
    {
        var operation = RequireTracked(operationId);
        EnsureHighImpact(operation);
        _session.EnsurePermission(AdminPermissionTokens.OperationSubmit);

        if (operation.State is not (AdminOperationLifecycleState.AwaitingConfirmation or AdminOperationLifecycleState.ReadyForSubmit))
        {
            throw new InvalidOperationException(
                $"High-impact confirmation cannot begin from operation state '{operation.State}'.");
        }

        var sessionGeneration = _session.Snapshot.SessionGeneration;
        var fingerprint = Fingerprint(operation);
        var snapshot = _confirmation.Snapshot;
        if (snapshot.State == HighImpactConfirmationState.ExpiredOrInvalid
            || snapshot.Fingerprint is null
            || !snapshot.Fingerprint.Equals(fingerprint)
            || snapshot.SessionGeneration != sessionGeneration)
        {
            _confirmation.Require(fingerprint, sessionGeneration);
        }

        _confirmation.Begin(fingerprint, sessionGeneration);
        _operations[operation.OperationId] = operation with
        {
            State = AdminOperationLifecycleState.AwaitingConfirmation,
            SessionGenerationAtPrepare = sessionGeneration,
            LastReasonCode = null,
        };
        Changed?.Invoke();
    }

    public LocalConfirmationEvidence Confirm(ByteString operationId)
    {
        var operation = RequireTracked(operationId);
        EnsureHighImpact(operation);
        _session.EnsurePermission(AdminPermissionTokens.OperationSubmit);
        var evidence = _confirmation.Confirm(Fingerprint(operation), _session.Snapshot.SessionGeneration);
        _operations[operation.OperationId] = operation with
        {
            State = AdminOperationLifecycleState.ReadyForSubmit,
            SessionGenerationAtPrepare = _session.Snapshot.SessionGeneration,
            LastReasonCode = null,
        };
        Changed?.Invoke();
        return evidence;
    }

    public StandardOperationV1 TakeForSubmission(ByteString operationId)
    {
        var operation = RequireTracked(operationId);
        _session.EnsurePermission(AdminPermissionTokens.OperationSubmit);
        if (operation.State is not (AdminOperationLifecycleState.ReadyForSubmit or AdminOperationLifecycleState.Prepared))
        {
            throw new InvalidOperationException(
                $"Admin Operation cannot be submitted from state '{operation.State}'.");
        }

        if (operation.HighImpact)
        {
            _ = _confirmation.Consume(Fingerprint(operation), _session.Snapshot.SessionGeneration);
        }

        _operations[operation.OperationId] = operation with
        {
            State = AdminOperationLifecycleState.Submitted,
            LastReasonCode = null,
        };
        Changed?.Invoke();
        return _requests[operation.OperationId].Clone();
    }

    public StandardOperationV1 RetryDelivery(ByteString operationId)
    {
        var operation = RequireTracked(operationId);
        _session.EnsurePermission(AdminPermissionTokens.OperationSubmit);
        if (operation.State is not (AdminOperationLifecycleState.Submitted or AdminOperationLifecycleState.DeliveryUnknown))
        {
            throw new InvalidOperationException(
                "Only submitted or delivery-unknown Admin Operations may be retried as the same delivery identity.");
        }
        return _requests[operation.OperationId].Clone();
    }

    public void MarkDeliveryUnknown(ByteString operationId)
    {
        var operation = RequireTracked(operationId);
        if (operation.State != AdminOperationLifecycleState.Submitted)
        {
            throw new InvalidOperationException(
                "Only a submitted Admin Operation can enter delivery-unknown state.");
        }
        _operations[operation.OperationId] = operation with
        {
            State = AdminOperationLifecycleState.DeliveryUnknown,
            LastReasonCode = "request.delivery-unknown",
        };
        Changed?.Invoke();
    }

    public bool TryApply(WireEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(envelope.MessageType, "operation.result", StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.Equals(envelope.PayloadSchemaId, "protocol.operation-status-result.v1", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"operation.result expected protocol.operation-status-result.v1, received '{envelope.PayloadSchemaId}'.");
        }

        var wire = OperationStatusResultV1.Parser.ParseFrom(envelope.Payload);
        var operationId = AdminSessionProjectionStore.ValidateId128(wire.OperationId, nameof(wire.OperationId));
        var operationContext = envelope.OperationContext
            ?? throw new InvalidDataException("operation.result requires OperationContext.");
        if (!operationContext.HasOperationId)
        {
            throw new InvalidDataException("operation.result OperationContext requires operation_id.");
        }
        var contextOperationId = AdminSessionProjectionStore.ValidateId128(
            operationContext.OperationId,
            "operation_context.operation_id");
        if (!string.Equals(contextOperationId, operationId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("operation.result payload and OperationContext OperationId differ.");
        }

        if (!_operations.TryGetValue(operationId, out var current))
        {
            return false;
        }

        if (operationContext.HasOperationPayloadDigest)
        {
            var contextDigest = AdminSessionProjectionStore.ValidateHash256(
                operationContext.OperationPayloadDigest,
                "operation_context.operation_payload_digest");
            if (!string.Equals(contextDigest, current.ImmutablePayloadDigest, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "operation.result OperationContext immutable payload digest differs from the tracked request.");
            }
        }

        if (wire.HasOperationPayloadDigest)
        {
            var digest = AdminSessionProjectionStore.ValidateHash256(
                wire.OperationPayloadDigest,
                nameof(wire.OperationPayloadDigest));
            if (!string.Equals(digest, current.ImmutablePayloadDigest, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Admin Operation result reused OperationId with a different immutable payload digest.");
            }
        }

        var terminal = AdminOperationResultProjection.FromWire(wire.TerminalResult);
        var state = MapLifecycle(wire, terminal, current.State);
        var correlationId = envelope.CorrelationId.Length == 16
            ? AdminSessionProjectionStore.Hex(envelope.CorrelationId)
            : current.CorrelationId;
        _operations[operationId] = current with
        {
            State = state,
            EffectiveStep = wire.HasEffectiveStep ? wire.EffectiveStep : current.EffectiveStep,
            TerminalResult = terminal ?? current.TerminalResult,
            CorrelationId = correlationId,
            LastReasonCode = terminal?.Code ?? current.LastReasonCode,
        };

        if (terminal?.Code is "auth.unauthorized" or "auth.session-revoked" or "auth.session-expired" or "auth.session-stale")
        {
            _session.MarkSecurityFailure(terminal.Code);
        }
        Changed?.Invoke();
        return true;
    }

    public AdminAuditCorrelationProjection CorrelateAudit(
        ByteString operationId,
        IEnumerable<AuditRecordProjection> auditRecords)
    {
        ArgumentNullException.ThrowIfNull(auditRecords);
        var operation = RequireTracked(operationId);
        var matching = auditRecords
            .Where(record => string.Equals(record.OperationId, operation.OperationId, StringComparison.Ordinal))
            .OrderBy(static record => record.TimestampUnixMillis)
            .ToArray();
        if (matching.Length == 0)
        {
            return new AdminAuditCorrelationProjection(
                operation.OperationId,
                AuditCorrelationState.Missing,
                0,
                LatestAuditEventKind: null,
                LatestResultCode: null,
                LatestSimulationStep: null,
                ReasonCode: "audit.operation-not-found");
        }

        var digestMismatch = matching.Any(record => record.ImmutablePayloadDigest is { } digest
            && !string.Equals(digest, operation.ImmutablePayloadDigest, StringComparison.Ordinal));
        var latest = matching[^1];
        return new AdminAuditCorrelationProjection(
            operation.OperationId,
            digestMismatch ? AuditCorrelationState.DigestMismatch : AuditCorrelationState.Matched,
            matching.Length,
            latest.AuditEventKind,
            latest.ResultCode,
            latest.SimulationStep,
            digestMismatch ? "audit.immutable-digest-mismatch" : null);
    }

    private void OnSessionChanged(AdminSessionProjection session)
    {
        _confirmation.OnSessionChanged(session);
        if (session.State == AdminSessionAccessState.Active)
        {
            foreach (var (key, operation) in _operations.ToArray())
            {
                if (operation.HighImpact
                    && operation.SessionGenerationAtPrepare != session.SessionGeneration
                    && operation.State is AdminOperationLifecycleState.AwaitingConfirmation or AdminOperationLifecycleState.ReadyForSubmit)
                {
                    _operations[key] = operation with
                    {
                        State = AdminOperationLifecycleState.AwaitingConfirmation,
                        SessionGenerationAtPrepare = session.SessionGeneration,
                        LastReasonCode = "confirmation.session-generation-changed",
                    };
                }
            }
            Changed?.Invoke();
            return;
        }

        foreach (var (key, operation) in _operations.ToArray())
        {
            if (operation.State is AdminOperationLifecycleState.Prepared
                or AdminOperationLifecycleState.AwaitingConfirmation
                or AdminOperationLifecycleState.ReadyForSubmit)
            {
                _operations[key] = operation with
                {
                    State = AdminOperationLifecycleState.Unauthorized,
                    LastReasonCode = session.ReasonCode ?? "auth.session-inactive",
                };
            }
        }
        Changed?.Invoke();
    }

    private TrackedAdminOperation RequireTracked(ByteString operationId)
    {
        var key = AdminSessionProjectionStore.ValidateId128(operationId, nameof(operationId));
        return _operations.TryGetValue(key, out var operation)
            ? operation
            : throw new KeyNotFoundException($"Unknown Admin OperationId '{key}'.");
    }

    private static void EnsureHighImpact(TrackedAdminOperation operation)
    {
        if (!operation.HighImpact)
        {
            throw new InvalidOperationException("Operation does not require high-impact confirmation.");
        }
    }

    private static AdminRequestFingerprint Fingerprint(TrackedAdminOperation operation)
        => new(operation.OperationId, operation.ImmutablePayloadDigest, operation.OperationKind);

    private static void ValidateScheduling(AdminOperationDraft draft)
    {
        AdminSessionProjectionStore.ValidateStableToken(draft.OperationKind, nameof(draft.OperationKind));
        if (draft.RequestedNotBeforeStep is { } notBefore
            && draft.RequestedDeadlineStep is { } deadline
            && deadline < notBefore)
        {
            throw new InvalidDataException("requested_deadline_step cannot precede requested_not_before_step.");
        }
    }

    private static AdminOperationLifecycleState MapLifecycle(
        OperationStatusResultV1 wire,
        AdminOperationResultProjection? terminal,
        AdminOperationLifecycleState current)
        => (int)wire.State switch
        {
            2 => AdminOperationLifecycleState.Accepted,
            3 => AdminOperationLifecycleState.Scheduled,
            4 when terminal?.Code is "auth.unauthorized" or "auth.session-revoked" or "auth.session-expired" or "auth.session-stale"
                => AdminOperationLifecycleState.Unauthorized,
            4 when terminal?.StatusValue == 6 => AdminOperationLifecycleState.Rejected,
            4 when terminal?.StatusValue == 7 => AdminOperationLifecycleState.Failed,
            4 => AdminOperationLifecycleState.Terminal,
            _ => current,
        };
}
