using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.View.Operations;
using MachiVerse.View.Participation;
using MachiVerse.View.Protocol;
using MachiVerse.View.State;

internal static class View05ParticipationSmoke
{
    public static void Run()
    {
        var session = new ViewSessionProjectionStore();
        var binding = new ParticipationBindingProjectionStore();
        var preferences = new ParticipationPreferenceCatalog(
        [
            new ParticipationPreferenceProfile(
                "fixture.broad",
                ["fixture.adult", "fixture.local"])
        ]);
        var absencePolicies = new AbsencePolicyProfileCatalog(
            ["fixture.autonomy", "fixture.pause-control"]);

        var confirmed = new ConfirmedWorldStore();
        var predictions = new PredictionStore();
        using var reconciliation = new ReconciliationCoordinator(confirmed, predictions);
        var operationCatalog = new ViewOperationCatalog(
        [
            new ViewOperationDescriptor(
                "participation.binding.create",
                "operation.participation.binding.create",
                1,
                0)
        ]);
        var operations = new ViewOperationController(confirmed, predictions, reconciliation, operationCatalog);
        operations.SetAccessState(ViewMutationAccessState.Ready);

        var fixtureAdapter = new FixtureParticipationOperationPayloadAdapter();
        using var participation = new DiverParticipationController(
            session,
            binding,
            preferences,
            absencePolicies,
            fixtureAdapter,
            operations);

        ApplyDiverSession(session, generation: 1);
        Assert(session.Snapshot.State == ViewSessionAccessState.Active);
        Assert(session.Snapshot.HasPermission(ViewPermissionTokens.ParticipationBind));
        Assert(session.Snapshot.HasPermission(ViewPermissionTokens.ParticipationPolicyWrite));
        Assert(session.Snapshot.HasPermission(ViewPermissionTokens.OperationDiver));

        // Session projection is General View only and requires canonical permission ordering.
        var wrongDomain = SessionWire(generation: 1, status: 1);
        wrongDomain.AuthDomain = (AuthDomainWireV1)2;
        AssertThrows<InvalidDataException>(() => session.Apply(wrongDomain));
        var wrongOrder = SessionWire(generation: 1, status: 1);
        wrongOrder.EffectivePermissions.Clear();
        wrongOrder.EffectivePermissions.Add(ViewPermissionTokens.ParticipationBind);
        wrongOrder.EffectivePermissions.Add(ViewPermissionTokens.OperationDiver);
        AssertThrows<InvalidDataException>(() => session.Apply(wrongOrder));

        ApplyBindingEnvelope(binding, status: 1, basisStep: 100); // NONE
        Assert(binding.Snapshot.State == ParticipationBindingState.None);
        Assert(binding.Snapshot.Freshness == ParticipationProjectionFreshness.Confirmed);
        Assert(participation.CanRequestBinding);
        Assert(participation.State == ParticipationUxState.EligibleUnbound);

        participation.SetJoinPreference("fixture.broad", ["fixture.adult", "fixture.local"]);
        participation.SetAbsencePolicyProfile("fixture.pause-control");
        Assert(participation.Draft.JoinPreference.ProfileId == "fixture.broad");
        Assert(participation.Draft.AbsencePolicy.ProfileId == "fixture.pause-control");
        Assert(binding.Snapshot.AbsencePolicyProfile is null); // local draft is not confirmed state.

        var operationId = Id(40);
        var digest = Hash(41);
        var tracked = participation.PrepareOperation(
            ParticipationOperationIntentKind.BindingCreate,
            new ParticipationSchedulingContext(
                AdmissionBasisStep: 100,
                SchedulingPolicyGeneration: 3,
                RequestedNotBeforeStep: 102,
                RequestedDeadlineStep: 160,
                CandidateStep: 104),
            operationId,
            digest);
        Assert(tracked.OperationKind == "participation.binding.create");
        Assert(tracked.ImmutablePayloadDigest == Hex(digest));
        Assert(participation.Draft.PendingOperationId == Hex(operationId));

        var firstSend = operations.TakeForSubmission(operationId);
        operations.MarkDeliveryUnknown(operationId);
        var retry = operations.RetryDelivery(operationId);
        Assert(retry.Equals(firstSend));
        Assert(retry.OperationId.Equals(operationId));
        Assert(retry.ImmutablePayloadDigest.Equals(digest));

        // Terminal success still does not create a binding locally.
        var terminal = new OperationStatusResultV1
        {
            OperationId = operationId,
            OperationPayloadDigest = digest,
            State = (OperationLifecycleWireStateV1)4,
            EffectiveStep = 105,
            TerminalResult = new ResultV1
            {
                Status = (ResultStatusV1)1,
                Code = "ok",
                RetryAdvice = (RetryAdviceV1)1
            }
        };
        Assert(operations.TryApplyResult(OperationResultEnvelope(terminal, operationId, digest)));
        Assert(participation.Draft.PendingOperationId is null);
        Assert(binding.Snapshot.State == ParticipationBindingState.None);

        ApplyBindingEnvelope(
            binding,
            status: 2,
            basisStep: 105,
            bindingId: Id(50),
            residentId: Id(51),
            effectiveFromStep: 105,
            absencePolicyProfile: "fixture.autonomy");
        Assert(binding.Snapshot.State == ParticipationBindingState.Active);
        Assert(binding.Snapshot.CanTreatAsCurrentControl);
        Assert(participation.State == ParticipationUxState.Active);
        Assert(participation.CanSubmitControl);
        Assert(binding.Snapshot.AbsencePolicyProfile == "fixture.autonomy");
        Assert(participation.Draft.AbsencePolicy.ProfileId == "fixture.pause-control");

        // Reconnect keeps old data for context but removes its authority until a fresh server projection arrives.
        participation.BeginReconnect();
        Assert(binding.Snapshot.Freshness == ParticipationProjectionFreshness.RefreshRequired);
        Assert(!binding.Snapshot.CanTreatAsCurrentControl);
        Assert(participation.State == ParticipationUxState.RefreshRequired);
        ApplyBindingEnvelope(
            binding,
            status: 2,
            basisStep: 106,
            bindingId: Id(50),
            residentId: Id(51),
            effectiveFromStep: 105,
            absencePolicyProfile: "fixture.autonomy");
        Assert(binding.Snapshot.CanTreatAsCurrentControl);

        // Session revoke stops new protected input but does not invent a binding release.
        var operationCountBeforeRevoke = operations.Operations.Count;
        session.Apply(SessionWire(generation: 2, status: 3));
        Assert(session.Snapshot.State == ViewSessionAccessState.Revoked);
        Assert(participation.State == ParticipationUxState.SessionUnavailable);
        Assert(binding.Snapshot.State == ParticipationBindingState.Active);
        Assert(binding.Snapshot.BindingId == Hex(Id(50)));
        Assert(operations.Operations.Count == operationCountBeforeRevoke);
        Assert(operations.AccessState == ViewMutationAccessState.SessionRevoked);

        // Resident death returns UX to normal participation flow after a fresh active session.
        ApplyDiverSession(session, generation: 3);
        operations.SetAccessState(ViewMutationAccessState.Ready);
        ApplyBindingEnvelope(
            binding,
            status: 3,
            basisStep: 107,
            bindingId: Id(50),
            residentId: Id(51),
            effectiveFromStep: 105,
            absencePolicyProfile: "fixture.autonomy");
        Assert(participation.State == ParticipationUxState.ResidentDeceased);
        Assert(!participation.CanSubmitControl);
        Assert(participation.CanRequestBinding);

        // Production-style unavailable codec fails closed before an undefined domain payload is created.
        using var unavailable = new DiverParticipationController(
            session,
            binding,
            preferences,
            absencePolicies,
            new UnavailableParticipationOperationPayloadAdapter(),
            operations);
        AssertThrows<InvalidOperationException>(() => unavailable.PrepareOperation(
            ParticipationOperationIntentKind.BindingCreate,
            new ParticipationSchedulingContext(107, 3, 108, 180, 110),
            Id(60),
            Hash(60)));
        Assert(unavailable.Draft.LastLocalReasonCode == "participation.operation-codec-unavailable");

        Console.WriteLine("VIEW-05 Participation UX smoke checks passed.");
    }

