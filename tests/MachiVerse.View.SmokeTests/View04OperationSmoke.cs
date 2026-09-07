using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.View.Operations;
using MachiVerse.View.Protocol;
using MachiVerse.View.State;

internal static class View04OperationSmoke
{
    public static void Run()
    {
        var store = new ConfirmedWorldStore();
        var publicationConsumer = new PublicationConsumer(store);
        InstallFull(publicationConsumer, basisStep: 100, tokenByte: 20);

        var predictions = new PredictionStore();
        using var reconciliation = new ReconciliationCoordinator(store, predictions);
        var controller = new ViewOperationController(store, predictions, reconciliation);
        controller.SetAccessState(ViewMutationAccessState.Ready);

        var descriptorKind = "fixture.view-action";
        var operationId = Id(40);
        var digest = Hash(41);
        var draft = Draft(descriptorKind, candidateStep: 105, payload: "input-a", predicted: "predicted-a");

        var prepared = controller.Prepare(draft, operationId, digest);
        Assert(prepared.State == ViewOperationLifecycleState.LocalDraft);
        Assert(prepared.CandidateStep == 105);
        Assert(prepared.EffectiveStep is null);
        Assert(predictions.Entries.Count == 1);
        Assert(predictions.Entries[0].SourceConfirmedBasisStep == 100);
        Assert(predictions.Entries[0].State == PredictionEntryState.Active);

        var submission = controller.TakeForSubmission(operationId);
        Assert(submission.OperationId.Equals(operationId));
        Assert(submission.ImmutablePayloadDigest.Equals(digest));
        controller.MarkDeliveryUnknown(operationId);
        var statusQuery = controller.CreateStatusQuery(operationId);
        Assert(statusQuery.OperationId.Equals(operationId));
        var retry = controller.RetryDelivery(operationId);
        Assert(retry.Equals(submission));
        Assert(retry.OperationId.Equals(operationId));
        Assert(retry.ImmutablePayloadDigest.Equals(digest));

        AssertThrows<InvalidDataException>(() => controller.Prepare(
            Draft(descriptorKind, candidateStep: 105, payload: "different", predicted: "predicted-a"),
            operationId,
            digest));

        var accepted = new OperationStatusResultV1
        {
            OperationId = operationId,
            OperationPayloadDigest = digest,
            State = (OperationLifecycleWireStateV1)2
        };
        Assert(controller.TryApplyResult(ResultEnvelope(accepted, operationId, digest, correlationByte: 60)));
        Assert(controller.Operations.Single().State == ViewOperationLifecycleState.AckedOrAccepted);
        Assert(controller.Operations.Single().EffectiveStep is null);

        var scheduled = new OperationStatusResultV1
        {
            OperationId = operationId,
            OperationPayloadDigest = digest,
            State = (OperationLifecycleWireStateV1)3,
            EffectiveStep = 108
        };
        Assert(controller.TryApplyResult(ResultEnvelope(scheduled, operationId, digest, correlationByte: 60)));
        var scheduledProjection = controller.Operations.Single();
        Assert(scheduledProjection.State == ViewOperationLifecycleState.PendingAuthoritative);
        Assert(scheduledProjection.CandidateStep == 105);
        Assert(scheduledProjection.EffectiveStep == 108);

        var terminal = new OperationStatusResultV1
        {
            OperationId = operationId,
            OperationPayloadDigest = digest,
            State = (OperationLifecycleWireStateV1)4,
            EffectiveStep = 108,
            TerminalResult = new ResultV1
            {
                Status = (ResultStatusV1)1,
                Code = "ok",
                RetryAdvice = (RetryAdviceV1)1
            }
        };
        Assert(controller.TryApplyResult(ResultEnvelope(terminal, operationId, digest, correlationByte: 60)));
        var terminalProjection = controller.Operations.Single();
        Assert(terminalProjection.State == ViewOperationLifecycleState.Terminal);
        Assert(terminalProjection.TerminalResult?.Code == "ok");
        Assert(terminalProjection.EffectiveStep == 108);
        Assert(terminalProjection.CorrelationId == Hex(Id(60)));
        Assert(predictions.Entries.Single().State == PredictionEntryState.AwaitingConfirmed);

        // A terminal success does not make prediction authoritative. It remains until a confirmed swap
        // reaches the authoritative effective Step.
        InstallFull(publicationConsumer, basisStep: 108, tokenByte: 21);
        Assert(predictions.Entries.Count == 0);

        // Reject must cancel local prediction immediately.
        var rejectedId = Id(42);
        var rejectedDigest = Hash(42);
        controller.Prepare(Draft(descriptorKind, 109, "reject-me", "predicted-reject"), rejectedId, rejectedDigest);
        _ = controller.TakeForSubmission(rejectedId);
        var rejected = new OperationStatusResultV1
        {
            OperationId = rejectedId,
            OperationPayloadDigest = rejectedDigest,
            State = (OperationLifecycleWireStateV1)4,
            TerminalResult = new ResultV1
            {
                Status = (ResultStatusV1)6,
                Code = "operation.rejected",
                RetryAdvice = (RetryAdviceV1)1
            }
        };
        Assert(controller.TryApplyResult(ResultEnvelope(rejected, rejectedId, rejectedDigest, correlationByte: 61)));
        Assert(controller.Operations.Single(x => x.OperationId == Hex(rejectedId)).State == ViewOperationLifecycleState.Rejected);
        Assert(predictions.Entries.All(x => x.OperationId != Hex(rejectedId)));

        // Envelope OperationContext is part of the stable identity and cannot disagree with payload.
        var mismatchId = Id(43);
        var mismatchDigest = Hash(43);
        controller.Prepare(Draft(descriptorKind, 110, "context", "predicted-context"), mismatchId, mismatchDigest);
        _ = controller.TakeForSubmission(mismatchId);
        var mismatch = new OperationStatusResultV1
        {
            OperationId = mismatchId,
            OperationPayloadDigest = mismatchDigest,
            State = (OperationLifecycleWireStateV1)2
        };
        AssertThrows<InvalidDataException>(() => controller.TryApplyResult(
            ResultEnvelope(mismatch, Id(99), mismatchDigest, correlationByte: 62)));

        // Resync freezes presentation prediction and blocks new world-affecting sends.
        var resyncId = Id(44);
        controller.Prepare(Draft(descriptorKind, 111, "resync", "predicted-resync"), resyncId, Hash(44));
        controller.SetAccessState(ViewMutationAccessState.Resyncing);
        Assert(predictions.Entries.Any(x => x.OperationId == Hex(resyncId) && x.State == PredictionEntryState.Frozen));
        AssertThrows<InvalidOperationException>(() => controller.TakeForSubmission(resyncId));
        InstallFull(publicationConsumer, basisStep: 109, tokenByte: 22);
        Assert(predictions.Entries.All(x => x.OperationId != Hex(resyncId)));

        // Session revoke is also fail-closed for new sends.
        controller.SetAccessState(ViewMutationAccessState.Ready);
        var revokedId = Id(45);
        controller.Prepare(Draft(descriptorKind, 112, "revoked", string.Empty), revokedId, Hash(45));
        controller.SetAccessState(ViewMutationAccessState.SessionRevoked);
        AssertThrows<InvalidOperationException>(() => controller.TakeForSubmission(revokedId));

        Console.WriteLine("VIEW-04 prediction/reconciliation/Operation smoke checks passed.");
    }

