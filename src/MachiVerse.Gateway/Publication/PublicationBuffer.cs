using MachiVerse.Gateway.Auth;
using MachiVerse.Gateway.Authorization;
using MachiVerse.Gateway.State;

namespace MachiVerse.Gateway.Publication;

public enum PublicationSubscriberStateV1
{
    Ready = 1,
    ResyncRequired = 2,
}

public sealed record PublicationDeliveryV1(
    byte[] SubscriptionId,
    byte[] SessionId,
    string ProjectionProfile,
    ulong BasisStep,
    byte[] ContinuityToken,
    byte[] ProjectionSchemaDigest,
    ConfirmedStateSnapshot Snapshot,
    bool RequiresFullResync);

public sealed record PublicationSubscriberSnapshotV1(
    byte[] SubscriptionId,
    byte[] SessionId,
    string ProjectionProfile,
    PublicationSubscriberStateV1 State,
    int PendingCount,
    ulong CoalescedPublicationCount,
    ulong? LatestConfirmedBasisStep);

public sealed record PublicationRouteResultV1(
    int EnqueuedCount,
    int ResyncRequiredCount,
    int ActiveSubscriberCount);

/// <summary>
/// Bounded, lossy-by-design state publication queue. It owns no world authority: every frame is
/// a reference to an already confirmed cache snapshot. Slow subscribers are moved to an explicit
/// resync-required state instead of allowing their backlog to block Operation custody/result work.
/// </summary>
public sealed class PublicationBufferV1
{
    private sealed class SubscriberState(
        byte[] subscriptionId,
        byte[] sessionId,
        string projectionProfile)
    {
        public byte[] SubscriptionId { get; } = subscriptionId;
        public byte[] SessionId { get; } = sessionId;
        public string ProjectionProfile { get; } = projectionProfile;
        public Queue<PublicationDeliveryV1> Pending { get; } = new();
        public PublicationSubscriberStateV1 State { get; set; } = PublicationSubscriberStateV1.Ready;
        public ulong CoalescedPublicationCount { get; set; }
        public ulong? LatestConfirmedBasisStep { get; set; }
    }

    private readonly int _capacity;
    private readonly int _maxClientBacklog;
    private readonly Dictionary<string, SubscriberState> _subscribers = new(StringComparer.Ordinal);
    private int _pendingCount;

