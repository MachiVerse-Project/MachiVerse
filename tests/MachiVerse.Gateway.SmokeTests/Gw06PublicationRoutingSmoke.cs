using System.Runtime.CompilerServices;
using MachiVerse.Gateway.Authorization;
using MachiVerse.Gateway.Configuration;
using MachiVerse.Gateway.Publication;
using MachiVerse.Gateway.State;
using MachiVerse.Protocol.V1;

internal static class Gw06PublicationRoutingSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var config = GatewayConfigLoader.LoadFile("config/gateway.toml");
        Require(config.OutboundQueues.PublicationCapacity == 64, "Gateway publication queue capacity must come from Config.");
        Require(config.OutboundQueues.ResultCapacity == 8192, "Gateway result queue capacity must come from Config.");
        Require(config.OutboundQueues.MaxClientBacklog == 8, "Gateway max client backlog must come from Config.");
        Require(config.OutboundQueues.PublicationBufferMs == 1000, "Gateway publication buffer window must come from Config.");

        var sessionId = Id(1);
        var subscriptionId = Id(2);
        var allow = new AuthorizationDecisionV1(
            Id(3),
            sessionId,
            SessionGeneration: 1,
            Permission: "view.world.read.public",
            OperationId: null,
            TargetKind: "world.subscribe/view.public.v1",
            Outcome: AuthorizationOutcomeV1.Allow,
            ReasonCode: "auth.allowed");

        var publication = new PublicationBufferV1(capacity: 2, maxClientBacklog: 1);
        publication.RegisterViewSubscriber(subscriptionId, "view.public.v1", allow);

        var denied = allow with
        {
            DecisionId = Id(4),
            Outcome = AuthorizationOutcomeV1.Deny,
            ReasonCode = "auth.unauthorized"
        };
        RequireReject(
            () => publication.RegisterViewSubscriber(Id(5), "view.public.v1", denied),
            "auth.unauthorized");

        var basis100 = Snapshot(100, 10);
        var first = publication.EnqueueConfirmed(basis100);
        Require(first.EnqueuedCount == 1 && publication.PendingCount == 1,
            "First confirmed publication must enter the bounded publication queue.");

        var basis101 = Snapshot(101, 11);
        var overflow = publication.EnqueueConfirmed(basis101);
        var subscriber = publication.ReadSubscriber(subscriptionId);
        Require(overflow.ResyncRequiredCount == 1 && publication.PendingCount == 0,
            "Slow publication backlog must be coalesced instead of growing without bound.");
        Require(subscriber.State == PublicationSubscriberStateV1.ResyncRequired && subscriber.CoalescedPublicationCount == 2,
            "Slow subscriber must move to explicit resync state with observable coalescing.");

        var resync = publication.RequireResyncDelivery(subscriptionId, basis101);
        Require(resync.RequiresFullResync && resync.BasisStep == 101,
            "Backlog loss must recover through an explicit latest-confirmed full resync delivery.");
        publication.CompleteResync(subscriptionId, deliveredBasisStep: 101);

        var basis102 = Snapshot(102, 12);
        publication.EnqueueConfirmed(basis102);
        var delivery = publication.TryDequeue(subscriptionId)
            ?? throw new InvalidOperationException("Resynced subscriber did not resume publication delivery.");
        Require(delivery.BasisStep == 102 && !delivery.RequiresFullResync,
            "Normal publication must resume after explicit resync completion.");

        var capacityBuffer = new PublicationBufferV1(capacity: 1, maxClientBacklog: 8);
        capacityBuffer.RegisterViewSubscriber(Id(20), "view.public.v1", allow with { DecisionId = Id(21) });
        capacityBuffer.RegisterViewSubscriber(
            Id(22),
            "view.public.v1",
            allow with { DecisionId = Id(23), SessionId = Id(24) });
        var capacityResult = capacityBuffer.EnqueueConfirmed(Snapshot(200, 20));
        Require(capacityResult.EnqueuedCount == 1 && capacityResult.ResyncRequiredCount == 1,
            "Global publication capacity must degrade a subscriber to resync instead of blocking other queues.");

        var resultRouter = new OperationResultRouterV1(capacity: 1);
        var routeId = Id(30);
        var terminalA = TerminalCustody(31, "result-a");
        var terminalB = TerminalCustody(32, "result-b");

        var enqueued = resultRouter.TryEnqueue(routeId, terminalA);
        Require(enqueued.Status == ResultRouteEnqueueStatusV1.Enqueued && resultRouter.PendingCount == 1,
            "Terminal result must enter its dedicated result queue.");
        var duplicate = resultRouter.TryEnqueue(routeId, terminalA);
        Require(duplicate.Status == ResultRouteEnqueueStatusV1.Duplicate && resultRouter.PendingCount == 1,
            "Repeated terminal observation must not duplicate a queued result.");

        var backpressured = resultRouter.TryEnqueue(routeId, terminalB);
        Require(backpressured.Status == ResultRouteEnqueueStatusV1.Backpressured &&
                backpressured.ReasonCode == "gateway.result-backpressure" &&
                resultRouter.PendingCount == 1,
            "Full result queue must signal stable backpressure instead of dropping an existing terminal result.");
        Require(terminalB.State == GatewayCustodyState.Terminal && terminalB.TerminalResult?.Code == "result-b",
            "Backpressured result must remain recoverable from custody authority.");

        var deliveredA = resultRouter.TryDequeue()
            ?? throw new InvalidOperationException("Queued terminal result disappeared.");
        Require(deliveredA.Result.Code == "result-a", "Result router changed terminal result identity.");
        var retriedB = resultRouter.TryEnqueue(routeId, terminalB);
        Require(retriedB.Status == ResultRouteEnqueueStatusV1.Enqueued,
            "Custody-held terminal result must be routable after queue capacity becomes available.");
    }

    private static ConfirmedStateSnapshot Snapshot(ulong basisStep, byte marker)
        => new(
            basisStep,
            Enumerable.Repeat(marker, 32).ToArray(),
            Enumerable.Repeat((byte)(marker + 1), 32).ToArray(),
            new Dictionary<ProjectionRecordKey, ConfirmedProjectionRecord>());

    private static OperationCustodyRecord TerminalCustody(byte marker, string code)
        => new(
            Id(marker),
            Enumerable.Repeat((byte)(marker + 1), 32).ToArray(),
            GatewayCustodyState.Terminal,
            LastObservedMasterGeneration: 1,
            new ResultV1
            {
                Status = (ResultStatusV1)1,
                Code = code,
                RetryAdvice = (RetryAdviceV1)1,
            });

    private static byte[] Id(byte marker) => Enumerable.Repeat(marker, 16).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireReject(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException($"Expected GW-06 rejection: {expectedMessage}");
    }
}
