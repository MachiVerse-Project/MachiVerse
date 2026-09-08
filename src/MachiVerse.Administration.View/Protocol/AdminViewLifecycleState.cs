namespace MachiVerse.Administration.View.Protocol;

public enum AdminViewLifecycleState
{
    Starting,
    Connecting,
    Negotiating,
    Authenticating,
    Syncing,
    Ready,
    Reconnecting,
    Degraded,
    Closed,
    Faulted
}
