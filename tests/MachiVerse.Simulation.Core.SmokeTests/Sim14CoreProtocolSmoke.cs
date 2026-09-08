using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Protocol;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim14CoreProtocolSmoke
{
    internal static async Task RunAsync()
    {
        ProtocolCommon();
        SchedulingSemantics();
        await MasterAndOperationAsync();
        await PublicationAsync();
    }

    private static void ProtocolCommon()
    {
        Require(CoreGatewayProtocolRegistryV1.Entries.Count == 16,
            "SIM-14 protocol registry must contain the 16 canonical mv.core-gateway message rows.");
        Require(CoreGatewayProtocolRegistryV1.Get("gateway.register").PayloadSchemaId.Value == "protocol.gateway-register.v1",
            "gateway.register schema id must match the canonical registry.");
        Require(CoreGatewayProtocolRegistryV1.BaselineCapabilities.SequenceEqual(new[]
        {
            "protocol.operation-batch.v1",
            "protocol.operation-status.v1",
            "protocol.protobuf.v1",
            "protocol.state-full.v1",
        }), "Core-Gateway baseline capabilities must remain canonical and ordered.");

        var gatewayId = Id(0x1401);
        var componentId = Id(0x1402);
        var register = new GatewayRegisterV1
        {
            GatewayLogicalId = Bytes(gatewayId),
            ComponentInstanceId = Bytes(componentId),
            LastKnownMasterGeneration = 1,
            Readiness = (GatewayReadinessV1)3,
        };
        var valid = GatewayEnvelope("gateway.register", register, componentId, generation: 1);
        CoreGatewayWireValidatorV1.Validate(valid, CoreGatewayMessageDirectionV1.GatewayToCore, generation: 1);
        var roundTrip = CoreGatewayWireValidatorV1.DecodeAndValidate(
            valid.ToByteArray(), CoreGatewayMessageDirectionV1.GatewayToCore, generation: 1);
        Require(roundTrip.MessageType == "gateway.register", "protocol.envelope.valid: valid envelope must round-trip.");

        RequireProtocolCode(
            () => CoreGatewayWireValidatorV1.DecodeAndValidate(
                new byte[CoreGatewayProtocolRegistryV1.MaxSerializedEnvelopeBytes + 1],
                CoreGatewayMessageDirectionV1.GatewayToCore,
                generation: 1),
            "protocol.limit-exceeded",
            "protocol.envelope.size-limit");

        var wrongProtocol = valid.Clone();
        wrongProtocol.ProtocolId = "mv.gateway-view";
        RequireProtocolCode(
            () => CoreGatewayWireValidatorV1.Validate(wrongProtocol, CoreGatewayMessageDirectionV1.GatewayToCore, 1),
            "protocol.wrong-protocol",
            "protocol.wrong-protocol");

        var stale = valid.Clone();
        stale.NegotiationGeneration = 2;
        RequireProtocolCode(
            () => CoreGatewayWireValidatorV1.Validate(stale, CoreGatewayMessageDirectionV1.GatewayToCore, 1),
            "protocol.negotiation-stale",
            "protocol.negotiation-stale");

        var unknown = valid.Clone();
        unknown.MessageType = "gateway.unknown";
        RequireProtocolCode(
            () => CoreGatewayWireValidatorV1.Validate(unknown, CoreGatewayMessageDirectionV1.GatewayToCore, 1),
            "protocol.unknown-message-type",
            "protocol.unknown-message");

        var compatibleUnknown = valid.ToByteArray().Concat(new byte[] { 0x98, 0x06, 0x01 }).ToArray(); // field 99, varint 1
        var compatible = CoreGatewayWireValidatorV1.DecodeAndValidate(
            compatibleUnknown, CoreGatewayMessageDirectionV1.GatewayToCore, generation: 1);
        Require(compatible.MessageType == valid.MessageType,
            "protocol.protobuf-unknown-compatible: same-major unknown protobuf field must not alter known semantics.");

        var hello = Hello(new SupportedVersionRangeV1 { Major = 1, MinMinor = 0, MaxMinor = 0 });
        var accept = CoreGatewayNegotiatorV1.Negotiate(hello, new CoreGatewayNegotiationProfileV1());
        Require(accept.NegotiatedVersion.Major == 1 && accept.NegotiatedVersion.Minor == 0 && accept.NegotiationGeneration == 1,
            "Protocol default negotiation must select 1.0 generation 1.");

        var highHello = Hello(new SupportedVersionRangeV1 { Major = 1, MinMinor = 1, MaxMinor = 3 });
        var highProfile = new CoreGatewayNegotiationProfileV1(
            versions: [new MachiVerse.Simulation.Core.Protocol.SupportedProtocolRangeV1(1, 0, 2)]);
        var highest = CoreGatewayNegotiatorV1.Negotiate(highHello, highProfile);
        Require(highest.NegotiatedVersion.Minor == 2,
            "protocol.version.highest-common: highest common minor must be selected deterministically.");

        var noCommon = Hello(new SupportedVersionRangeV1 { Major = 2, MinMinor = 0, MaxMinor = 0 });
        RequireProtocolCode(
            () => CoreGatewayNegotiatorV1.Negotiate(noCommon, new CoreGatewayNegotiationProfileV1()),
            "protocol.version-incompatible",
            "protocol.version.no-common");

        var missingCapability = Hello(new SupportedVersionRangeV1 { Major = 1, MinMinor = 0, MaxMinor = 0 });
        missingCapability.ProvidedCapabilities.Remove("protocol.operation-status.v1");
        RequireProtocolCode(
            () => CoreGatewayNegotiatorV1.Negotiate(missingCapability, new CoreGatewayNegotiationProfileV1()),
            "protocol.capability-missing",
            "protocol.capability.required-missing");
    }

    private static void SchedulingSemantics()
    {
        var deferPolicy = new OperationSchedulingPolicyV1(
            ownerConfigGeneration: 1,
            minLeadSteps: 0,
            defaultDeadlineWindowSteps: 0,
            graceSteps: 2,
            OperationLatePolicyV1.DeferWithinGrace);
        var admission = new OperationSchedulingAdmissionV1(10, 1, null, null);
        var deferred = OperationSchedulingPlannerV1.Plan(
            deferPolicy,
            admission,
            new OperationSchedulingBarrierV1(11, PauseActive: false, PauseBasisStep: null));
        Require(deferred.Kind == OperationSchedulingDecisionKindV1.Scheduled && deferred.EffectiveStep == 11 && deferred.WasLate,
            "protocol.operation.defer-grace: late target within grace must defer to exact authoritative Step.");

        var rejectPolicy = new OperationSchedulingPolicyV1(
            ownerConfigGeneration: 1,
            minLeadSteps: 0,
            defaultDeadlineWindowSteps: 0,
            graceSteps: 2,
            OperationLatePolicyV1.Reject);
        var rejected = OperationSchedulingPlannerV1.Plan(
            rejectPolicy,
            admission,
            new OperationSchedulingBarrierV1(11, PauseActive: false, PauseBasisStep: null));
        Require(rejected.Kind == OperationSchedulingDecisionKindV1.TerminalRejected && rejected.EffectiveStep is null && rejected.ResultCode == "world.deadline-exceeded",
            "protocol.operation.deadline-reject: late REJECT policy must return exact terminal result.");

        var pauseFloor = OperationSchedulingPlannerV1.Plan(
            new OperationSchedulingPolicyV1(1, 0, null, 0, OperationLatePolicyV1.Reject),
            admission,
            new OperationSchedulingBarrierV1(10, PauseActive: true, PauseBasisStep: 10));
        Require(pauseFloor.EffectiveStep == 11,
            "protocol.operation.pause-floor: new operation admitted while paused at P must schedule no earlier than P+1.");
    }

    private static async Task MasterAndOperationAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-sim14-operation-" + Guid.NewGuid().ToString("N"));
        var worldId = Id(0x1410);
        var gatewayId = Id(0x1411);
        var componentId = Id(0x1412);
        var configDigest = SHA256.HashData("sim14-operation-config"u8);
        var seed = new WorldSeed256(new byte[32]);
        var paths = PersistenceLayout.Resolve(root, worldId, 1);
        try
        {
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);
            var genesis = Genesis(worldId, seed, configDigest);
            var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);

            StandardOperationV1 originalOperation;
            ulong firstMasterGeneration;
            await using (var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
            {
                await store.InitializeWorldMetadataAsync(
                    new WorldPersistenceMetadataSeed(worldId, 1, seed, continuity, 1, configDigest, 1),
                    genesis);

                var sessions = new CoreGatewaySessionRegistryV1();
                var master = new CoreMasterAuthorityCoordinatorV1(store, sessions);
                var recovered = await master.RecoverAsync();
                Require(recovered.MasterGeneration == 1 && recovered.CurrentMasterGatewayId is null,
                    "Core recovery must retain MasterGeneration but never restore a Gateway identity as authority.");
                sessions.Register(gatewayId, Register(gatewayId, componentId, 1));
                var assigned = await master.AssignMasterAsync(gatewayId, new StableToken("master.initial-election"));
                firstMasterGeneration = assigned.MasterGeneration;
                Require(firstMasterGeneration == 2 && assigned.CurrentMasterGatewayId == gatewayId,
                    "Master assignment must advance generation durably before authority publication.");
                Require((int)master.ToRoleState(gatewayId).Role == 2,
                    "Assigned Gateway role must publish MASTER at the durable generation.");

                var ingress = new PersistingIngress(store, worldId);
                var protocol = new CoreGatewayOperationProtocolV1(store, master, ingress);
                originalOperation = Operation(Id(0x1420), "test.accept", candidateStep: 999);
                var batch = Batch(Id(0x1430), "sim14-batch-a", originalOperation);

                await RequireProtocolCodeAsync(
                    () => protocol.SubmitBatchAsync(gatewayId, firstMasterGeneration - 1, batch),
                    "master.stale-generation",
                    "protocol.master.stale-generation");
                Require(await store.ReadOperationStateAsync(Id(0x1420)) is null,
                    "Stale Master batch must not terminalize or accept contained Operation.");

                var first = await protocol.SubmitBatchAsync(gatewayId, firstMasterGeneration, batch);
                Require(first.Entries.Count == 1 && (int)first.Entries[0].Lifecycle == 2 && !first.Entries[0].HasEffectiveStep,
                    "protocol.operation.candidate-not-effective: advisory candidate Step must not become authoritative effective_step on acceptance.");
                Require(ingress.NewDurableAcceptanceCount == 1,
                    "protocol.operation.retry-same-id: first logical Operation must create exactly one durable acceptance.");

                var retry = await protocol.SubmitBatchAsync(
                    gatewayId,
                    firstMasterGeneration,
                    Batch(Id(0x1431), "sim14-batch-retry", originalOperation));
                Require((int)retry.Entries[0].Result.Status == 5 && ingress.NewDurableAcceptanceCount == 1,
                    "protocol.operation.retry-same-id: retry must converge as duplicate without second acceptance.");

                var changedDigest = originalOperation.Clone();
                changedDigest.ImmutablePayloadDigest = ByteString.CopyFrom(SHA256.HashData("sim14-different-operation"u8));
                var mismatch = await protocol.SubmitBatchAsync(
                    gatewayId,
                    firstMasterGeneration,
                    Batch(Id(0x1432), "sim14-batch-mismatch", changedDigest));
                Require((int)mismatch.Entries[0].Result.Status == 6 && mismatch.Entries[0].Result.Code == "protocol.operation-payload-mismatch",
                    "protocol.operation.same-id-different-digest: old durable identity must reject a changed immutable digest.");
                var persistedOriginal = await store.ReadOperationStateAsync(Id(0x1420));
                Require(persistedOriginal is not null && persistedOriginal.OperationPayloadDigest.SequenceEqual(originalOperation.ImmutablePayloadDigest.ToByteArray()),
                    "Operation payload mismatch must leave old durable state unchanged.");

                var partial = await protocol.SubmitBatchAsync(
                    gatewayId,
                    firstMasterGeneration,
                    Batch(
                        Id(0x1433),
                        "sim14-batch-partial",
                        Operation(Id(0x1421), "test.accept"),
                        Operation(Id(0x1422), "test.reject")));
                Require((int)partial.Status == 2 && partial.Entries.Count == 2 &&
                        partial.Entries.Count(static x => (int)x.Result.Status == 6) == 1,
                    "protocol.batch.partial: one per-operation rejection must not discard the accepted peer entry.");

                var sameBatchDifferentDigest = batch.Clone();
                sameBatchDifferentDigest.BatchDigest = ByteString.CopyFrom(SHA256.HashData("changed-batch-wrapper"u8));
                await RequireProtocolCodeAsync(
                    () => protocol.SubmitBatchAsync(gatewayId, firstMasterGeneration, sameBatchDifferentDigest),
                    "protocol.batch-payload-mismatch",
                    "protocol.batch.same-id-different-digest");

                var status = await protocol.QueryStatusAsync(new OperationStatusQueryV1 { OperationId = originalOperation.OperationId });
                Require((int)status.State == 2 && status.OperationPayloadDigest.Equals(originalOperation.ImmutablePayloadDigest),
                    "Durable status query must expose current ACCEPTED lifecycle and immutable digest.");
            }

            await using (var recoveredStore = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
            {
                var sessions = new CoreGatewaySessionRegistryV1();
                var master = new CoreMasterAuthorityCoordinatorV1(recoveredStore, sessions);
                var recovered = await master.RecoverAsync();
                Require(recovered.MasterGeneration == firstMasterGeneration && recovered.CurrentMasterGatewayId is null,
                    "Master generation must survive process restart while Master identity returns to transition/unknown.");
                sessions.Register(gatewayId, Register(gatewayId, componentId, recovered.MasterGeneration));
                var reassigned = await master.AssignMasterAsync(gatewayId, new StableToken("master.restart-reselect"));
                Require(reassigned.MasterGeneration == firstMasterGeneration + 1,
                    "Master failover/reselection must advance generation monotonically.");

                var ingress = new PersistingIngress(recoveredStore, worldId);
                var protocol = new CoreGatewayOperationProtocolV1(recoveredStore, master, ingress);
                var retryAfterUnknownAck = await protocol.SubmitBatchAsync(
                    gatewayId,
                    reassigned.MasterGeneration,
                    Batch(Id(0x1434), "sim14-batch-after-reconnect", originalOperation));
                Require((int)retryAfterUnknownAck.Entries[0].Result.Status == 5 && ingress.NewDurableAcceptanceCount == 0,
                    "protocol.master.failover-unknown-ack: retry after failover must converge without double acceptance/apply.");
                var statusAfterReconnect = await protocol.QueryStatusAsync(new OperationStatusQueryV1 { OperationId = originalOperation.OperationId });
                Require((int)statusAfterReconnect.State == 2,
                    "protocol.operation.status-after-reconnect: recovered durable lifecycle must be queryable after reconnect.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task PublicationAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-sim14-publication-" + Guid.NewGuid().ToString("N"));
        var worldId = Id(0x1440);
        var seed = new WorldSeed256(new byte[32]);
        var configDigest = SHA256.HashData("sim14-publication-config"u8);
        var schemaDigest = SHA256.HashData("sim14-projection-schema"u8);
        var paths = PersistenceLayout.Resolve(root, worldId, 1);
        byte[] resultingContinuity;
        WorldStateV1 state1;
        try
        {
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);
            var genesis = Genesis(worldId, seed, configDigest);
            var initialContinuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);

            await using (var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
            {
                await store.InitializeWorldMetadataAsync(
                    new WorldPersistenceMetadataSeed(worldId, 1, seed, initialContinuity, 1, configDigest, 1),
                    genesis);
                var source = new FixedProjectionSource(schemaDigest);
                var coordinator = new CoreConfirmedPublicationCoordinatorV1(store, source, new SequentialPublicationIds());
                var state0 = State(worldId, 0, configDigest, masterGeneration: 1);
                var full = await coordinator.BuildFullAsync(state0);
                Require((int)full.Publication.Kind == 1 && !full.Publication.HasBaseStateContinuityToken &&
                        full.StateContinuityToken.SequenceEqual(initialContinuity),
                    "protocol.publication.full: FULL must bind exactly to durable State(0) continuity.");
                Require(full.Chunks.Count > 0 && full.Chunks.All(static x => x.Payload.Length <= CoreGatewayProtocolRegistryV1.MaxPublicationChunkBytes),
                    "FULL publication chunks must stay within the canonical 1 MiB limit.");
                foreach (var chunk in full.Chunks)
                    Require(SHA256.HashData(chunk.Payload.Span).AsSpan().SequenceEqual(chunk.UncompressedPayloadDigest.Span),
                        "Publication chunk digest must cover exact uncompressed protobuf payload bytes.");
                var fullRecords = full.Chunks
                    .OrderBy(static x => x.ChunkIndex)
                    .SelectMany(static x => ProjectionChunkPayloadV1.Parser.ParseFrom(x.Payload).Records)
                    .ToArray();
                Require(fullRecords.Length == 2 && fullRecords[0].RecordId.Span.SequenceCompareTo(fullRecords[1].RecordId.Span) < 0,
                    "FULL projection emission must use canonical record ordering independent of source order.");

                state1 = State(worldId, 1, configDigest, masterGeneration: 1);
                await RequireProtocolCodeAsync(
                    () => coordinator.BuildFullAsync(state1),
                    "world.invalid-state",
                    "no authoritative publication may cross before transition durability");

                var transition = HistoryRecordMaterial.Create(
                    worldId,
                    sequence: 2,
                    previousRecordDigest: genesis.RecordDigest,
                    recordType: "transition.committed.v1",
                    payloadSchemaId: "persistence.transition-committed",
                    payloadSchemaMajor: 1,
                    payloadSchemaMinor: 0,
                    payloadBytes: [0x14, 0x01],
                    writeNormalizedPayload: writer =>
                    {
                        writer.WriteMapStart(2);
                        writer.WriteUnsigned(0); writer.WriteUnsigned(0);
                        writer.WriteUnsigned(1); writer.WriteUnsigned(1);
                    });
                resultingContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
                    worldId, 1, initialContinuity, transition.RecordDigest);
                await store.PersistTransitionCommitAsync(
                    effectiveStep: 0,
                    resultingStep: 1,
                    resultingStateContinuityToken: resultingContinuity,
                    activeConfigGeneration: 1,
                    activeConfigDigest: configDigest,
                    history: transition,
                    terminalOperations: []);

                var continueRequest = new StateResyncRequestV1
                {
                    WorldId = Bytes(worldId),
                    ClientBasisStep = 0,
                    ClientContinuityToken = ByteString.CopyFrom(initialContinuity),
                    Preference = (ResyncPreferenceV1)1,
                };
                var delta = await coordinator.BuildForResyncAsync(continueRequest, state1);
                Require((int)delta.Publication.Kind == 2 && delta.Publication.HasBaseStateContinuityToken &&
                        delta.Publication.BaseStateContinuityToken.Span.SequenceEqual(initialContinuity) &&
                        delta.StateContinuityToken.SequenceEqual(resultingContinuity),
                    "protocol.publication.delta: exact retained base must produce a DELTA bound to old and new continuity tokens.");

                var badBase = new StateResyncRequestV1
                {
                    WorldId = Bytes(worldId),
                    ClientBasisStep = 0,
                    ClientContinuityToken = ByteString.CopyFrom(SHA256.HashData("bad-base"u8)),
                    Preference = (ResyncPreferenceV1)1,
                };
                var recoveredFull = await coordinator.BuildForResyncAsync(badBase, state1);
                Require((int)recoveredFull.Publication.Kind == 1 && !recoveredFull.Publication.HasBaseStateContinuityToken,
                    "protocol.publication.bad-base: mismatched continuity must take FULL/resync path, never blind DELTA.");

                var oversized = new CoreConfirmedPublicationCoordinatorV1(
                    store,
                    new OversizedProjectionSource(schemaDigest),
                    new SequentialPublicationIds(0x1460));
                await RequireProtocolCodeAsync(
                    () => oversized.BuildFullAsync(state1),
                    "protocol.limit-exceeded",
                    "single projection record larger than 1 MiB must fail closed");
            }

            await using (var recoveredStore = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
            {
                var recoveredHead = await recoveredStore.ReadCoreProtocolHeadAsync();
                Require(recoveredHead.FinalizedStep == 1 && recoveredHead.StateContinuityToken.SequenceEqual(resultingContinuity),
                    "Committed continuity token sequence must survive Core process restart exactly.");
                var afterRestart = new CoreConfirmedPublicationCoordinatorV1(
                    recoveredStore,
                    new FixedProjectionSource(schemaDigest),
                    new SequentialPublicationIds(0x1470));
                var fullAfterRestart = await afterRestart.BuildFullAsync(state1);
                Require((int)fullAfterRestart.Publication.Kind == 1 && fullAfterRestart.StateContinuityToken.SequenceEqual(resultingContinuity),
                    "After restart without retained DELTA base, Core must safely publish FULL from durable continuity.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ProtocolHelloV1 Hello(params SupportedVersionRangeV1[] ranges)
    {
        var hello = new ProtocolHelloV1 { ProtocolId = CoreGatewayProtocolRegistryV1.ProtocolId };
        hello.SupportedVersions.AddRange(ranges);
        hello.ProvidedCapabilities.AddRange(CoreGatewayProtocolRegistryV1.BaselineCapabilities);
        hello.RequiredCapabilities.AddRange(CoreGatewayProtocolRegistryV1.BaselineCapabilities);
        return hello;
    }

    private static WireEnvelopeV1 GatewayEnvelope(
        string type,
        IMessage payload,
        OpaqueId128 sender,
        uint generation,
        WorldContextWireV1? world = null,
        OperationContextWireV1? operation = null)
    {
        var entry = CoreGatewayProtocolRegistryV1.Get(type);
        var envelope = new WireEnvelopeV1
        {
            EnvelopeVersion = 1,
            ProtocolId = CoreGatewayProtocolRegistryV1.ProtocolId,
            ProtocolVersion = new ProtocolVersionV1 { Major = 1, Minor = 0 },
            NegotiationGeneration = generation,
            MessageType = type,
            MessageId = Bytes(Id(0x14f1)),
            CorrelationId = Bytes(Id(0x14f2)),
            SenderInstanceId = Bytes(sender),
            PayloadSchemaId = entry.PayloadSchemaId.Value,
            PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            PayloadCompression = (CompressionKindV1)1,
            Payload = payload.ToByteString(),
        };
        envelope.WorldContext = world;
        envelope.OperationContext = operation;
        return envelope;
    }

    private static GatewayRegisterV1 Register(OpaqueId128 gatewayId, OpaqueId128 componentId, ulong generation)
        => new()
        {
            GatewayLogicalId = Bytes(gatewayId),
            ComponentInstanceId = Bytes(componentId),
            LastKnownMasterGeneration = generation,
            Readiness = (GatewayReadinessV1)3,
        };

    private static StandardOperationV1 Operation(OpaqueId128 id, string kind, ulong? candidateStep = null)
    {
        var operation = new StandardOperationV1
        {
            OperationId = Bytes(id),
            ImmutablePayloadDigest = ByteString.CopyFrom(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes("sim14-operation:" + id))),
            OperationKind = kind,
            Admission = new OperationSchedulingAdmissionWireV1
            {
                AdmissionBasisStep = 0,
                SchedulingPolicyGeneration = 1,
            },
            OperationPayloadSchemaId = "operation.sim14-test",
            OperationPayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            OperationPayload = ByteString.CopyFromUtf8(kind),
        };
        if (candidateStep is { } value) operation.Candidate = new CandidateSchedulingWireV1 { CandidateStep = value };
        return operation;
    }

    private static OperationBatchV1 Batch(OpaqueId128 batchId, string digestSeed, params StandardOperationV1[] operations)
    {
        var batch = new OperationBatchV1
        {
            BatchId = Bytes(batchId),
            BatchDigest = ByteString.CopyFrom(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(digestSeed))),
            BatchKind = "batch.standard",
        };
        batch.Operations.AddRange(operations);
        return batch;
    }

    private static HistoryRecordMaterial Genesis(OpaqueId128 worldId, WorldSeed256 seed, byte[] configDigest)
        => HistoryRecordMaterial.Create(
            worldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "world.genesis.v1",
            payloadSchemaId: "core.world-genesis.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: [0x14],
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(seed.ToBytes());
                writer.WriteUnsigned(2); writer.WriteUnsigned(0);
                writer.WriteUnsigned(3); writer.WriteBytes(configDigest);
            });

    private static WorldStateV1 State(OpaqueId128 worldId, ulong step, byte[] configDigest, ulong masterGeneration)
    {
        var partitions = StandardDomainPartitionRegistry.Entries.Select(entry => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                entry,
                revision: 1,
                basisStep: step,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(entry.PartitionId.Value + ":" + step)))));
        var header = new WorldStateHeaderV1(
            worldId,
            step,
            worldSeedDigest: SHA256.HashData("sim14-world-seed"u8),
            configGeneration: 1,
            masterGeneration: masterGeneration,
            rateGeneration: 1);
        return new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            configDigest);
    }

    private static ByteString Bytes(OpaqueId128 id) => ByteString.CopyFrom(id.ToBytes());
    private static OpaqueId128 Id(int value) => OpaqueId128.Parse(value.ToString("x32"));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireProtocolCode(Action action, string code, string test)
    {
        try
        {
            action();
        }
        catch (CoreGatewayProtocolException ex) when (ex.Code.Value == code)
        {
            return;
        }
        throw new InvalidOperationException($"{test}: expected {code}.");
    }

    private static async Task RequireProtocolCodeAsync(Func<Task> action, string code, string test)
    {
        try
        {
            await action();
        }
        catch (CoreGatewayProtocolException ex) when (ex.Code.Value == code)
        {
            return;
        }
        throw new InvalidOperationException($"{test}: expected {code}.");
    }

    private sealed class PersistingIngress : ICoreGatewayDurableOperationIngressV1
    {
        private readonly SqlitePersistenceStore _store;
        private readonly OpaqueId128 _worldId;

        internal PersistingIngress(SqlitePersistenceStore store, OpaqueId128 worldId)
        {
            _store = store;
            _worldId = worldId;
        }

        internal int NewDurableAcceptanceCount { get; private set; }

        public async Task<OperationDurableObservationV1> SubmitDurablyAsync(
            StandardOperationV1 operation,
            CancellationToken cancellationToken = default)
        {
            if (operation.OperationKind == "test.reject")
                throw new CoreGatewayProtocolException("request.invalid", "SIM-14 per-operation rejection fixture.");
            var operationId = OpaqueId128.FromBytes(operation.OperationId.Span);
            var digest = operation.ImmutablePayloadDigest.ToByteArray();
            var anchor = await _store.ReadHistoryAnchorAsync(cancellationToken);
            var history = HistoryRecordMaterial.Create(
                _worldId,
                anchor.Sequence + 1,
                anchor.Digest,
                "operation.accepted.v1",
                "persistence.operation-accepted",
                1,
                0,
                payloadBytes: [0x14, 0xac],
                writeNormalizedPayload: writer =>
                {
                    writer.WriteMapStart(2);
                    writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteBytes(digest);
                });
            var accepted = await _store.PersistAcceptedOperationAsync(operationId, digest, history, cancellationToken);
            var duplicate = accepted.Status == DurableAcceptanceStatus.Duplicate;
            if (!duplicate) NewDurableAcceptanceCount++;
            return new OperationDurableObservationV1(
                OperationLifecycleStateV1.AcceptedDurable,
                duplicate,
                accepted.AcceptedSequence,
                ScheduledSequence: null,
                EffectiveStep: null,
                TerminalSequence: null,
                TerminalStatus: null,
                ResultCode: null);
        }
    }

    private sealed class FixedProjectionSource : ICoreStateProjectionSourceV1
    {
        private readonly byte[] _schemaDigest;
        internal FixedProjectionSource(byte[] schemaDigest) => _schemaDigest = schemaDigest.ToArray();

        public CoreProjectionFrameV1 BuildFull(WorldStateV1 state)
            => new(
                [
                    Projection(Id(0x1452), revision: state.Header.Step + 1, mutation: 1, payload: "second"),
                    Projection(Id(0x1451), revision: state.Header.Step + 1, mutation: 1, payload: "first"),
                ],
                _schemaDigest);

        public CoreProjectionFrameV1 BuildDelta(WorldStateV1 previousState, WorldStateV1 currentState)
            => new(
                [Projection(Id(0x1451), revision: currentState.Header.Step + 1, mutation: 1, payload: "updated")],
                _schemaDigest);
    }

    private sealed class OversizedProjectionSource : ICoreStateProjectionSourceV1
    {
        private readonly byte[] _schemaDigest;
        internal OversizedProjectionSource(byte[] schemaDigest) => _schemaDigest = schemaDigest.ToArray();

        public CoreProjectionFrameV1 BuildFull(WorldStateV1 state)
            => new(
                [new ProjectionRecordV1
                {
                    RecordSchemaId = "projection.sim14",
                    RecordSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
                    RecordId = Bytes(Id(0x1459)),
                    RecordRevision = 1,
                    MutationKind = (ProjectionMutationKindV1)1,
                    Payload = ByteString.CopyFrom(new byte[CoreGatewayProtocolRegistryV1.MaxPublicationChunkBytes]),
                }],
                _schemaDigest);

        public CoreProjectionFrameV1 BuildDelta(WorldStateV1 previousState, WorldStateV1 currentState)
            => throw new NotSupportedException();
    }

    private static ProjectionRecordV1 Projection(OpaqueId128 id, ulong revision, int mutation, string payload)
        => new()
        {
            RecordSchemaId = "projection.sim14",
            RecordSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            RecordId = Bytes(id),
            RecordRevision = revision,
            MutationKind = (ProjectionMutationKindV1)mutation,
            Payload = ByteString.CopyFromUtf8(payload),
        };

    private sealed class SequentialPublicationIds : IProtocolPublicationIdSourceV1
    {
        private int _next;
        internal SequentialPublicationIds(int first = 0x1450) => _next = first;
        public OpaqueId128 Next() => Id(Interlocked.Increment(ref _next));
    }
}