    private static ViewOperationDraft Draft(
        string operationKind,
        ulong candidateStep,
        string payload,
        string predicted)
        => new(
            OperationKind: operationKind,
            AdmissionBasisStep: 100,
            SchedulingPolicyGeneration: 4,
            RequestedNotBeforeStep: 102,
            RequestedDeadlineStep: 180,
            CandidateStep: candidateStep,
            PayloadSchemaId: "fixture.view-action.v1",
            PayloadSchemaMajor: 1,
            PayloadSchemaMinor: 0,
            Payload: ByteString.CopyFromUtf8(payload),
            SemanticTarget: "resident:fixture-1",
            PredictedPayload: ByteString.CopyFromUtf8(predicted));

    private static void InstallFull(PublicationConsumer consumer, ulong basisStep, byte tokenByte)
    {
        var publication = new StatePublicationV1
        {
            PublicationId = Id(tokenByte),
            Kind = (PublicationKindV1)1,
            StateContinuityToken = Hash(tokenByte),
            ChunkCount = 1,
            ProjectionSchemaDigest = Hash(90)
        };
        var chunkPayload = new ProjectionChunkPayloadV1
        {
            SubscriptionId = Id(91),
            PublicationId = publication.PublicationId,
            ChunkIndex = 0
        };
        var payloadBytes = chunkPayload.ToByteArray();
        var chunk = new StatePublicationChunkV1
        {
            PublicationId = publication.PublicationId,
            ChunkIndex = 0,
            ChunkCount = 1,
            UncompressedPayloadDigest = ByteString.CopyFrom(SHA256.HashData(payloadBytes)),
            Compression = (CompressionKindV1)1,
            Payload = ByteString.CopyFrom(payloadBytes)
        };
        _ = consumer.Consume(publication, basisStep, [chunk]);
    }

    private static WireEnvelopeV1 ResultEnvelope(
        OperationStatusResultV1 result,
        ByteString contextOperationId,
        ByteString contextDigest,
        byte correlationByte)
    {
        var envelope = new WireEnvelopeV1
        {
            EnvelopeVersion = 1,
            ProtocolId = GatewayEnvelopeCodec.ProtocolId,
            ProtocolVersion = new ProtocolVersionV1 { Major = 1, Minor = 0 },
            NegotiationGeneration = 1,
            MessageType = "operation.result",
            MessageId = Id((byte)(correlationByte + 10)),
            CorrelationId = Id(correlationByte),
            SenderInstanceId = Id(70),
            OperationContext = new OperationContextWireV1
            {
                OperationId = contextOperationId,
                OperationPayloadDigest = contextDigest
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
        if (!condition) throw new InvalidOperationException("VIEW-04 smoke assertion failed.");
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
