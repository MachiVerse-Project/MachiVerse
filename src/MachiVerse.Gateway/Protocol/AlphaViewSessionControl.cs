namespace MachiVerse.Gateway.Protocol;

public enum AlphaViewSessionTerminalKind
{
    None = 0,
    Revoked = 3,
    Expired = 4,
}

/// <summary>
/// Local-Alpha-only test/control-plane seam used by INT-01 to cause a canonical
/// auth.session.changed terminal transition. It never authorizes requests and is not
/// part of the Standard Protocol surface. By default Alpha accepts exactly one active
/// General View session so a scheduled transition cannot target another tab. INT-02 may
/// explicitly enable concurrent sessions for churn/slow-consumer integration coverage;
/// terminal scheduling remains fail-closed unless exactly one session is active.
/// </summary>
public sealed class AlphaViewSessionControl(bool allowConcurrentSessions = false)
{
    private readonly object _gate = new();
    private readonly HashSet<string> _activeSessionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AlphaViewSessionTerminalKind> _pendingTerminalBySession = new(StringComparer.Ordinal);

    public bool HasActiveSession
    {
        get
        {
            lock (_gate) return _activeSessionIds.Count != 0;
        }
    }

    public bool TryRegister(string sessionIdHex)
    {
        ValidateSessionIdHex(sessionIdHex);
        lock (_gate)
        {
            if (!allowConcurrentSessions && _activeSessionIds.Count != 0)
                return false;
            if (!_activeSessionIds.Add(sessionIdHex))
                return false;
            _pendingTerminalBySession.Remove(sessionIdHex);
            return true;
        }
    }

    public void Unregister(string sessionIdHex)
    {
        ValidateSessionIdHex(sessionIdHex);
        lock (_gate)
        {
            _activeSessionIds.Remove(sessionIdHex);
            _pendingTerminalBySession.Remove(sessionIdHex);
        }
    }

    public bool TrySchedule(string terminal, out AlphaViewSessionTerminalKind kind)
    {
        kind = terminal switch
        {
            "revoked" => AlphaViewSessionTerminalKind.Revoked,
            "expired" => AlphaViewSessionTerminalKind.Expired,
            _ => AlphaViewSessionTerminalKind.None,
        };
        if (kind == AlphaViewSessionTerminalKind.None)
            return false;

        lock (_gate)
        {
            if (_activeSessionIds.Count != 1)
                return false;
            var activeSessionId = _activeSessionIds.Single();
            _pendingTerminalBySession[activeSessionId] = kind;
            return true;
        }
    }

    public AlphaViewSessionTerminalKind ConsumePending(string sessionIdHex)
    {
        ValidateSessionIdHex(sessionIdHex);
        lock (_gate)
        {
            if (!_activeSessionIds.Contains(sessionIdHex) ||
                !_pendingTerminalBySession.Remove(sessionIdHex, out var pending))
                return AlphaViewSessionTerminalKind.None;
            return pending;
        }
    }

    private static void ValidateSessionIdHex(string value)
    {
        if (value.Length != 32 || value.Any(static c => !Uri.IsHexDigit(c)) || value.All(static c => c == '0'))
            throw new ArgumentException("Alpha View session id must be a non-zero Id128 hex value.", nameof(value));
    }
}
