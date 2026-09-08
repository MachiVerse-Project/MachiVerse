using Google.Protobuf;
using MachiVerse.Protocol.V1;

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
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsLowerAlphaNumeric(value[0]))
            throw new InvalidDataException($"{field} must use StableToken grammar.");
        for (var i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!IsLowerAlphaNumeric(ch) && ch is not ('.' or '_' or '/' or '-'))
                throw new InvalidDataException($"{field} must use StableToken grammar.");
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

    private static bool IsLowerAlphaNumeric(char value)
        => value is >= 'a' and <= 'z' or >= '0' and <= '9';
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

/// <summary>
/// View-owned semantic payload adapter for the currently standardized Alpha binding-create operation.
/// OperationId/digest are intentionally filled by DiverParticipationController only after scheduling
/// context is known, so immutable identity remains canonical and non-self-referential.
/// </summary>
public sealed class CanonicalParticipationOperationPayloadAdapter(ViewSessionProjectionStore session)
    : IParticipationOperationPayloadAdapter
{
    private readonly ViewSessionProjectionStore _session = session ?? throw new ArgumentNullException(nameof(session));

    public bool TryEncode(
        ParticipationOperationIntent intent,
        out EncodedParticipationOperation? operation,
        out string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Kind != ParticipationOperationIntentKind.BindingCreate)
        {
            operation = null;
            reasonCode = "participation.operation-kind-unavailable";
            return false;
        }
        if (_session.Snapshot.State != ViewSessionAccessState.Active || string.IsNullOrEmpty(_session.Snapshot.DiverRef))
        {
            operation = null;
            reasonCode = "auth.unauthenticated";
            return false;
        }
        if (intent.ConfirmedBinding.Freshness != ParticipationProjectionFreshness.Confirmed ||
            intent.ConfirmedBinding.State != ParticipationBindingState.None)
        {
            operation = null;
            reasonCode = "participation.binding-create-not-eligible";
            return false;
        }
        if (string.IsNullOrEmpty(intent.JoinPreference.ProfileId))
        {
            operation = null;
            reasonCode = "participation.preference-profile-required";
            return false;
        }

        ParticipationPreferenceCatalog.ValidateToken(intent.JoinPreference.ProfileId, "preference profile");
        ParticipationPreferenceCatalog.ValidateOrderedTokens(intent.JoinPreference.PreferenceTokens, "preference token");
        var payload = new ParticipationBindingRequestV1
        {
            PreferenceProfile = intent.JoinPreference.ProfileId,
            DiverRef = ByteString.CopyFrom(Convert.FromHexString(_session.Snapshot.DiverRef)),
            ExpectedBindingGeneration = intent.ConfirmedBinding.BindingGeneration,
        };
        payload.PreferenceTokens.AddRange(intent.JoinPreference.PreferenceTokens);

        operation = new EncodedParticipationOperation(
            ViewParticipationBindingIdentityV1.OperationKind,
            ViewParticipationBindingIdentityV1.PayloadSchemaId,
            ViewParticipationBindingIdentityV1.PayloadSchemaMajor,
            ViewParticipationBindingIdentityV1.PayloadSchemaMinor,
            payload.ToByteString(),
            "participation.binding",
            ByteString.Empty);
        reasonCode = string.Empty;
        return true;
    }
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