    private static void ApplyDiverSession(ViewSessionProjectionStore session, ulong generation)
        => session.Apply(SessionWire(generation, status: 1));

    private static AuthSessionStateV1 SessionWire(ulong generation, int status)
    {
        var wire = new AuthSessionStateV1
        {
            SessionId = Id(10),
            AuthDomain = (AuthDomainWireV1)1,
            EffectiveRoleSet = "view.diver",
            SessionGeneration = generation,
            Status = (SessionWireStatusV1)status
        };
        wire.EffectivePermissions.Add(ViewPermissionTokens.OperationDiver);
        wire.EffectivePermissions.Add(ViewPermissionTokens.ParticipationBind);
        wire.EffectivePermissions.Add(ViewPermissionTokens.ParticipationPolicyWrite);
        wire.EffectivePermissions.Add(ViewPermissionTokens.SessionReadSelf);
        wire.EffectivePermissions.Add(ViewPermissionTokens.WorldReadParticipant);
        wire.EffectivePermissions.Add(ViewPermissionTokens.WorldReadPublic);
        wire.EffectivePermissions.Add(ViewPermissionTokens.WorldSubscribe);
        return wire;
    }

    private static void ApplyBindingEnvelope(
        ParticipationBindingProjectionStore store,
        int status,
        ulong basisStep,
        ByteString? bindingId = null,
        ByteString? residentId = null,
        ulong? effectiveFromStep = null,
        string? absencePolicyProfile = null)
    {
        var payload = new ParticipationBindingViewV1
        {
            Status = (ParticipationBindingWireStatusV1)status
        };
        if (bindingId is not null) payload.BindingId = bindingId;
        if (residentId is not null) payload.ResidentId = residentId;
        if (effectiveFromStep.HasValue) payload.EffectiveFromStep = effectiveFromStep.Value;
        if (absencePolicyProfile is not null) payload.AbsencePolicyProfile = absencePolicyProfile;

        var envelope = new WireEnvelopeV1
        {
            EnvelopeVersion = 1,
            ProtocolId = GatewayEnvelopeCodec.ProtocolId,
            ProtocolVersion = new ProtocolVersionV1 { Major = 1, Minor = 0 },
            NegotiationGeneration = 1,
            MessageType = "participation.binding.state",
            MessageId = Id(70),
            CorrelationId = Id(71),
            SenderInstanceId = Id(72),
            BasisStep = basisStep,
            PayloadSchemaId = "protocol.participation-binding-view.v1",
            PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            PayloadCompression = (CompressionKindV1)1,
            Payload = payload.ToByteString()
        };
        Assert(store.TryApply(GatewayEnvelopeCodec.Decode(GatewayEnvelopeCodec.Encode(envelope))));
    }

