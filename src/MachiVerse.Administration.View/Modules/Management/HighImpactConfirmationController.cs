using MachiVerse.Administration.View.Configuration;

namespace MachiVerse.Administration.View.Modules.Management;

public sealed class HighImpactConfirmationController
{
    private readonly TimeSpan _timeout;
    private readonly Func<DateTimeOffset> _utcNow;
    private HighImpactConfirmationSnapshot _snapshot = new(
        HighImpactConfirmationState.NotRequired,
        Fingerprint: null,
        SessionGeneration: 0,
        ExpiresAt: null,
        Consumed: false,
        ReasonCode: null);
    private LocalConfirmationEvidence? _evidence;

    public HighImpactConfirmationController(AdminViewConfig config)
        : this(config, static () => DateTimeOffset.UtcNow)
    {
    }

    public HighImpactConfirmationController(AdminViewConfig config, Func<DateTimeOffset> utcNow)
    {
        ArgumentNullException.ThrowIfNull(config);
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _timeout = TimeSpan.FromSeconds(config.ConfirmationUxTimeoutSeconds);
    }

    public event Action? Changed;

    public HighImpactConfirmationSnapshot Snapshot
    {
        get
        {
            RefreshExpiration();
            return _snapshot;
        }
    }

    public void Require(AdminRequestFingerprint fingerprint, ulong sessionGeneration)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (sessionGeneration == 0)
        {
            throw new InvalidDataException("High-impact confirmation requires a non-zero session generation.");
        }

        _evidence = null;
        _snapshot = new HighImpactConfirmationSnapshot(
            HighImpactConfirmationState.Required,
            fingerprint,
            sessionGeneration,
            ExpiresAt: null,
            Consumed: false,
            ReasonCode: null);
        Changed?.Invoke();
    }

    public void Begin(AdminRequestFingerprint fingerprint, ulong sessionGeneration)
    {
        RefreshExpiration();
        RequireMatchingRequest(fingerprint, sessionGeneration);
        if (_snapshot.State != HighImpactConfirmationState.Required)
        {
            throw new InvalidOperationException(
                $"Confirmation cannot begin from state '{_snapshot.State}'.");
        }

        _snapshot = _snapshot with
        {
            State = HighImpactConfirmationState.Confirming,
            ReasonCode = null,
        };
        Changed?.Invoke();
    }

    public LocalConfirmationEvidence Confirm(AdminRequestFingerprint fingerprint, ulong sessionGeneration)
    {
        RefreshExpiration();
        RequireMatchingRequest(fingerprint, sessionGeneration);
        if (_snapshot.State != HighImpactConfirmationState.Confirming)
        {
            throw new InvalidOperationException(
                $"Explicit confirmation is required before confirming a high-impact request; current state '{_snapshot.State}'.");
        }

        var now = _utcNow();
        _evidence = new LocalConfirmationEvidence(
            EvidenceId: Guid.NewGuid().ToString("N"),
            Fingerprint: fingerprint,
            SessionGeneration: sessionGeneration,
            ConfirmedAt: now,
            ExpiresAt: now.Add(_timeout));
        _snapshot = _snapshot with
        {
            State = HighImpactConfirmationState.Confirmed,
            ExpiresAt = _evidence.ExpiresAt,
            Consumed = false,
            ReasonCode = null,
        };
        Changed?.Invoke();
        return _evidence;
    }

    public LocalConfirmationEvidence Consume(AdminRequestFingerprint fingerprint, ulong sessionGeneration)
    {
        RefreshExpiration();
        RequireMatchingRequest(fingerprint, sessionGeneration);
        if (_snapshot.State != HighImpactConfirmationState.Confirmed || _snapshot.Consumed || _evidence is null)
        {
            throw new InvalidOperationException("High-impact confirmation is absent, expired, invalid, or already consumed.");
        }

        var evidence = _evidence;
        _snapshot = _snapshot with
        {
            State = HighImpactConfirmationState.ExpiredOrInvalid,
            Consumed = true,
            ReasonCode = "confirmation.consumed",
        };
        _evidence = null;
        Changed?.Invoke();
        return evidence;
    }

    public void Invalidate(string reasonCode)
    {
        AdminSessionProjectionStore.ValidateStableToken(reasonCode, nameof(reasonCode));
        if (_snapshot.State == HighImpactConfirmationState.NotRequired)
        {
            return;
        }

        _evidence = null;
        _snapshot = _snapshot with
        {
            State = HighImpactConfirmationState.ExpiredOrInvalid,
            ExpiresAt = null,
            Consumed = false,
            ReasonCode = reasonCode,
        };
        Changed?.Invoke();
    }

    public void Reset()
    {
        _evidence = null;
        _snapshot = new HighImpactConfirmationSnapshot(
            HighImpactConfirmationState.NotRequired,
            Fingerprint: null,
            SessionGeneration: 0,
            ExpiresAt: null,
            Consumed: false,
            ReasonCode: null);
        Changed?.Invoke();
    }

    public void OnSessionChanged(AdminSessionProjection session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_snapshot.State == HighImpactConfirmationState.NotRequired)
        {
            return;
        }

        if (session.State != AdminSessionAccessState.Active)
        {
            Invalidate("confirmation.session-inactive");
            return;
        }
        if (session.SessionGeneration != _snapshot.SessionGeneration)
        {
            Invalidate("confirmation.session-generation-changed");
        }
    }

    private void RefreshExpiration()
    {
        if (_snapshot.State != HighImpactConfirmationState.Confirmed || _snapshot.ExpiresAt is not { } expiresAt)
        {
            return;
        }
        if (_utcNow() < expiresAt)
        {
            return;
        }

        _evidence = null;
        _snapshot = _snapshot with
        {
            State = HighImpactConfirmationState.ExpiredOrInvalid,
            ExpiresAt = null,
            Consumed = false,
            ReasonCode = "confirmation.expired",
        };
        Changed?.Invoke();
    }

    private void RequireMatchingRequest(AdminRequestFingerprint fingerprint, ulong sessionGeneration)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (_snapshot.Fingerprint is null || !_snapshot.Fingerprint.Equals(fingerprint))
        {
            throw new InvalidOperationException("Confirmation evidence cannot be reused for a different request fingerprint.");
        }
        if (_snapshot.SessionGeneration != sessionGeneration)
        {
            throw new InvalidOperationException("Confirmation session generation is stale.");
        }
    }
}
