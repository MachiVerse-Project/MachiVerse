namespace MachiVerse.View.Protocol;

public enum ViewLifecycleState
{
    Starting,
    Connecting,
    Negotiating,
    Authenticating,
    Syncing,
    Ready,
    Resyncing,
    Reconnecting,
    Degraded,
    Closed,
    Faulted
}
