using System.Runtime.CompilerServices;
using Google.Protobuf;
using MachiVerse.Administration.View.Modules.Management;
using MachiVerse.Protocol.V1;

internal static class Admin03IdentitySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var target = new ComponentTargetV1 { ComponentKind = (ComponentKindV1)2 };
        var projection = new ConfigTargetProjection(
            TargetKey: target.ComponentKind.ToString(),
            ComponentKind: target.ComponentKind.ToString(),
            LogicalInstanceId: null,
            ConfigGeneration: 3,
            ConfigDigest: new string('b', 64),
            Entries:
            [
                new ConfigEntryProjection(
                    "observability.log-level",
                    "{ \"stringValue\": \"info\" }",
                    "operational",
                    "runtime-safe",
                    Sensitive: false,
                    Redacted: false),
            ],
            Result: new ManagementResultProjection(1, "Success", "ok", 1, "DoNotRetry", string.Empty));

        var descriptor = new CommandDescriptor(
            "fixture.identity-command",
            [(ComponentKindV1)2],
            "fixture.command.v1",
            1,
            0,
            "admin.command.execute.low-impact",
            "low-impact",
            StateChanging: true);
        var management = new ManagementProjectionStore(new OperationalCommandCatalog([descriptor]));

        var operationId = Id(41);
        var digest = Hash(42);
        var firstDraft = management.CreateDraft(projection).WithEdits([
            new ConfigChangeEdit("observability.log-level", new ConfigValueWireV1 { StringValue = "debug" }),
        ]);
        management.PrepareConfigChange(firstDraft, operationId, digest);
        management.PrepareConfigChange(firstDraft, operationId, digest); // exact retry preparation is idempotent.

        var changedDraft = management.CreateDraft(projection).WithEdits([
            new ConfigChangeEdit("observability.log-level", new ConfigValueWireV1 { StringValue = "warn" }),
        ]);
        AssertThrows<InvalidDataException>(() => management.PrepareConfigChange(changedDraft, operationId, digest));

        var success = new ConfigChangeResultV1
        {
            Result = new ResultV1
            {
                Status = (ResultStatusV1)1,
                Code = "ok",
                RetryAdvice = (RetryAdviceV1)1,
            },
            ResultingGeneration = 4,
            ResultingConfigDigest = Hash(43),
        };
        Assert(management.TryApply(MutationEnvelope(
            "config.change.result",
            "protocol.config-change-result.v1",
            success,
            operationId,
            digest)));
        var trackedConfig = management.Snapshot.Mutations.Single(x => x.OperationId == Hex(operationId));
        Assert(trackedConfig.State == ManagementMutationState.Terminal);
        Assert(trackedConfig.ResultingGeneration == 4);

        var missingDigestOperationId = Id(44);
        var missingDigestIdentity = Hash(45);
        management.PrepareConfigChange(firstDraft, missingDigestOperationId, missingDigestIdentity);
        var malformedSuccess = new ConfigChangeResultV1
        {
            Result = new ResultV1 { Status = (ResultStatusV1)1, Code = "ok" },
            ResultingGeneration = 4,
        };
        AssertThrows<InvalidDataException>(() => management.TryApply(MutationEnvelope(
            "config.change.result",
            "protocol.config-change-result.v1",
            malformedSuccess,
            missingDigestOperationId,
            missingDigestIdentity)));

        var commandOperationId = Id(51);
        var commandDigest = Hash(52);
        management.PrepareOperationalCommand(
            descriptor.CommandKind,
            target,
            ByteString.CopyFromUtf8("payload-a"),
            commandOperationId,
            commandDigest);
        management.PrepareOperationalCommand(
            descriptor.CommandKind,
            target,
            ByteString.CopyFromUtf8("payload-a"),
            commandOperationId,
            commandDigest);
        AssertThrows<InvalidDataException>(() => management.PrepareOperationalCommand(
            descriptor.CommandKind,
            target,
            ByteString.CopyFromUtf8("payload-b"),
            commandOperationId,
            commandDigest));

        var unauthorized = new OperationStatusResultV1
        {
            OperationId = commandOperationId,
            OperationPayloadDigest = commandDigest,
            State = (OperationLifecycleWireStateV1)4,
            TerminalResult = new ResultV1
            {
                Status = (ResultStatusV1)6,
                Code = "auth.unauthorized",
                RetryAdvice = (RetryAdviceV1)1,
            },
        };
        Assert(management.TryApply(Envelope("operation.result", "protocol.operation-status-result.v1", unauthorized)));
        Assert(management.Snapshot.CommandChannel.State == ManagementAccessState.Unauthorized);
    }

    private static WireEnvelopeV1 Envelope(string type, string schema, IMessage payload)
        => new()
        {
            MessageType = type,
            PayloadSchemaId = schema,
            Payload = payload.ToByteString(),
        };

    private static WireEnvelopeV1 MutationEnvelope(
        string type,
        string schema,
        IMessage payload,
        ByteString operationId,
        ByteString digest)
    {
        var envelope = Envelope(type, schema, payload);
        envelope.OperationContext = new OperationContextWireV1
        {
            OperationId = operationId,
            OperationPayloadDigest = digest,
        };
        return envelope;
    }

    private static ByteString Id(byte first)
    {
        var bytes = new byte[16];
        bytes[0] = first;
        return ByteString.CopyFrom(bytes);
    }

    private static ByteString Hash(byte first)
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
            throw new InvalidOperationException("ADMIN-03 identity smoke assertion failed.");
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
