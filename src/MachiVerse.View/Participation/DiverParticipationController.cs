using Google.Protobuf;
using MachiVerse.View.Operations;

namespace MachiVerse.View.Participation;

public enum ParticipationUxState
{
    SessionUnavailable,
    RefreshRequired,
    ReadOnly,
    EligibleUnbound,
    Active,
    ResidentDeceased,
    Pending
}

public sealed class DiverParticipationController : IDisposable
{
    private readonly ViewSessionProjectionStore _session;
    private readonly ParticipationBindingProjectionStore _binding;
    private readonly ParticipationPreferenceCatalog _preferences;
    private readonly AbsencePolicyProfileCatalog _absencePolicies;
    private readonly IParticipationOperationPayloadAdapter _payloadAdapter;
    private readonly ViewOperationController _operations;

    public DiverParticipationController(
        ViewSessionProjectionStore session,
        ParticipationBindingProjectionStore binding,
        ParticipationPreferenceCatalog preferences,
        AbsencePolicyProfileCatalog absencePolicies,
        IParticipationOperationPayloadAdapter payloadAdapter,
        ViewOperationController operations)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _absencePolicies = absencePolicies ?? throw new ArgumentNullException(nameof(absencePolicies));
        _payloadAdapter = payloadAdapter ?? throw new ArgumentNullException(nameof(payloadAdapter));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _session.Changed += OnSessionChanged;
        _binding.Changed += OnBindingChanged;
        _operations.Changed += OnOperationChanged;
    }

    public ParticipationDraftState Draft { get; private set; } = ParticipationDraftState.Empty;

    public ParticipationUxState State
    {
        get
        {
            if (_session.Snapshot.State != ViewSessionAccessState.Active)
                return ParticipationUxState.SessionUnavailable;
            if (_binding.Snapshot.Freshness != ParticipationProjectionFreshness.Confirmed)
                return ParticipationUxState.RefreshRequired;
            if (Draft.PendingOperationId is not null)
                return ParticipationUxState.Pending;
            if (_binding.Snapshot.State == ParticipationBindingState.ResidentDeceased)
                return ParticipationUxState.ResidentDeceased;
            if (_binding.Snapshot.CanTreatAsCurrentControl)
                return ParticipationUxState.Active;
            return CanRequestBinding
                ? ParticipationUxState.EligibleUnbound
                : ParticipationUxState.ReadOnly;
        }
    }

    public bool CanRequestBinding
        => _session.Snapshot.HasPermission(ViewPermissionTokens.ParticipationBind)
            && _binding.Snapshot.Freshness == ParticipationProjectionFreshness.Confirmed
            && _binding.Snapshot.State is not ParticipationBindingState.Active;

    public bool CanReleaseOrRebind
        => _session.Snapshot.HasPermission(ViewPermissionTokens.ParticipationBind)
            && _binding.Snapshot.CanTreatAsCurrentControl;

    public bool CanEditAbsencePolicy
        => _session.Snapshot.HasPermission(ViewPermissionTokens.ParticipationPolicyWrite);

    public bool CanSubmitControl
        => _session.Snapshot.HasPermission(ViewPermissionTokens.OperationDiver)
            && _binding.Snapshot.CanTreatAsCurrentControl;

    public event Action? Changed;

    public void SetJoinPreference(string profileId, IEnumerable<string> preferenceTokens)
    {
        var profile = _preferences.Require(profileId);
        var tokens = preferenceTokens?.ToArray()
            ?? throw new ArgumentNullException(nameof(preferenceTokens));
        ParticipationPreferenceCatalog.ValidateOrderedTokens(tokens, "selected preference token");
        foreach (var token in tokens)
        {
            if (!profile.AllowedPreferenceTokens.BinarySearchOrdinal(token))
                throw new InvalidOperationException($"Preference token '{token}' is not allowed by profile '{profileId}'.");
        }
        Draft = Draft with
        {
            JoinPreference = new JoinPreferenceDraft(profileId, tokens),
            LastLocalReasonCode = null
        };
        Changed?.Invoke();
    }

    public void SetAbsencePolicyProfile(string profileId)
    {
        _absencePolicies.Require(profileId);
        Draft = Draft with
        {
            AbsencePolicy = new AbsencePolicyDraft(profileId),
            LastLocalReasonCode = null
        };
        Changed?.Invoke();
    }

    public void ClearLocalDraft()
    {
        Draft = ParticipationDraftState.Empty;
        Changed?.Invoke();
    }

    public void BeginReconnect()
    {
        _binding.MarkRefreshRequired("participation.binding-refresh-required");
        Changed?.Invoke();
    }

    public TrackedViewOperation PrepareOperation(
        ParticipationOperationIntentKind kind,
        ParticipationSchedulingContext scheduling,
        ByteString operationId,
        ByteString immutablePayloadDigest,
        ByteString? controlPayload = null)
    {
        RequireLocalEligibility(kind);
        if (scheduling.SchedulingPolicyGeneration == 0)
            throw new InvalidDataException("Participation scheduling policy generation must be non-zero.");

        var intent = new ParticipationOperationIntent(
            kind,
            Draft.JoinPreference,
            Draft.AbsencePolicy,
            _binding.Snapshot,
            controlPayload);
        if (!_payloadAdapter.TryEncode(intent, out var encoded, out var reasonCode) || encoded is null)
        {
            Draft = Draft with { LastLocalReasonCode = reasonCode };
            Changed?.Invoke();
            throw new InvalidOperationException(reasonCode);
        }

        ValidateEncodedIntent(kind, encoded);
        var operationDraft = new ViewOperationDraft(
            encoded.OperationKind,
            scheduling.AdmissionBasisStep,
            scheduling.SchedulingPolicyGeneration,
            scheduling.RequestedNotBeforeStep,
            scheduling.RequestedDeadlineStep,
            scheduling.CandidateStep,
            encoded.PayloadSchemaId,
            encoded.PayloadSchemaMajor,
            encoded.PayloadSchemaMinor,
            encoded.Payload,
            encoded.SemanticTarget,
            encoded.PredictedPayload);

        var tracked = _operations.Prepare(operationDraft, operationId, immutablePayloadDigest);
        Draft = Draft with
        {
            PendingOperationId = tracked.OperationId,
            LastLocalReasonCode = null
        };
        Changed?.Invoke();
        return tracked;
    }

    private void RequireLocalEligibility(ParticipationOperationIntentKind kind)
    {
        var allowed = kind switch
        {
            ParticipationOperationIntentKind.BindingCreate => CanRequestBinding,
            ParticipationOperationIntentKind.BindingRelease => CanReleaseOrRebind,
            ParticipationOperationIntentKind.BindingRebind => CanReleaseOrRebind,
            ParticipationOperationIntentKind.AbsencePolicySet => CanEditAbsencePolicy,
            ParticipationOperationIntentKind.ControlSubmit => CanSubmitControl,
            _ => false
        };
        if (!allowed)
            throw new InvalidOperationException("participation.local-eligibility-denied");
    }

    private static void ValidateEncodedIntent(
        ParticipationOperationIntentKind kind,
        EncodedParticipationOperation encoded)
    {
        var expectedKind = kind switch
        {
            ParticipationOperationIntentKind.BindingCreate => "participation.binding.create",
            ParticipationOperationIntentKind.BindingRelease => "participation.binding.release",
            ParticipationOperationIntentKind.BindingRebind => "participation.binding.rebind",
            ParticipationOperationIntentKind.AbsencePolicySet => "participation.absence-policy.set",
            ParticipationOperationIntentKind.ControlSubmit => "participation.control.submit",
            _ => throw new InvalidDataException("Unsupported Participation Operation intent.")
        };
        var expectedSchema = $"operation.{expectedKind}";
        if (!string.Equals(encoded.OperationKind, expectedKind, StringComparison.Ordinal)
            || !string.Equals(encoded.PayloadSchemaId, expectedSchema, StringComparison.Ordinal)
            || encoded.PayloadSchemaMajor != 1
            || encoded.PayloadSchemaMinor != 0)
        {
            throw new InvalidDataException("Participation payload adapter returned an incompatible canonical Operation identity/schema.");
        }
        if (string.IsNullOrWhiteSpace(encoded.SemanticTarget))
            throw new InvalidDataException("Participation Operation semantic target must be non-empty.");
    }

    private void OnSessionChanged(ViewSessionProjection _)
    {
        if (_session.Snapshot.State != ViewSessionAccessState.Active)
        {
            // Session loss stops new input but never invents a binding release Operation.
            _operations.SetAccessState(_session.Snapshot.State switch
            {
                ViewSessionAccessState.Revoked => ViewMutationAccessState.SessionRevoked,
                ViewSessionAccessState.Expired => ViewMutationAccessState.SessionRevoked,
                _ => ViewMutationAccessState.Blocked
            }, _session.Snapshot.ReasonCode);
        }
        Changed?.Invoke();
    }

    private void OnBindingChanged(ParticipationBindingProjection _)
        => Changed?.Invoke();

    private void OnOperationChanged()
    {
        if (Draft.PendingOperationId is { } pending)
        {
            var operation = _operations.Operations.FirstOrDefault(x => x.OperationId == pending);
            if (operation is not null
                && operation.State is ViewOperationLifecycleState.Terminal
                    or ViewOperationLifecycleState.Rejected
                    or ViewOperationLifecycleState.Failed)
            {
                Draft = Draft with
                {
                    PendingOperationId = null,
                    LastLocalReasonCode = operation.TerminalResult?.Code ?? operation.LastReasonCode
                };
            }
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _session.Changed -= OnSessionChanged;
        _binding.Changed -= OnBindingChanged;
        _operations.Changed -= OnOperationChanged;
    }
}
