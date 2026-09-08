using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.State;

public enum GatewaySyncState
{
    Starting,
    Synced,
    Suspect,
    Resyncing
}

public sealed class ResyncCoordinator(ConfirmedProjectionCache cache)
{
    private GatewaySyncState _state = GatewaySyncState.Starting;
    private string? _reason;

    public GatewaySyncState State => _state;
    public string? Reason => _reason;
    public bool AllowsWorldAffectingAdmission => _state == GatewaySyncState.Synced && cache.Current is not null;

    public void MarkSynced()
    {
        _state = GatewaySyncState.Synced;
        _reason = null;
    }

    public void MarkSuspect(string reason)
    {
        _state = GatewaySyncState.Suspect;
        _reason = reason;
    }

    public StateResyncRequestV1 BeginResync(ByteString worldId, bool forceFull)
    {
        if (worldId.Length != 16 || worldId.Span.ToArray().All(static b => b == 0))
            throw new InvalidDataException("protocol.invalid-world-id");

        _state = GatewaySyncState.Resyncing;
        var request = new StateResyncRequestV1
        {
            WorldId = worldId,
            Preference = (ResyncPreferenceV1)(forceFull ? 2 : 1)
        };
        if (!forceFull && cache.Current is { } current)
        {
            request.ClientBasisStep = current.BasisStep;
            request.ClientContinuityToken = ByteString.CopyFrom(current.ContinuityToken);
        }
        return request;
    }

    public ConfirmedStateSnapshot ApplyOrEnterSuspect(
        StatePublicationV1 publication,
        ulong basisStep,
        IReadOnlyCollection<StatePublicationChunkV1> chunks)
    {
        try
        {
            var snapshot = cache.Apply(publication, basisStep, chunks);
            MarkSynced();
            return snapshot;
        }
        catch (ContinuityMismatchException ex)
        {
            MarkSuspect(ex.Message);
            throw;
        }
    }
}
