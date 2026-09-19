using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
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
                "soak-operational-cycle-run" => await RunSoakOperationalCycleAsync(
                    request.DataRoot,
                    request.CycleOrdinal,
                    cancellationToken).ConfigureAwait(false),
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

    private static async Task<object> RunSoakOperationalCycleAsync(
        string dataRoot,
        ulong cycleOrdinal,
        CancellationToken cancellationToken)
    {
        RequireRoot(dataRoot);
        if (cycleOrdinal == 0)
            throw new InvalidDataException("qa04.soak.gateway-cycle-ordinal-zero");

        var localId = ByteString.CopyFrom(Id(0x51));
        var remoteId = ByteString.CopyFrom(Id(0x52));
        var authority = new MasterAuthorityTracker(localId);
        var firstGeneration = checked(cycleOrdinal * 2UL - 1UL);
        var secondGeneration = checked(firstGeneration + 1UL);
        authority.Apply(new MasterGenerationStateV1
        {
            MasterGeneration = firstGeneration,
            CurrentMasterGatewayId = localId,
        });
        if (!authority.IsLocalMaster)
            throw new InvalidDataException("qa04.soak.gateway-initial-master-not-local");
        authority.Apply(new MasterGenerationStateV1
        {
            MasterGeneration = secondGeneration,
            CurrentMasterGatewayId = remoteId,
        });
        if (authority.IsLocalMaster ||
            authority.Current?.MasterGeneration != secondGeneration ||
            authority.Current.CurrentMasterGatewayId is null ||
            !authority.Current.CurrentMasterGatewayId.AsSpan().SequenceEqual(remoteId.Span))
            throw new InvalidDataException("qa04.soak.gateway-failover-did-not-converge");

        var cache = new ConfirmedProjectionCache();
        var resync = new ResyncCoordinator(cache);
        var initial = FullPublication(
            publicationMarker: 0x61,
            continuityMarker: 0x62,
            schemaMarker: 0x63);
        _ = resync.ApplyOrEnterSuspect(initial.Publication, checked(cycleOrdinal * 2UL - 1UL), [initial.Chunk]);
        if (resync.State != GatewaySyncState.Synced || !resync.AllowsWorldAffectingAdmission)
            throw new InvalidDataException("qa04.soak.gateway-initial-sync-failed");

        resync.MarkSuspect("component.core-disconnected");
        if (resync.State != GatewaySyncState.Suspect || resync.AllowsWorldAffectingAdmission)
            throw new InvalidDataException("qa04.soak.gateway-disconnect-did-not-gate-admission");
        var request = resync.BeginResync(ByteString.CopyFrom(Id(0x64)), forceFull: true);
        if ((int)request.Preference != 2 || request.HasClientBasisStep || request.HasClientContinuityToken)
            throw new InvalidDataException("qa04.soak.gateway-full-resync-request-drift");

        var recovered = FullPublication(
            publicationMarker: 0x65,
            continuityMarker: 0x66,
            schemaMarker: 0x63);
        _ = resync.ApplyOrEnterSuspect(recovered.Publication, checked(cycleOrdinal * 2UL), [recovered.Chunk]);
        if (resync.State != GatewaySyncState.Synced || !resync.AllowsWorldAffectingAdmission)
            throw new InvalidDataException("qa04.soak.gateway-resync-did-not-recover");

        _ = RunPublicationStress();

        var auditRoot = Path.Combine(Path.GetFullPath(dataRoot), "gateway-audit");
        await using var audit = new GatewayAuditStoreV1(auditRoot, queryMaxPageSize: 5000);
        await audit.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _ = await audit.AppendAsync(
            SoakAuditDraft("audit.gateway.master-role-changed", cycleOrdinal, secondGeneration),
            cancellationToken).ConfigureAwait(false);
        _ = await audit.AppendAsync(
            SoakAuditDraft("audit.persistence.recovery-completed", cycleOrdinal, secondGeneration),
            cancellationToken).ConfigureAwait(false);
        await audit.ValidateChainAsync(cancellationToken).ConfigureAwait(false);
        var records = await audit.QueryAsync(null, 5000, cancellationToken).ConfigureAwait(false);
        if (records.Count != checked((int)cycleOrdinal * 2))
            throw new InvalidDataException("qa04.soak.gateway-audit-cardinality-drift");

        return new
        {
            schemaVersion = "1.0",
            cycleOrdinal,
            failoverConverged = true,
            reconnectResyncConverged = true,
            viewChurnAndSlowConsumerLoadApplied = true,
            auditChainValid = true,
            resultingMasterGeneration = secondGeneration,
            auditRecordCount = records.Count,
            passed = true,
            failureCodes = Array.Empty<string>(),
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

    private static (StatePublicationV1 Publication, StatePublicationChunkV1 Chunk) FullPublication(
        byte publicationMarker,
        byte continuityMarker,
        byte schemaMarker)
    {
        var publicationId = ByteString.CopyFrom(Id(publicationMarker));
        var publication = new StatePublicationV1
        {
            PublicationId = publicationId,
            Kind = (PublicationKindV1)1,
            StateContinuityToken = ByteString.CopyFrom(Enumerable.Repeat(continuityMarker, 32).ToArray()),
            ChunkCount = 1,
            ProjectionSchemaDigest = ByteString.CopyFrom(Enumerable.Repeat(schemaMarker, 32).ToArray()),
        };
        var payload = new ProjectionChunkPayloadV1
        {
            SubscriptionId = ByteString.CopyFrom(Id(0x67)),
            PublicationId = publicationId,
            ChunkIndex = 0,
        }.ToByteArray();
        var chunk = new StatePublicationChunkV1
        {
            PublicationId = publicationId,
            ChunkIndex = 0,
            ChunkCount = 1,
            Compression = (CompressionKindV1)1,
            UncompressedPayloadDigest = ByteString.CopyFrom(SHA256.HashData(payload)),
            Payload = ByteString.CopyFrom(payload),
        };
        return (publication, chunk);
    }

    private static AuditRecordDraftV1 SoakAuditDraft(
        string kind,
        ulong cycleOrdinal,
        ulong masterGeneration)
        => new(
            AuditKind: kind,
            ObservedAtUnixNs: checked((long)cycleOrdinal),
            Component: "gateway",
            ComponentInstanceId: Id(0x51),
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
            ResultCode: "qa04.soak.operational-cycle",
            ApprovalEvidenceDigest: null,
            SummaryFields: new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["cycle"] = cycleOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["master-generation"] = masterGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

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
        public ulong CycleOrdinal { get; set; }
    }
}
