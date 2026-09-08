using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.View.Participation;

public enum ParticipationBindingState
{
    Unknown,
    None,
    Active,
    ResidentDeceased,
    Released,
    Superseded
}

public enum ParticipationProjectionFreshness
{
    Unknown,
    Confirmed,
    RefreshRequired
}

public sealed record ParticipationBindingProjection(
    ParticipationBindingState State,
    ParticipationProjectionFreshness Freshness,
    string? BindingId,
    string? ResidentId,
    string? DiverRef,
    ulong BindingGeneration,
    ulong? EffectiveFromStep,
    string? AbsencePolicyProfile,
    ulong? BasisStep,
    string? ReasonCode)
{
    public bool CanTreatAsCurrentControl
        => Freshness == ParticipationProjectionFreshness.Confirmed
            && State == ParticipationBindingState.Active;
}

public sealed class ParticipationBindingProjectionStore
{
    public ParticipationBindingProjection Snapshot { get; private set; } = new(
        ParticipationBindingState.Unknown,
        ParticipationProjectionFreshness.Unknown,
        BindingId: null,
        ResidentId: null,
        DiverRef: null,
        BindingGeneration: 0,
        EffectiveFromStep: null,
        AbsencePolicyProfile: null,
        BasisStep: null,
        ReasonCode: "participation.binding-unknown");

    public event Action<ParticipationBindingProjection>? Changed;

    public bool TryApply(WireEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(envelope.MessageType, "participation.binding.state", StringComparison.Ordinal))
            return false;
        if (!string.Equals(envelope.PayloadSchemaId, "protocol.participation-binding-view.v1", StringComparison.Ordinal)
            || envelope.PayloadSchemaVersion is null
            || envelope.PayloadSchemaVersion.Major != 1)
        {
            throw new InvalidDataException("participation.binding.state payload schema mismatch.");
        }
        if (envelope.WorldContext is null || !envelope.WorldContext.HasBasisStep)
            throw new InvalidDataException("participation.binding.state requires world_context.basis_step.");

        ParticipationBindingViewV1 wire;
        try
        {
            wire = ParticipationBindingViewV1.Parser.ParseFrom(envelope.Payload);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new InvalidDataException("participation.binding.state structural decode failed.", ex);
        }
        Apply(wire, envelope.WorldContext.BasisStep);
        return true;
    }

    public void Apply(ParticipationBindingViewV1 wire, ulong basisStep)
    {
        ArgumentNullException.ThrowIfNull(wire);
        if (wire.HasBindingId) ValidateId128(wire.BindingId, nameof(wire.BindingId));
        if (wire.HasResidentId) ValidateId128(wire.ResidentId, nameof(wire.ResidentId));
        if (wire.HasDiverRef) ValidateId128(wire.DiverRef, nameof(wire.DiverRef));
        if (wire.HasAbsencePolicyProfile) ValidateStableToken(wire.AbsencePolicyProfile, nameof(wire.AbsencePolicyProfile));

        var state = (int)wire.Status switch
        {
            1 => ParticipationBindingState.None,
            2 => ParticipationBindingState.Active,
            3 => ParticipationBindingState.ResidentDeceased,
            4 => ParticipationBindingState.Released,
            5 => ParticipationBindingState.Superseded,
            _ => throw new InvalidDataException("Participation binding status is unspecified or unsupported.")
        };

        if (state == ParticipationBindingState.None)
        {
            if (wire.BindingGeneration != 0 || wire.HasBindingId || wire.HasResidentId || wire.HasDiverRef || wire.HasEffectiveFromStep)
                throw new InvalidDataException("Confirmed NONE binding has an invalid authoritative shape.");
        }
        else if (state == ParticipationBindingState.Active)
        {
            if (!wire.HasBindingId || !wire.HasResidentId || !wire.HasDiverRef || !wire.HasEffectiveFromStep || wire.BindingGeneration == 0)
                throw new InvalidDataException("Confirmed ACTIVE binding is missing authoritative identity/generation fields.");
        }

        if (Snapshot.Freshness == ParticipationProjectionFreshness.Confirmed &&
            Snapshot.BasisStep is { } previousBasis && basisStep >= previousBasis &&
            wire.BindingGeneration < Snapshot.BindingGeneration)
        {
            throw new InvalidDataException("Participation binding generation regressed within confirmed continuity.");
        }

        Snapshot = new ParticipationBindingProjection(
            state,
            ParticipationProjectionFreshness.Confirmed,
            wire.HasBindingId ? Hex(wire.BindingId) : null,
            wire.HasResidentId ? Hex(wire.ResidentId) : null,
            wire.HasDiverRef ? Hex(wire.DiverRef) : null,
            wire.BindingGeneration,
            wire.HasEffectiveFromStep ? wire.EffectiveFromStep : null,
            wire.HasAbsencePolicyProfile ? wire.AbsencePolicyProfile : null,
            basisStep,
            state switch
            {
                ParticipationBindingState.ResidentDeceased => "participation.binding.resident-deceased",
                ParticipationBindingState.Released => "participation.binding.released",
                ParticipationBindingState.Superseded => "participation.binding.superseded",
                _ => null
            });
        Changed?.Invoke(Snapshot);
    }

    public void MarkRefreshRequired(string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("Reason code must be non-empty.", nameof(reasonCode));
        Snapshot = Snapshot with
        {
            Freshness = ParticipationProjectionFreshness.RefreshRequired,
            ReasonCode = reasonCode
        };
        Changed?.Invoke(Snapshot);
    }

    private static void ValidateId128(ByteString value, string field)
    {
        if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"{field} must be non-zero Id128.");
    }

    private static void ValidateStableToken(string value, string field)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsLowerAlphaNumeric(value[0]))
            throw new InvalidDataException($"{field} must use StableToken grammar.");
        for (var i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!IsLowerAlphaNumeric(ch) && ch is not ('.' or '_' or '/' or '-'))
                throw new InvalidDataException($"{field} must use StableToken grammar.");
        }
    }

    private static bool IsLowerAlphaNumeric(char value)
        => value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static string Hex(ByteString value) => Convert.ToHexStringLower(value.Span);
}
