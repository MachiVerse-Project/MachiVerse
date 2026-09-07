using Google.Protobuf;

namespace MachiVerse.View.Participation;

public sealed record ParticipationPreferenceProfile(
    string ProfileId,
    IReadOnlyList<string> AllowedPreferenceTokens);

public sealed class ParticipationPreferenceCatalog
{
    private readonly IReadOnlyDictionary<string, ParticipationPreferenceProfile> _profiles;

    public static ParticipationPreferenceCatalog Empty { get; } = new(Array.Empty<ParticipationPreferenceProfile>());

    public ParticipationPreferenceCatalog(IEnumerable<ParticipationPreferenceProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var map = new Dictionary<string, ParticipationPreferenceProfile>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ValidateToken(profile.ProfileId, nameof(profile.ProfileId));
            var tokens = profile.AllowedPreferenceTokens.ToArray();
            ValidateOrderedTokens(tokens, "preference token");
            if (!map.TryAdd(profile.ProfileId, profile with { AllowedPreferenceTokens = tokens }))
                throw new InvalidDataException($"Duplicate Participation preference profile '{profile.ProfileId}'.");
        }
        _profiles = map;
    }

    public IReadOnlyList<ParticipationPreferenceProfile> Profiles => _profiles.Values
        .OrderBy(static x => x.ProfileId, StringComparer.Ordinal)
        .ToArray();

    public ParticipationPreferenceProfile Require(string profileId)
        => _profiles.TryGetValue(profileId, out var profile)
            ? profile
            : throw new InvalidOperationException($"Unknown Participation preference profile '{profileId}'.");

    internal static void ValidateToken(string value, string field)
    {
        if (string.IsNullOrEmpty(value))
            throw new InvalidDataException($"{field} must be non-empty.");
        foreach (var ch in value)
        {
            if (ch > 0x7f || char.IsControl(ch) || char.IsWhiteSpace(ch))
                throw new InvalidDataException($"{field} must use canonical ASCII token text.");
        }
    }

    internal static void ValidateOrderedTokens(IReadOnlyList<string> values, string field)
    {
        string? previous = null;
        foreach (var value in values)
        {
            ValidateToken(value, field);
            if (previous is not null && string.CompareOrdinal(previous, value) >= 0)
                throw new InvalidDataException($"{field} values must be ASCII ascending and duplicate-free.");
            previous = value;
        }
    }
}

public sealed class AbsencePolicyProfileCatalog
{
    private readonly IReadOnlyList<string> _profiles;

    public static AbsencePolicyProfileCatalog Empty { get; } = new(Array.Empty<string>());

    public AbsencePolicyProfileCatalog(IEnumerable<string> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var values = profiles.ToArray();
        ParticipationPreferenceCatalog.ValidateOrderedTokens(values, "absence policy profile");
        _profiles = values;
    }

    public IReadOnlyList<string> Profiles => _profiles;

    public string Require(string profile)
        => _profiles.BinarySearchOrdinal(profile)
            ? profile
            : throw new InvalidOperationException($"Unknown absence policy profile '{profile}'.");
}

public sealed record JoinPreferenceDraft(
    string? ProfileId,
    IReadOnlyList<string> PreferenceTokens);

public sealed record AbsencePolicyDraft(string? ProfileId);

public sealed record ParticipationDraftState(
    JoinPreferenceDraft JoinPreference,
    AbsencePolicyDraft AbsencePolicy,
    string? PendingOperationId,
    string? LastLocalReasonCode)
{
    public static ParticipationDraftState Empty { get; } = new(
        new JoinPreferenceDraft(null, Array.Empty<string>()),
        new AbsencePolicyDraft(null),
        PendingOperationId: null,
        LastLocalReasonCode: null);
}

public enum ParticipationOperationIntentKind
{
    BindingCreate,
    BindingRelease,
    BindingRebind,
    AbsencePolicySet,
    ControlSubmit
}

public sealed record ParticipationOperationIntent(
    ParticipationOperationIntentKind Kind,
    JoinPreferenceDraft JoinPreference,
    AbsencePolicyDraft AbsencePolicy,
    ParticipationBindingProjection ConfirmedBinding,
    ByteString? ControlPayload = null);

public sealed record EncodedParticipationOperation(
    string OperationKind,
    string PayloadSchemaId,
    uint PayloadSchemaMajor,
    uint PayloadSchemaMinor,
    ByteString Payload,
    string SemanticTarget,
    ByteString PredictedPayload);

public interface IParticipationOperationPayloadAdapter
{
    bool TryEncode(
        ParticipationOperationIntent intent,
        out EncodedParticipationOperation? operation,
        out string reasonCode);
}

public sealed class UnavailableParticipationOperationPayloadAdapter : IParticipationOperationPayloadAdapter
{
    public bool TryEncode(
        ParticipationOperationIntent intent,
        out EncodedParticipationOperation? operation,
        out string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(intent);
        operation = null;
        reasonCode = "participation.operation-codec-unavailable";
        return false;
    }
}

public sealed record ParticipationSchedulingContext(
    ulong AdmissionBasisStep,
    ulong SchedulingPolicyGeneration,
    ulong? RequestedNotBeforeStep,
    ulong? RequestedDeadlineStep,
    ulong? CandidateStep);
