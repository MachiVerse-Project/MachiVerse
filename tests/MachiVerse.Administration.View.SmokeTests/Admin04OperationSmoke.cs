using Google.Protobuf;
using MachiVerse.Administration.View.Configuration;
using MachiVerse.Administration.View.Modules.Management;
using MachiVerse.Administration.View.Modules.Monitoring;
using MachiVerse.Protocol.V1;

internal static class Admin04OperationSmoke
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        var config = new AdminViewConfig(
            DashboardRefreshMs: 1000,
            MetricsLocalHistorySamples: 3600,
            MetricsMaxSeries: 200,
            LogDefaultPageSize: 200,
            AuditDefaultPageSize: 200,
            PresentationTimeoutMs: 30000,
            ConfirmationUxTimeoutSeconds: 10,
            ReconnectInitialMs: 250,
            ReconnectMaxMs: 10000);
        var session = new AdminSessionProjectionStore();
        var confirmation = new HighImpactConfirmationController(config, () => now);
        var descriptor = new AdminOperationDescriptor(
            OperationKind: "fixture.admin-simulation",
            PayloadSchemaId: "fixture.admin-operation.v1",
            PayloadSchemaMajor: 1,
            PayloadSchemaMinor: 0,
            HighImpact: true);
        var catalog = new AdminOperationCatalog([descriptor]);
        var controller = new SimulationAdminOperationController(session, confirmation, catalog);

        ApplyActiveSession(session, generation: 7);
        Assert(session.Snapshot.State == AdminSessionAccessState.Active);
        Assert(session.Snapshot.HasPermission(AdminPermissionTokens.OperationSubmit));

        var operationId = Id(40);
        var digest = Digest(41);
        var draft = Draft(descriptor.OperationKind, candidateStep: 105, payload: "first");
        var prepared = controller.Prepare(draft, operationId, digest);
        Assert(prepared.OperationId.Equals(operationId));
        Assert(prepared.ImmutablePayloadDigest.Equals(digest));
        Assert(prepared.Candidate is not null && prepared.Candidate.CandidateStep == 105);
        Assert(controller.Operations.Single().CandidateStep == 105);
        Assert(controller.Operations.Single().EffectiveStep is null);
        Assert(controller.Confirmation.State == HighImpactConfirmationState.Required);
        AssertThrows<InvalidOperationException>(() => controller.TakeForSubmission(operationId));

        controller.BeginConfirmation(operationId);
        Assert(controller.Confirmation.State == HighImpactConfirmationState.Confirming);
        var evidence = controller.Confirm(operationId);
        Assert(evidence.Fingerprint.OperationId == Hex(operationId));
        Assert(evidence.EvidenceId != Hex(operationId));
        Assert(controller.Operations.Single().State == AdminOperationLifecycleState.ReadyForSubmit);

        var submission = controller.TakeForSubmission(operationId);
        Assert(submission.OperationId.Equals(operationId));
        Assert(submission.ImmutablePayloadDigest.Equals(digest));
        Assert(controller.Operations.Single().State == AdminOperationLifecycleState.Submitted);
        Assert(controller.Confirmation.State == HighImpactConfirmationState.ExpiredOrInvalid);
        Assert(controller.Confirmation.Consumed);

        controller.MarkDeliveryUnknown(operationId);
        var retry = controller.RetryDelivery(operationId);
        Assert(retry.OperationId.Equals(operationId));
        Assert(retry.ImmutablePayloadDigest.Equals(digest));
        Assert(retry.Equals(submission));

        AssertThrows<InvalidDataException>(() => controller.Prepare(
            Draft(descriptor.OperationKind, candidateStep: 105, payload: "different"),
            operationId,
            digest));

        var accepted = new OperationStatusResultV1
        {
            OperationId = operationId,
            OperationPayloadDigest = digest,
            State = (OperationLifecycleWireStateV1)2, // ACCEPTED
        };
        Assert(controller.TryApply(ResultEnvelope(accepted, correlationFirstByte: 60)));
        Assert(controller.Operations.Single().State == AdminOperationLifecycleState.Accepted);
        Assert(controller.Operations.Single().EffectiveStep is null); // candidate is not authoritative.

        var scheduled = new OperationStatusResultV1
        {
            OperationId = operationId,
            OperationPayloadDigest = digest,
            State = (OperationLifecycleWireStateV1)3, // SCHEDULED
            EffectiveStep = 110,
        };
        Assert(controller.TryApply(ResultEnvelope(scheduled, correlationFirstByte: 60)));
        Assert(controller.Operations.Single().State == AdminOperationLifecycleState.Scheduled);
        Assert(controller.Operations.Single().EffectiveStep == 110);

        var terminal = new OperationStatusResultV1
        {
            OperationId = operationId,
            OperationPayloadDigest = digest,
            State = (OperationLifecycleWireStateV1)4, // TERMINAL
            EffectiveStep = 110,
            TerminalResult = new ResultV1
            {
                Status = (ResultStatusV1)1, // SUCCESS
                Code = "ok",
                RetryAdvice = (RetryAdviceV1)1, // DO_NOT_RETRY
            },
        };
        Assert(controller.TryApply(ResultEnvelope(terminal, correlationFirstByte: 60)));
        var tracked = controller.Operations.Single();
        Assert(tracked.State == AdminOperationLifecycleState.Terminal);
        Assert(tracked.TerminalResult?.Code == "ok");
        Assert(tracked.CorrelationId == Hex(Id(60)));

        var audit = new AuditRecordProjection(
            AuditRecordId: Hex(Id(61)),
            TimestampUnixMillis: 1000,
            AuditEventKind: "admin.operation.terminal",
            ActorAccountRef: Hex(Id(62)),
            OperationId: Hex(operationId),
            ImmutablePayloadDigest: Hex(digest),
            SimulationStep: 110,
            TargetKind: "simulation-core",
            ResultCode: "ok",
            Attributes: Array.Empty<KeyValueProjection>());
        var correlation = controller.CorrelateAudit(operationId, [audit]);
        Assert(correlation.State == AuditCorrelationState.Matched);
        Assert(correlation.LatestSimulationStep == 110);
        var badAudit = audit with { ImmutablePayloadDigest = Hex(Digest(99)) };
        Assert(controller.CorrelateAudit(operationId, [badAudit]).State == AuditCorrelationState.DigestMismatch);

        // Expired confirmation cannot submit.
        var expiringId = Id(42);
        controller.Prepare(Draft(descriptor.OperationKind, 120, "expiring"), expiringId, Digest(42));
        controller.BeginConfirmation(expiringId);
        _ = controller.Confirm(expiringId);
        now = now.AddSeconds(11);
        Assert(controller.Confirmation.State == HighImpactConfirmationState.ExpiredOrInvalid);
        AssertThrows<InvalidOperationException>(() => controller.TakeForSubmission(expiringId));

        // Session generation change invalidates already-confirmed evidence.
        var generationChangeId = Id(43);
        controller.Prepare(Draft(descriptor.OperationKind, 130, "generation-change"), generationChangeId, Digest(43));
        controller.BeginConfirmation(generationChangeId);
        _ = controller.Confirm(generationChangeId);
        ApplyActiveSession(session, generation: 8);
        Assert(controller.Confirmation.State == HighImpactConfirmationState.ExpiredOrInvalid);
        AssertThrows<InvalidOperationException>(() => controller.TakeForSubmission(generationChangeId));

        // Severe revoke stops new protected mutations and invalidates pending confirmation.
        var revokePendingId = Id(44);
        controller.Prepare(Draft(descriptor.OperationKind, 140, "revoke"), revokePendingId, Digest(44));
        controller.BeginConfirmation(revokePendingId);
        ApplySession(session, generation: 8, status: 3); // REVOKED
        Assert(session.Snapshot.State == AdminSessionAccessState.Revoked);
        Assert(controller.Confirmation.State == HighImpactConfirmationState.ExpiredOrInvalid);
        Assert(controller.Operations.Single(x => x.OperationId == Hex(revokePendingId)).State == AdminOperationLifecycleState.Unauthorized);
        AssertThrows<InvalidOperationException>(() => controller.Prepare(
            Draft(descriptor.OperationKind, 150, "blocked"),
            Id(45),
            Digest(45)));

        // Session payload validation remains Admin-domain-specific and ordered.
        var invalidPermissions = SessionWire(generation: 9, status: 1);
        invalidPermissions.EffectivePermissions.Clear();
        invalidPermissions.EffectivePermissions.Add("admin.operation.submit");
        invalidPermissions.EffectivePermissions.Add("admin.audit.read");
        AssertThrows<InvalidDataException>(() => session.Apply(invalidPermissions));

        Console.WriteLine("ADMIN-04 high-impact/session/operation smoke checks passed.");
    }

    private static AdminOperationDraft Draft(string operationKind, ulong candidateStep, string payload)
        => new(
            OperationKind: operationKind,
            AdmissionBasisStep: 100,
            SchedulingPolicyGeneration: 4,
            RequestedNotBeforeStep: 102,
            RequestedDeadlineStep: 180,
            CandidateStep: candidateStep,
            Payload: ByteString.CopyFromUtf8(payload));

    private static void ApplyActiveSession(AdminSessionProjectionStore session, ulong generation)
        => ApplySession(session, generation, status: 1);

    private static void ApplySession(AdminSessionProjectionStore session, ulong generation, int status)
        => session.Apply(SessionWire(generation, status));

    private static AuthSessionStateV1 SessionWire(ulong generation, int status)
    {
        var wire = new AuthSessionStateV1
        {
            SessionId = Id(50),
            AuthDomain = (AuthDomainWireV1)2, // ADMIN_VIEW
            EffectiveRoleSet = "admin.operator",
            SessionGeneration = generation,
            Status = (SessionWireStatusV1)status,
        };
        wire.EffectivePermissions.Add("admin.audit.read");
        wire.EffectivePermissions.Add("admin.operation.submit");
        return wire;
    }

    private static WireEnvelopeV1 ResultEnvelope(OperationStatusResultV1 payload, byte correlationFirstByte)
        => new()
        {
            MessageType = "operation.result",
            PayloadSchemaId = "protocol.operation-status-result.v1",
            CorrelationId = Id(correlationFirstByte),
            Payload = payload.ToByteString(),
        };

    private static ByteString Id(byte first)
    {
        var bytes = new byte[16];
        bytes[0] = first;
        return ByteString.CopyFrom(bytes);
    }

    private static ByteString Digest(byte first)
    {
        var bytes = new byte[32];
        bytes[0] = first;
        return ByteString.CopyFrom(bytes);
    }

    private static string Hex(ByteString value)
        => Convert.ToHexString(value.ToByteArray()).ToLowerInvariant();

    private static void Assert(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("ADMIN-04 smoke assertion failed.");
        }
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
