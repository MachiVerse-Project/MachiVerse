using System.Runtime.CompilerServices;
using Google.Protobuf;
using MachiVerse.Gateway.State;
using MachiVerse.Protocol.V1;

internal static class Int02CustodyConvergenceSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var sourceGateway = Id(0x41);
        var initialMaster = Id(0x42);
        var failoverMaster = Id(0x43);
        var operationId = Id(0x44);
        var unknownOperationId = Id(0x45);
        var digest = Hash(0x46);

        var authority = new MasterAuthorityTracker(sourceGateway);
        authority.Apply(new MasterGenerationStateV1
        {
            MasterGeneration = 7,
            CurrentMasterGatewayId = initialMaster,
        });

        var ledger = new OperationCustodyLedger();
        var held = ledger.HoldSource(operationId, digest);
        Require(held.State == GatewayCustodyState.SourceHeld, "INT-02 fixture must begin SOURCE_HELD.");

        var unknownAck = new GatewayBatchAckV1
        {
            BatchId = Id(0x47),
            BatchDigest = Hash(0x48),
            BatchStatus = (BatchWireStatusV1)1,
        };
        unknownAck.Entries.Add(new BatchEntryAckV1
        {
            OperationId = unknownOperationId,
            CustodyState = (GatewayCustodyWireStateV1)2,
        });
        RequireReject(
            () => ledger.ApplyMasterAck(unknownAck, 7, initialMaster, authority),
            "custody.unknown-operation");
        Require(ledger.Records.Single().State == GatewayCustodyState.SourceHeld,
            "Unknown Master ACK must not mutate an unrelated held Operation.");

        authority.Apply(new MasterGenerationStateV1
        {
            MasterGeneration = 8,
            CurrentMasterGatewayId = failoverMaster,
        });
        var candidates = ledger.GetFailoverConvergenceCandidates();
        Require(candidates.Count == 1 && candidates[0].OperationId.AsSpan().SequenceEqual(operationId.Span),
            "Failover must retain SOURCE_HELD identity for authoritative convergence.");

        var unknown = ledger.ApplyCoreStatus(new OperationStatusResultV1
        {
            OperationId = operationId,
            State = (OperationLifecycleWireStateV1)1,
        }, observedMasterGeneration: 8);
        Require(unknown.State == GatewayCustodyState.SourceHeld && unknown.LastObservedMasterGeneration == 8,
            "Core UNKNOWN status must retain source custody and advance observed generation only.");

        var accepted = ledger.ApplyCoreStatus(new OperationStatusResultV1
        {
            OperationId = operationId,
            State = (OperationLifecycleWireStateV1)2,
            OperationPayloadDigest = digest,
        }, observedMasterGeneration: 8);
        Require(accepted.State == GatewayCustodyState.CoreAccepted && !accepted.NeedsAuthoritativeConvergence,
            "Authoritative ACCEPTED status must converge custody without changing Operation identity.");

        var terminal = ledger.ApplyCoreStatus(new OperationStatusResultV1
        {
            OperationId = operationId,
            State = (OperationLifecycleWireStateV1)4,
            OperationPayloadDigest = digest,
            TerminalResult = new ResultV1
            {
                Status = (ResultStatusV1)1,
                Code = "int02.ok",
                RetryAdvice = (RetryAdviceV1)1,
            },
        }, observedMasterGeneration: 8);
        Require(terminal.State == GatewayCustodyState.Terminal && terminal.TerminalResult?.Code == "int02.ok",
            "Authoritative terminal status must converge to one retained terminal result.");

        RequireReject(
            () => ledger.ApplyCoreStatus(new OperationStatusResultV1
            {
                OperationId = unknownOperationId,
                State = (OperationLifecycleWireStateV1)1,
            }, observedMasterGeneration: 8),
            "custody.unknown-operation");
    }

    private static ByteString Id(byte marker) => ByteString.CopyFrom(Enumerable.Repeat(marker, 16).ToArray());
    private static ByteString Hash(byte marker) => ByteString.CopyFrom(Enumerable.Repeat(marker, 32).ToArray());

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireReject(Action action, string expected)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expected)
        {
            return;
        }
        throw new InvalidOperationException($"Expected INT-02 rejection: {expected}");
    }
}