    private static WireEnvelopeV1 OperationResultEnvelope(
        OperationStatusResultV1 result,
        ByteString operationId,
        ByteString digest)
    {
        var envelope = new WireEnvelopeV1
        {
            EnvelopeVersion = 1,
            ProtocolId = GatewayEnvelopeCodec.ProtocolId,
            ProtocolVersion = new ProtocolVersionV1 { Major = 1, Minor = 0 },
            NegotiationGeneration = 1,
            MessageType = "operation.result",
            MessageId = Id(80),
            CorrelationId = Id(81),
            SenderInstanceId = Id(82),
            OperationContext = new OperationContextWireV1
            {
                OperationId = operationId,
                OperationPayloadDigest = digest
            },
            PayloadSchemaId = "protocol.operation-status-result.v1",
            PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            PayloadCompression = (CompressionKindV1)1,
            Payload = result.ToByteString()
        };
        return GatewayEnvelopeCodec.Decode(GatewayEnvelopeCodec.Encode(envelope));
    }

    private static ByteString Id(byte value)
        => ByteString.CopyFrom(Enumerable.Repeat(value, 16).ToArray());

    private static ByteString Hash(byte value)
        => ByteString.CopyFrom(Enumerable.Repeat(value, 32).ToArray());

    private static string Hex(ByteString value)
        => Convert.ToHexStringLower(value.Span);

    private static void Assert(bool condition)
    {
        if (!condition) throw new InvalidOperationException("VIEW-05 smoke assertion failed.");
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

    private sealed class FixtureParticipationOperationPayloadAdapter : IParticipationOperationPayloadAdapter
    {
        public bool TryEncode(
            ParticipationOperationIntent intent,
            out EncodedParticipationOperation? operation,
            out string reasonCode)
        {
            if (intent.Kind != ParticipationOperationIntentKind.BindingCreate)
            {
                operation = null;
                reasonCode = "fixture.unsupported";
                return false;
            }

            operation = new EncodedParticipationOperation(
                "participation.binding.create",
                "operation.participation.binding.create",
                1,
                0,
                ByteString.CopyFromUtf8("fixture-canonical-owner-payload"),
                "participation:fixture-diver",
                ByteString.Empty);
            reasonCode = string.Empty;
            return true;
        }
    }
}