    public PublicationBufferV1(int capacity, int maxClientBacklog)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maxClientBacklog <= 0) throw new ArgumentOutOfRangeException(nameof(maxClientBacklog));
        _capacity = capacity;
        _maxClientBacklog = maxClientBacklog;
    }

    public int PendingCount => _pendingCount;
    public int SubscriberCount => _subscribers.Count;

    public PublicationSubscriberSnapshotV1 RegisterViewSubscriber(
        ReadOnlySpan<byte> subscriptionId,
        string projectionProfile,
        GatewayAuthorizationService authorizationService,
        GatewaySessionSnapshot session,
        ulong expectedSessionGeneration)
    {
        var id = RequireId128(subscriptionId, nameof(subscriptionId));
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(session);

        var authorization = RequestAuthorizationPolicies.AuthorizeViewSubscription(
            authorizationService,
            session,
            projectionProfile,
            expectedSessionGeneration);
        authorizationService.RequireAllowed(authorization);

        var key = Convert.ToHexStringLower(id);
        if (_subscribers.ContainsKey(key))
            throw new InvalidDataException("publication.subscription-duplicate");

        var state = new SubscriberState(id, authorization.SessionId.ToArray(), projectionProfile);
        _subscribers.Add(key, state);
        return Snapshot(state);
    }

    public bool RemoveSubscriber(ReadOnlySpan<byte> subscriptionId)
    {
        var id = RequireId128(subscriptionId, nameof(subscriptionId));
        var key = Convert.ToHexStringLower(id);
        if (!_subscribers.Remove(key, out var state)) return false;
        _pendingCount -= state.Pending.Count;
        return true;
    }

    public PublicationRouteResultV1 EnqueueConfirmed(ConfirmedStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);

        var enqueued = 0;
        var resyncRequired = 0;
        foreach (var state in _subscribers.Values.OrderBy(static item => Convert.ToHexStringLower(item.SubscriptionId), StringComparer.Ordinal))
        {
            if (state.LatestConfirmedBasisStep is { } latest && snapshot.BasisStep < latest)
                throw new InvalidDataException("publication.basis-regression");
            state.LatestConfirmedBasisStep = snapshot.BasisStep;

            if (state.State == PublicationSubscriberStateV1.ResyncRequired)
            {
                resyncRequired++;
                continue;
            }

            if (state.Pending.Count >= _maxClientBacklog || _pendingCount >= _capacity)
            {
                state.CoalescedPublicationCount = checked(state.CoalescedPublicationCount + (ulong)state.Pending.Count + 1UL);
                _pendingCount -= state.Pending.Count;
                state.Pending.Clear();
                state.State = PublicationSubscriberStateV1.ResyncRequired;
                resyncRequired++;
                continue;
            }

            state.Pending.Enqueue(new PublicationDeliveryV1(
                state.SubscriptionId.ToArray(),
                state.SessionId.ToArray(),
                state.ProjectionProfile,
                snapshot.BasisStep,
                snapshot.ContinuityToken.ToArray(),
                snapshot.ProjectionSchemaDigest.ToArray(),
                snapshot,
                RequiresFullResync: false));
            _pendingCount++;
            enqueued++;
        }

        return new PublicationRouteResultV1(enqueued, resyncRequired, _subscribers.Count);
    }

    public PublicationDeliveryV1? TryDequeue(ReadOnlySpan<byte> subscriptionId)
    {
        var id = RequireId128(subscriptionId, nameof(subscriptionId));
        var key = Convert.ToHexStringLower(id);
        if (!_subscribers.TryGetValue(key, out var state))
            throw new InvalidDataException("publication.subscription-unknown");
        if (state.State == PublicationSubscriberStateV1.ResyncRequired || state.Pending.Count == 0)
            return null;

        _pendingCount--;
        return state.Pending.Dequeue();
    }

    public PublicationDeliveryV1 RequireResyncDelivery(
        ReadOnlySpan<byte> subscriptionId,
        ConfirmedStateSnapshot latestConfirmed)
    {
        ArgumentNullException.ThrowIfNull(latestConfirmed);
        ValidateSnapshot(latestConfirmed);
        var id = RequireId128(subscriptionId, nameof(subscriptionId));
        var key = Convert.ToHexStringLower(id);
        if (!_subscribers.TryGetValue(key, out var state))
            throw new InvalidDataException("publication.subscription-unknown");
        if (state.State != PublicationSubscriberStateV1.ResyncRequired)
            throw new InvalidDataException("publication.resync-not-required");
        if (state.LatestConfirmedBasisStep is not { } latest || latestConfirmed.BasisStep != latest)
            throw new InvalidDataException("publication.resync-basis-stale");

        return new PublicationDeliveryV1(
            state.SubscriptionId.ToArray(),
            state.SessionId.ToArray(),
            state.ProjectionProfile,
            latestConfirmed.BasisStep,
            latestConfirmed.ContinuityToken.ToArray(),
            latestConfirmed.ProjectionSchemaDigest.ToArray(),
            latestConfirmed,
            RequiresFullResync: true);
    }

    public void CompleteResync(ReadOnlySpan<byte> subscriptionId, ulong deliveredBasisStep)
    {
        var id = RequireId128(subscriptionId, nameof(subscriptionId));
        var key = Convert.ToHexStringLower(id);
        if (!_subscribers.TryGetValue(key, out var state))
            throw new InvalidDataException("publication.subscription-unknown");
        if (state.State != PublicationSubscriberStateV1.ResyncRequired)
            throw new InvalidDataException("publication.resync-not-required");
        if (state.LatestConfirmedBasisStep is not { } latest || deliveredBasisStep != latest)
            throw new InvalidDataException("publication.resync-basis-stale");
        state.State = PublicationSubscriberStateV1.Ready;
    }

    public PublicationSubscriberSnapshotV1 ReadSubscriber(ReadOnlySpan<byte> subscriptionId)
    {
        var id = RequireId128(subscriptionId, nameof(subscriptionId));
        var key = Convert.ToHexStringLower(id);
        if (!_subscribers.TryGetValue(key, out var state))
            throw new InvalidDataException("publication.subscription-unknown");
        return Snapshot(state);
    }

    private static PublicationSubscriberSnapshotV1 Snapshot(SubscriberState state)
        => new(
            state.SubscriptionId.ToArray(),
            state.SessionId.ToArray(),
            state.ProjectionProfile,
            state.State,
            state.Pending.Count,
            state.CoalescedPublicationCount,
            state.LatestConfirmedBasisStep);

    private static void ValidateSnapshot(ConfirmedStateSnapshot snapshot)
    {
        if (snapshot.ContinuityToken.Length != 32)
            throw new InvalidDataException("protocol.invalid-continuity-token-length");
        if (snapshot.ProjectionSchemaDigest.Length != 32)
            throw new InvalidDataException("protocol.invalid-projection-schema-digest");
    }

    private static byte[] RequireId128(ReadOnlySpan<byte> value, string field)
    {
        if (value.Length != 16 || value.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
        return value.ToArray();
    }
}
