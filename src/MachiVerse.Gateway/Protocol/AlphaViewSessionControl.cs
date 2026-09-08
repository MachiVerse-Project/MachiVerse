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
/// part of the Standard Protocol surface.
/// </summary>
public sealed class AlphaViewSessionControl
{
    private int _pendingTerminal;

    public AlphaViewSessionTerminalKind PendingTerminal
        => (AlphaViewSessionTerminalKind)Volatile.Read(ref _pendingTerminal);

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

        Interlocked.Exchange(ref _pendingTerminal, (int)kind);
        return true;
    }

    public AlphaViewSessionTerminalKind ConsumePending()
        => (AlphaViewSessionTerminalKind)Interlocked.Exchange(
            ref _pendingTerminal,
            (int)AlphaViewSessionTerminalKind.None);
}
