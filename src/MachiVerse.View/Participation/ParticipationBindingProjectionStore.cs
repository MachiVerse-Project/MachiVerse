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
        if (!envelope.HasBasisStep)
            throw new InvalidDataException("participation.binding.state requires basis_step context.");

        ParticipationBindingViewV1 wire;
        try
        {
            wire = ParticipationBindingViewV1.Parser.ParseFrom(envelope.Payload);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new InvalidDataException("participation.binding.state structural decode failed.", ex);
        }
        Apply(wire, envelope.BasisStep);
        return true;
    }

    public void Apply(ParticipationBindingViewV1 wire, ulong basisStep)
    {
        ArgumentNullException.ThrowIfNull(wire);
        if (wire.HasBindingId) ValidateId128(wire.BindingId, nameof(wire.BindingId));
        if (wire.HasResidentId) ValidateId128(wire.ResidentId, nameof(wire.ResidentId));
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

        Snapshot = new ParticipationBindingProjection(
            state,
            ParticipationProjectionFreshness.Confirmed,
            wire.HasBindingId ? Hex(wire.BindingId) : null,
            wire.HasResidentId ? Hex(wire.ResidentId) : null,
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
        if (value.Length != 16 || value.Span.ToArray().All(static octet => octet == 0))
            throw new InvalidDataException($"{field} must be non-zero Id128.");
    }

    private static void ValidateStableToken(string value, string field)
    {
        if (string.IsNullOrEmpty(value))
            throw new InvalidDataException($"{field} must be non-empty when present.");
        foreach (var ch in value)
        {
            if (ch > 0x7f || char.IsControl(ch) || char.IsWhiteSpace(ch))
                throw new InvalidDataException($"{field} must use canonical ASCII token text.");
        }
    }

    private static string Hex(ByteString value) => Convert.ToHexStringLower(value.Span);
}
