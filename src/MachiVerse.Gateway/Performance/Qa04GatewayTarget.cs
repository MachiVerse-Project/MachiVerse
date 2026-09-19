using System.Text.Json;
using MachiVerse.Gateway.Audit;
using MachiVerse.Gateway.Auth;
using MachiVerse.Gateway.Authorization;
using MachiVerse.Gateway.Publication;
using MachiVerse.Gateway.State;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Performance;

public static class Qa04GatewayTargetV1
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidDataException("qa04.gateway.request-line-required");
            var request = JsonSerializer.Deserialize<Request>(line, Json)
                ?? throw new InvalidDataException("qa04.gateway.request-null");
            if (!string.Equals(request.SchemaVersion, "1.0", StringComparison.Ordinal))
                throw new InvalidDataException("qa04.gateway.schema-version-unsupported");

            object response = request.Command switch
            {
                "audit-crash-case-run" => await RunAuditCrashCaseAsync(request.DataRoot, cancellationToken).ConfigureAwait(false),
                "audit-crash-case-verify" => await VerifyAuditCrashCaseAsync(
                    request.DataRoot,
                    request.ExpectedDurable,
                    cancellationToken).ConfigureAwait(false),
                "publication-stress-run" => RunPublicationStress(),
                _ => throw new InvalidDataException($"qa04.gateway.command-unsupported:{request.Command}"),
            };

            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, Json)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"QA-04 Gateway target FAILED: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<object> RunAuditCrashCaseAsync(
        string dataRoot,
        CancellationToken cancellationToken)
    {
        RequireRoot(dataRoot);
        await using var store = new GatewayAuditStoreV1(dataRoot, queryMaxPageSize: 500);
        await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _ = await store.AppendAsync(AuditDraft(), cancellationToken).ConfigureAwait(false);
        return new
        {
            schemaVersion = "1.0",
            stage = "audit-append",
            completedWithoutCrash = true,
        };
    }

    private static async Task<object> VerifyAuditCrashCaseAsync(
        string dataRoot,
        bool expectedDurable,
        CancellationToken cancellationToken)
    {
        RequireRoot(dataRoot);
        await using var store = new GatewayAuditStoreV1(dataRoot, queryMaxPageSize: 500);
        await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await store.ValidateChainAsync(cancellationToken).ConfigureAwait(false);
        var records = await store.QueryAsync(null, 100, cancellationToken).ConfigureAwait(false);
        var durable = records.Count == 1;
        if (durable != expectedDurable)
            throw new InvalidDataException(
                $"qa04.audit.durability-mismatch:expected={expectedDurable}:actual={durable}");
        if (durable && !string.Equals(records[0].AuditKind, "audit.persistence.recovery-completed", StringComparison.Ordinal))
            throw new InvalidDataException("qa04.audit.record-kind-drift");
        return new
        {
            schemaVersion = "1.0",
            stage = "audit-append",
            expectedDurable,
            durableFactPresent = durable,
            historyChainValid = true,
            noHalfTransition = true,
            recordCount = records.Count,
        };
    }

    private static object RunPublicationStress()
    {
        const int subscriberCount = 100;
        const int slowConsumerCount = 10;
        var auth = new GatewayAuthorizationService();
        var publication = new PublicationBufferV1(capacity: 2048, maxClientBacklog: 4);
        var subscriptions = new byte[subscriberCount][];
        var slow = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < subscriberCount; index++)
        {
            var marker = checked((byte)(index + 1));
            var subscription = Id(marker);
            subscriptions[index] = subscription;
            publication.RegisterViewSubscriber(
                subscription,
                "view.public.v1",
                auth,
                SubscriberSession(index, marker),
                expectedSessionGeneration: 1);
            if (index >= subscriberCount - slowConsumerCount)
                slow.Add(Convert.ToHexStringLower(subscription));
        }

        ConfirmedStateSnapshot? latest = null;
        for (ulong basis = 1; basis <= 6; basis++)
        {
            latest = Snapshot(basis, checked((byte)(basis + 10)));
            _ = publication.EnqueueConfirmed(latest);
            foreach (var subscription in subscriptions)
            {
                if (slow.Contains(Convert.ToHexStringLower(subscription)))
                    continue;
                var delivery = publication.TryDequeue(subscription)
                    ?? throw new InvalidDataException("qa04.publication.normal-subscriber-delivery-missing");
                if (delivery.BasisStep != basis)
                    throw new InvalidDataException("qa04.publication.normal-subscriber-basis-drift");
            }
        }

        if (latest is null)
            throw new InvalidDataException("qa04.publication.latest-snapshot-missing");

        var slowResyncCount = 0;
        ulong totalCoalesced = 0;
        foreach (var subscription in subscriptions)
        {
            if (!slow.Contains(Convert.ToHexStringLower(subscription)))
                continue;
            var state = publication.ReadSubscriber(subscription);
            if (state.State != PublicationSubscriberStateV1.ResyncRequired)
                throw new InvalidDataException("qa04.publication.slow-subscriber-did-not-enter-resync");
            totalCoalesced = checked(totalCoalesced + state.CoalescedPublicationCount);
            var resync = publication.RequireResyncDelivery(subscription, latest);
            if (!resync.RequiresFullResync || resync.BasisStep != latest.BasisStep)
                throw new InvalidDataException("qa04.publication.full-resync-delivery-drift");
            publication.CompleteResync(subscription, latest.BasisStep);
            slowResyncCount++;
        }

        if (slowResyncCount != slowConsumerCount || totalCoalesced == 0)
            throw new InvalidDataException("qa04.publication.slow-consumer-coverage-incomplete");

        var resultRouter = new OperationResultRouterV1(capacity: subscriberCount);
        for (var index = 0; index < subscriberCount; index++)
        {
            var marker = checked((byte)(index + 1));
            var route = new GatewayOutboundRouteV1(GatewayOutboundRouteKindV1.GeneralView, Id(marker));
            var result = resultRouter.TryEnqueue(route, TerminalCustody(marker));
            if (result.Status != ResultRouteEnqueueStatusV1.Enqueued)
                throw new InvalidDataException("qa04.publication.result-route-blocked-by-publication-backlog");
        }
        for (var index = 0; index < subscriberCount; index++)
        {
            if (resultRouter.TryDequeue() is null)
                throw new InvalidDataException("qa04.publication.result-route-loss");
        }

        var resumed = Snapshot(7, 20);
        _ = publication.EnqueueConfirmed(resumed);
        foreach (var subscription in subscriptions)
        {
            var delivery = publication.TryDequeue(subscription)
                ?? throw new InvalidDataException("qa04.publication.post-resync-delivery-missing");
            if (delivery.BasisStep != resumed.BasisStep || delivery.RequiresFullResync)
                throw new InvalidDataException("qa04.publication.post-resync-continuity-drift");
        }

        return new
        {
            schemaVersion = "1.0",
            profileId = "perf.publication.v1",
            gatewayCount = 1,
            viewSubscribers = subscriberCount,
            slowConsumers = slowConsumerCount,
            subscriberClassMix = new
            {
                spectator = 60,
                diver = 35,
                moderator = 4,
                generalViewAdministrator = 1,
            },
            slowConsumersDidNotBlockCustodyOrResult = true,
            continuityAfterCoalesceResync = true,
            slowConsumerResyncCount = slowResyncCount,
            totalCoalescedPublicationCount = totalCoalesced,
            finalPendingPublicationCount = publication.PendingCount,
            finalPendingResultCount = resultRouter.PendingCount,
            passed = true,
            failureCodes = Array.Empty<string>(),
        };
    }

    private static AuditRecordDraftV1 AuditDraft()
        => new(
            AuditKind: "audit.persistence.recovery-completed",
            ObservedAtUnixNs: 1,
            Component: "gateway",
            ComponentInstanceId: Id(0x41),
            ActorRef: null,
            SessionRefDigest: null,
            OperationId: null,
            CorrelationId: null,
            TargetRef: "gateway",
            WorldId: null,
            SimulationStep: null,
            ConfigGeneration: null,
            RequestDigest: null,
            ResultStatus: "success",
            ResultCode: "qa04.persistence.audit",
            ApprovalEvidenceDigest: null,
            SummaryFields: new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["profile"] = "perf.persistence.v1",
            });

    private static GatewaySessionSnapshot SubscriberSession(int index, byte marker)
    {
        var role = index switch
        {
            < 60 => "view.spectator",
            < 95 => "view.diver",
            < 99 => "view.moderator",
            99 => "view.administrator",
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
        var now = DateTimeOffset.UnixEpoch;
        var accountMarker = marker == byte.MaxValue ? (byte)1 : checked((byte)(marker + 1));
        return new GatewaySessionSnapshot(
            SessionId: Id(marker),
            AccountId: Id(accountMarker),
            DiverRef: role == "view.spectator" ? null : Id(checked((byte)(0x80 + index % 100))),
            AuthDomain: (AuthDomainWireV1)1,
            EffectiveRoleSet: role,
            EffectivePermissions: PermissionRegistry.ResolveGeneralRole(role),
            IssuedMasterGeneration: 1,
            SessionGeneration: 1,
            CreatedAt: now,
            LastSecurityEventAt: now,
            LastSeenAt: now,
            Status: GatewaySessionStatus.Active);
    }

    private static ConfirmedStateSnapshot Snapshot(ulong basisStep, byte marker)
        => new(
            basisStep,
            Enumerable.Repeat(marker, 32).ToArray(),
            Enumerable.Repeat(checked((byte)(marker + 1)), 32).ToArray(),
            new Dictionary<ProjectionRecordKey, ConfirmedProjectionRecord>());

    private static OperationCustodyRecord TerminalCustody(byte marker)
        => new(
            Id(marker),
            Enumerable.Repeat(checked((byte)(marker + 1)), 32).ToArray(),
            GatewayCustodyState.Terminal,
            LastObservedMasterGeneration: 1,
            new ResultV1
            {
                Status = (ResultStatusV1)1,
                Code = "qa04.result",
                RetryAdvice = (RetryAdviceV1)1,
            });

    private static byte[] Id(byte marker)
        => Enumerable.Repeat(marker == 0 ? (byte)1 : marker, 16).ToArray();

    private static void RequireRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidDataException("qa04.gateway.data-root-required");
        Directory.CreateDirectory(Path.GetFullPath(root));
    }

    private sealed class Request
    {
        public string SchemaVersion { get; set; } = "";
        public string Command { get; set; } = "";
        public string DataRoot { get; set; } = "";
        public bool ExpectedDurable { get; set; }
    }
}
