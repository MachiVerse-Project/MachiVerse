using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.State;

public sealed record MasterAuthoritySnapshot(
    ulong MasterGeneration,
    byte[]? CurrentMasterGatewayId);

public sealed class MasterAuthorityTracker
{
    private readonly byte[] _localGatewayId;
    private MasterAuthoritySnapshot? _current;

    public MasterAuthorityTracker(ByteString localGatewayId)
    {
        _localGatewayId = ValidateId128(localGatewayId, "local_gateway_id");
    }

    public MasterAuthoritySnapshot? Current => Volatile.Read(ref _current);

    public bool IsLocalMaster
    {
        get
        {
            var current = Current;
            return current?.CurrentMasterGatewayId is not null &&
                   current.CurrentMasterGatewayId.AsSpan().SequenceEqual(_localGatewayId);
        }
    }

    public MasterAuthoritySnapshot Apply(MasterGenerationStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.MasterGeneration == 0)
            throw new InvalidDataException("master.invalid-generation");

        var masterId = state.HasCurrentMasterGatewayId
            ? ValidateId128(state.CurrentMasterGatewayId, "current_master_gateway_id")
            : null;

        var current = Current;
        if (current is not null)
        {
            if (state.MasterGeneration < current.MasterGeneration)
                throw new InvalidDataException("master.stale-generation");

            if (state.MasterGeneration == current.MasterGeneration)
            {
                if (!SameOptionalId(current.CurrentMasterGatewayId, masterId))
                    throw new InvalidDataException("master.generation-conflict");
                return current;
            }
        }

        var next = new MasterAuthoritySnapshot(state.MasterGeneration, masterId);
        Volatile.Write(ref _current, next);
        return next;
    }

    public void RequireCurrentMaster(ulong masterGeneration, ByteString senderGatewayId)
    {
        var current = Current ?? throw new InvalidDataException("master.authority-unknown");
        if (masterGeneration != current.MasterGeneration)
            throw new InvalidDataException("master.stale-generation");
        if (current.CurrentMasterGatewayId is null)
            throw new InvalidDataException("master.transition-no-authority");

        var sender = ValidateId128(senderGatewayId, "sender_gateway_id");
        if (!sender.AsSpan().SequenceEqual(current.CurrentMasterGatewayId))
            throw new InvalidDataException("master.not-current");
    }

    private static bool SameOptionalId(byte[]? left, byte[]? right)
        => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private static byte[] ValidateId128(ByteString value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 16) throw new InvalidDataException($"protocol.invalid-id:{field}");
        if (value.Span.IndexOfAnyExcept((byte)0) < 0) throw new InvalidDataException($"protocol.invalid-id:{field}");
        return value.ToByteArray();
    }
}
