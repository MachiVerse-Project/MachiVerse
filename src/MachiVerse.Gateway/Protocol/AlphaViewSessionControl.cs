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
/// part of the Standard Protocol surface. Alpha accepts exactly one active General View
/// session while this seam is enabled so a scheduled transition cannot target another tab.
/// </summary>
public sealed class AlphaViewSessionControl
{
    private readonly object _gate = new();
    private string? _activeSessionIdHex;
    private AlphaViewSessionTerminalKind _pendingTerminal;

    public bool HasActiveSession
    {
        get
        {
            lock (_gate) return _activeSessionIdHex is not null;
        }
    }

    public bool TryRegister(string sessionIdHex)
    {
        ValidateSessionIdHex(sessionIdHex);
        lock (_gate)
        {
            if (_activeSessionIdHex is not null)
                return false;
            _activeSessionIdHex = sessionIdHex;
            _pendingTerminal = AlphaViewSessionTerminalKind.None;
            return true;
        }
    }

    public void Unregister(string sessionIdHex)
    {
        ValidateSessionIdHex(sessionIdHex);
        lock (_gate)
        {
            if (!string.Equals(_activeSessionIdHex, sessionIdHex, StringComparison.Ordinal))
                return;
            _activeSessionIdHex = null;
            _pendingTerminal = AlphaViewSessionTerminalKind.None;
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
            if (_activeSessionIdHex is null)
                return false;
            _pendingTerminal = kind;
            return true;
        }
    }

    public AlphaViewSessionTerminalKind ConsumePending(string sessionIdHex)
    {
        ValidateSessionIdHex(sessionIdHex);
        lock (_gate)
        {
            if (!string.Equals(_activeSessionIdHex, sessionIdHex, StringComparison.Ordinal))
                return AlphaViewSessionTerminalKind.None;
            var pending = _pendingTerminal;
            _pendingTerminal = AlphaViewSessionTerminalKind.None;
            return pending;
        }
    }

    private static void ValidateSessionIdHex(string value)
    {
        if (value.Length != 32 || value.Any(static c => !Uri.IsHexDigit(c)) || value.All(static c => c == '0'))
            throw new ArgumentException("Alpha View session id must be a non-zero Id128 hex value.", nameof(value));
    }
}
