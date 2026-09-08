using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.State;

public enum GatewayCustodyState
{
    SourceHeld = 1,
    MasterReceived = 2,
    CoreAccepted = 3,
    Terminal = 4
}

public sealed record OperationCustodyRecord(
    byte[] OperationId,
    byte[] ImmutablePayloadDigest,
    GatewayCustodyState State,
    ulong LastObservedMasterGeneration,
    ResultV1? TerminalResult)
{
    public string OperationIdHex => Convert.ToHexStringLower(OperationId);
    public bool NeedsAuthoritativeConvergence => (int)State < (int)GatewayCustodyState.CoreAccepted;
    public bool RetainIdentity => true;
}

public sealed class OperationCustodyLedger
{
    private readonly Dictionary<string, OperationCustodyRecord> _records = new(StringComparer.Ordinal);

    public IReadOnlyCollection<OperationCustodyRecord> Records => _records.Values.ToArray();

    public OperationCustodyRecord HoldSource(ByteString operationId, ByteString immutablePayloadDigest)
    {
        var id = ValidateId128(operationId, "operation_id");
        var digest = ValidateHash256(immutablePayloadDigest, "immutable_payload_digest");
        var key = Convert.ToHexStringLower(id);

        if (_records.TryGetValue(key, out var existing))
        {
            RequireSameDigest(existing.ImmutablePayloadDigest, digest);
            return existing;
        }

        var created = new OperationCustodyRecord(id, digest, GatewayCustodyState.SourceHeld, 0, null);
        _records.Add(key, created);
        return created;
    }

    public IReadOnlyList<OperationCustodyRecord> ApplyMasterAck(
        GatewayBatchAckV1 ack,
        ulong masterGeneration,
        ByteString senderMasterGatewayId,
        MasterAuthorityTracker authority)
    {
        ArgumentNullException.ThrowIfNull(ack);
        ArgumentNullException.ThrowIfNull(authority);
        authority.RequireCurrentMaster(masterGeneration, senderMasterGatewayId);
        ValidateId128(ack.BatchId, "batch_id");
        ValidateHash256(ack.BatchDigest, "batch_digest");
        if ((int)ack.BatchStatus is < 1 or > 4)
            throw new InvalidDataException("protocol.invalid-batch-status");

        var updated = new List<OperationCustodyRecord>(ack.Entries.Count);
        foreach (var entry in ack.Entries)
        {
            var id = ValidateId128(entry.OperationId, "operation_id");
            var key = Convert.ToHexStringLower(id);
            if (!_records.TryGetValue(key, out var current))
                throw new InvalidDataException("custody.unknown-operation");

            var target = (int)entry.CustodyState switch
            {
                1 => GatewayCustodyState.SourceHeld,
                2 => GatewayCustodyState.MasterReceived,
                3 => GatewayCustodyState.CoreAccepted,
                4 => GatewayCustodyState.Terminal,
                _ => throw new InvalidDataException("custody.invalid-state")
            };
            if (target == GatewayCustodyState.Terminal && entry.Result is null)
                throw new InvalidDataException("custody.terminal-result-missing");

            var next = Advance(current, target, masterGeneration, entry.Result);
            _records[key] = next;
            updated.Add(next);
        }
        return updated;
    }

    public OperationCustodyRecord ApplyCoreStatus(OperationStatusResultV1 status, ulong observedMasterGeneration)
    {
        ArgumentNullException.ThrowIfNull(status);
        var id = ValidateId128(status.OperationId, "operation_id");
        var key = Convert.ToHexStringLower(id);
        if (!_records.TryGetValue(key, out var current))
            throw new InvalidDataException("custody.unknown-operation");

        if (status.HasOperationPayloadDigest)
        {
            var digest = ValidateHash256(status.OperationPayloadDigest, "operation_payload_digest");
            RequireSameDigest(current.ImmutablePayloadDigest, digest);
        }

        var target = (int)status.State switch
        {
            1 => current.State,
            2 or 3 => GatewayCustodyState.CoreAccepted,
            4 => GatewayCustodyState.Terminal,
            _ => throw new InvalidDataException("custody.invalid-core-lifecycle")
        };
        if (target == GatewayCustodyState.Terminal && status.TerminalResult is null)
            throw new InvalidDataException("custody.terminal-result-missing");

        var next = Advance(current, target, observedMasterGeneration, status.TerminalResult);
        _records[key] = next;
        return next;
    }

    public IReadOnlyList<OperationCustodyRecord> GetFailoverConvergenceCandidates()
        => _records.Values
            .Where(static record => record.NeedsAuthoritativeConvergence)
            .OrderBy(static record => record.OperationIdHex, StringComparer.Ordinal)
            .ToArray();

    private static OperationCustodyRecord Advance(
        OperationCustodyRecord current,
        GatewayCustodyState target,
        ulong masterGeneration,
        ResultV1? terminalResult)
    {
        if ((int)target < (int)current.State)
            throw new InvalidDataException("custody.state-regression");
        if (current.State == GatewayCustodyState.Terminal && target != GatewayCustodyState.Terminal)
            throw new InvalidDataException("custody.terminal-state-regression");
        if ((int)target < (int)GatewayCustodyState.Terminal && terminalResult is not null)
            throw new InvalidDataException("custody.premature-terminal-result");

        return current with
        {
            State = target,
            LastObservedMasterGeneration = Math.Max(current.LastObservedMasterGeneration, masterGeneration),
            TerminalResult = target == GatewayCustodyState.Terminal ? terminalResult ?? current.TerminalResult : current.TerminalResult
        };
    }

    private static byte[] ValidateId128(ByteString value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
        return value.ToByteArray();
    }

    private static byte[] ValidateHash256(ByteString value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 32) throw new InvalidDataException($"protocol.invalid-hash:{field}");
        return value.ToByteArray();
    }

    private static void RequireSameDigest(byte[] expected, byte[] actual)
    {
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new InvalidDataException("protocol.operation-payload-mismatch");
    }
}
