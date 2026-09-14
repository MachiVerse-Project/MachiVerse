using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04CanonicalOperationStepFinalizationSmoke
{
    private const string ResidentFamily = "participation-control-resident-action";
    private const string PhysicalFamily = "physical-item-movement-work";
    private const string MarketFamily = "society-market-payment-contract";

    internal static async Task RunAsync()
    {
        var bindings = Qa04ReferenceLoadV1.OperationsForStep(0)
            .GroupBy(static descriptor => descriptor.FamilyToken.Value, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Select(static descriptor => Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1))
            .OrderBy(static binding => binding.OrderKey)
            .ToArray();
        Require(bindings.Length == 6, "Gate2 finalization smoke requires all six canonical Operation families.");

        var effectiveStep = bindings[0].ScheduledOperation.EffectiveStep;
        Require(effectiveStep == 1 && bindings.All(binding => binding.ScheduledOperation.EffectiveStep == effectiveStep),
            "Gate2 finalization smoke requires canonical Operations effective at Step 1.");

        var residentBinding = bindings.Single(static value => value.SourceDescriptor.FamilyToken.Value == ResidentFamily);
        var physicalBinding = bindings.Single(static value => value.SourceDescriptor.FamilyToken.Value == PhysicalFamily);
        var marketBinding = bindings.Single(static value => value.SourceDescriptor.FamilyToken.Value == MarketFamily);

        var residentOrdinal = residentBinding.SourceDescriptor.FamilyOrdinal;
        var resident = Qa04ReferenceLoadV1.Record(new StableToken("resident.persistent-identity"), residentOrdinal);
        var residentRef = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
        var controlIdentity = StandardDomainPartitionRegistry.Get(ParticipationControlModePayloadV1.PartitionId);
        var controlMode = new DomainRecordEnvelopeV1<ParticipationControlModePayloadV1>(
            Qa04ParticipationControlModeCanonicalAuthorityV1.RecordId(residentOrdinal),
            controlIdentity.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            Qa04ReferenceLoadV1.ResidentDetailLevel(residentOrdinal),
            lineageRef: null,
            new ParticipationControlModePayloadV1(
                residentRef,
                BindingRef: null,
                Qa04ParticipationControlModeCanonicalAuthorityV1.Autonomous,
                Qa04ParticipationControlModeCanonicalAuthorityV1.InitialEffectiveFrom,
                Qa04ParticipationControlModeCanonicalAuthorityV1.InitialInputAuthorityGeneration));
        var controlModes = new DomainPartitionStateV1<ParticipationControlModePayloadV1>(
            controlIdentity,
            new[] { controlMode });

        var serviceQueue = new DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1>(
            StandardDomainPartitionRegistry.Get(InfrastructureServiceQueuePayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1>>());
        var behavior = new DomainPartitionStateV1<ResidentBehaviorStatePayloadV1>(
            StandardDomainPartitionRegistry.Get(ResidentBehaviorStatePayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>>());
        var physical = PhysicalMaterial(physicalBinding.SourceDescriptor.FamilyOrdinal);
        var presence = new DomainPartitionStateV1<PhysicalPresencePayloadV1>(
            StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId),
            new[] { physical.Presence });
        var marketScope = checked((uint)(marketBinding.SourceDescriptor.FamilyOrdinal %
            (ulong)Qa04ReferenceScenariosV1.MarketScopeCount));
        var market = Qa04MarketMaterializerV1.CreateMarketState(
            marketScope,
            Qa04SpatialTileScopeAuthorityV1.ScopeRef,
            out _);
        var marketState = new SocietyMarketTransactionPartitionStateV2(new[] { market });
        var incidents = new DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1>(
            StandardDomainPartitionRegistry.Get(GovernanceSecurityIncidentPayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<GovernanceSecurityIncidentPayloadV1>>());
        var hazards = new DomainPartitionStateV1<EnvironmentHazardPayloadV1>(
            StandardDomainPartitionRegistry.Get(EnvironmentHazardPayloadV1.PartitionId),
            Array.Empty<DomainRecordEnvelopeV1<EnvironmentHazardPayloadV1>>());

        var initial = new Qa04CanonicalOperationMutationStateV1(
            serviceQueue,
            controlModes,
            behavior,
            presence,
            marketState,
            incidents,
            hazards);
        var references = new RegistryResolver();
        var basisState = BuildBasisState(effectiveStep, initial, references);
        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: effectiveStep,
            freezeStep: null,
            bindings.Select(static binding => binding.ScheduledOperation));
        var frozen = StepInputFreezerV1.Freeze(basisState, scheduler);
        Require(frozen.BasisStep == effectiveStep && frozen.ScheduledOperations.Count == 6,
            "Gate2 finalization smoke must freeze the six actual canonical Operations at State(S).");

        var mutation = Qa04CanonicalOperationMutationBatchV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            bindings,
            initial,
            references);
        var preparation = Qa04CanonicalOperationStepPreparationV1.Prepare(
            OpaqueId128.Parse("0000000000000000000000000000f208"),
            basisState,
            frozen,
            bindings,
            mutation,
            references,
            invariantResults:
            [
                new InvariantResultV1(
                    new StableToken("qa04.full-step.commit-path"),
                    InvariantSeverityV1.CommitBlocking,
                    InvariantOutcomeV1.Pass),
            ]);
        Require(preparation.Candidate.BasisStep == effectiveStep &&
                preparation.Candidate.TargetStep == effectiveStep + 1UL &&
                preparation.Candidate.PartitionCandidates.Count == 6 &&
                preparation.Candidate.FrozenInput.ScheduledOperations.Count == 6,
            "Gate2 Steps 1-5 did not preserve the actual frozen Operation set into StepCandidate.");
        Require(!preparation.Candidate.IsPublishable && !preparation.PreparedState.IsPublishable,
            "Gate2 prepared authority must remain non-publishable before SQLite COMMIT.");

        var root = Path.Combine(Path.GetTempPath(), "machiverse-qa04-full-step-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = PersistenceLayout.Resolve(root, Qa04ReferenceLoadV1.WorldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);
            await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths);
            await InitializePersistenceAtStepOneAsync(store, basisState);
            await PersistCanonicalOperationsAsync(store, bindings);

            var before = await store.ReadRecoveryHeadAsync();
            Require(before.FinalizedStep == effectiveStep,
                "Gate2 fixture persistence head must match State(S) before actual transition COMMIT.");
            foreach (var binding in bindings)
            {
                var durable = await store.ReadOperationStateAsync(binding.SourceDescriptor.OperationId)
                    ?? throw new InvalidOperationException("Scheduled canonical Operation missing before Gate2 COMMIT.");
                Require(durable.Lifecycle == DurableOperationLifecycleV1.ScheduledDurable &&
                        durable.EffectiveStep == effectiveStep,
                    "Canonical Operation must be durably scheduled for State(S) before Gate2 COMMIT.");
            }

            var terminals = bindings
                .Select(static binding => new TerminalOperationCommit(
                    binding.SourceDescriptor.OperationId,
                    (int)CoreOperationResultStatusV1.Success,
                    "operation.succeeded"))
                .ToArray();

            var rejected = false;
            try
            {
                _ = await Qa04CanonicalOperationStepFinalizationV1.CommitAndPublishAsync(
                    store,
                    scheduler,
                    preparation,
                    terminals.Take(terminals.Length - 1).ToArray());
            }
            catch (InvalidDataException ex) when (ex.Message == "qa04.full-step.finalization-terminal-coverage-drift")
            {
                rejected = true;
            }
            Require(rejected,
                "Gate2 finalization must reject incomplete terminal Operation coverage before COMMIT.");
            var afterRejected = await store.ReadRecoveryHeadAsync();
            Require(afterRejected.FinalizedStep == effectiveStep &&
                    scheduler.FreezeStep == effectiveStep &&
                    scheduler.ForEffectiveStep(effectiveStep).Count == 6,
                "Rejected Gate2 finalization must leave State(S) scheduler/persistence authority unchanged.");

            var result = await Qa04CanonicalOperationStepFinalizationV1.CommitAndPublishAsync(
                store,
                scheduler,
                preparation,
                terminals);
            Require(result.DurableReceipt.IsPublishable &&
                    result.DurableReceipt.BasisStep == effectiveStep &&
                    result.DurableReceipt.ResultingStep == effectiveStep + 1UL,
                "Gate2 Step 7 did not establish a durable Step receipt for State(S+1).");
            Require(result.TerminalOperations.Count == 6 &&
                    result.TerminalOperations.All(static operation =>
                        operation.Lifecycle == DurableOperationLifecycleV1.TerminalDurable &&
                        operation.TerminalStatus == (int)CoreOperationResultStatusV1.Success &&
                        operation.ResultCode == "operation.succeeded"),
                "Gate2 Step 6 durable Operation terminal state drifted.");
            Require(scheduler.FreezeStep is null &&
                    scheduler.NextSchedulableStep == effectiveStep + 1UL &&
                    scheduler.ForEffectiveStep(effectiveStep).Count == 0,
                "Gate2 scheduler must reopen and retire State(S) Operations only after COMMIT.");
            Require(result.AuthoritativeState.IsPublishable &&
                    result.AuthoritativeState.State.Header.Step == effectiveStep + 1UL &&
                    result.AuthoritativeState.State.Header.PreviousStateDigest is not null &&
                    result.AuthoritativeState.State.Header.PreviousStateDigest.AsSpan().SequenceEqual(
                        basisState.Diagnostic.StateDigest),
                "Gate2 Step 8 did not publish the exact prepared State(S+1) after COMMIT.");

            foreach (var candidate in preparation.Candidate.PartitionCandidates)
            {
                var preparedHeader = preparation.PreparedState.ResultingState.Partitions.Get(candidate.PartitionId.Value).Header;
                var publishedHeader = result.AuthoritativeState.State.Partitions.Get(candidate.PartitionId.Value).Header;
                Require(publishedHeader.Revision == preparedHeader.Revision &&
                        publishedHeader.BasisStep == preparedHeader.BasisStep &&
                        publishedHeader.CanonicalDigest.AsSpan().SequenceEqual(preparedHeader.CanonicalDigest),
                    $"Published Gate2 partition drifted after COMMIT: {candidate.PartitionId.Value}");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task InitializePersistenceAtStepOneAsync(
        SqlitePersistenceStore store,
        WorldStateV1 basisState)
    {
        var genesis = HistoryRecordMaterial.Create(
            Qa04ReferenceLoadV1.WorldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "world.genesis.v1",
            payloadSchemaId: "core.world-genesis.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: Qa04ReferenceLoadV1.WorldId.ToBytes(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteBytes(Qa04ReferenceLoadV1.WorldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(Qa04ReferenceLoadV1.WorldSeed.ToBytes());
                writer.WriteUnsigned(2); writer.WriteUnsigned(0);
            });
        var initialContinuity = HistoryIntegrity.ComputeGenesisContinuityToken(
            Qa04ReferenceLoadV1.WorldId,
            genesis.RecordDigest);
        await store.InitializeWorldMetadataAsync(
            new WorldPersistenceMetadataSeed(
                Qa04ReferenceLoadV1.WorldId,
                PersistenceGeneration: 1,
                Qa04ReferenceLoadV1.WorldSeed,
                initialContinuity,
                basisState.Header.ConfigGeneration,
                basisState.Diagnostic.ConfigDigest,
                basisState.Header.MasterGeneration),
            genesis);

        var transition = HistoryRecordMaterial.Create(
            Qa04ReferenceLoadV1.WorldId,
            sequence: 2,
            previousRecordDigest: genesis.RecordDigest,
            recordType: "transition.committed.v1",
            payloadSchemaId: "persistence.transition-committed",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: basisState.Diagnostic.StateDigest,
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteUnsigned(0);
                writer.WriteUnsigned(1); writer.WriteUnsigned(1);
                writer.WriteUnsigned(2); writer.WriteBytes(basisState.Diagnostic.StateDigest);
            });
        var continuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            Qa04ReferenceLoadV1.WorldId,
            resultingStep: 1,
            initialContinuity,
            transition.RecordDigest);
        _ = await store.PersistTransitionCommitAsync(
            effectiveStep: 0,
            resultingStep: 1,
            continuity,
            basisState.Header.ConfigGeneration,
            basisState.Diagnostic.ConfigDigest,
            transition,
            Array.Empty<TerminalOperationCommit>());
    }

    private static async Task PersistCanonicalOperationsAsync(
        SqlitePersistenceStore store,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings)
    {
        var anchor = await store.ReadHistoryAnchorAsync();
        foreach (var binding in bindings)
        {
            var operationId = binding.SourceDescriptor.OperationId;
            var payloadDigest = binding.BoundDescriptor.PayloadDigest.ToArray();
            var accepted = HistoryRecordMaterial.Create(
                Qa04ReferenceLoadV1.WorldId,
                checked(anchor.Sequence + 1UL),
                anchor.Digest,
                recordType: "operation.accepted.v1",
                payloadSchemaId: "persistence.operation-accepted",
                payloadSchemaMajor: 1,
                payloadSchemaMinor: 0,
                payloadBytes: operationId.ToBytes().Concat(payloadDigest).ToArray(),
                writeNormalizedPayload: writer =>
                {
                    writer.WriteMapStart(2);
                    writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteBytes(payloadDigest);
                });
            _ = await store.PersistAcceptedOperationAsync(operationId, payloadDigest, accepted);
            anchor = new HistoryAnchor(accepted.Sequence, accepted.RecordDigest);

            var scheduled = HistoryRecordMaterial.Create(
                Qa04ReferenceLoadV1.WorldId,
                checked(anchor.Sequence + 1UL),
                anchor.Digest,
                recordType: "operation.scheduled.v1",
                payloadSchemaId: "persistence.operation-scheduled",
                payloadSchemaMajor: 1,
                payloadSchemaMinor: 0,
                payloadBytes: operationId.ToBytes().Concat(binding.OrderKey.ToDatabaseBytes()).ToArray(),
                writeNormalizedPayload: writer =>
                {
                    writer.WriteMapStart(3);
                    writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteUnsigned(binding.ScheduledOperation.EffectiveStep);
                    writer.WriteUnsigned(2); writer.WriteBytes(binding.OrderKey.ToDatabaseBytes());
                });
            _ = await store.PersistScheduledOperationAsync(
                operationId,
                binding.ScheduledOperation.EffectiveStep,
                binding.OrderKey,
                scheduled);
            anchor = new HistoryAnchor(scheduled.Sequence, scheduled.RecordDigest);
        }
    }

    private static WorldStateV1 BuildBasisState(
        ulong basisStep,
        Qa04CanonicalOperationMutationStateV1 initial,
        IDomainRecordSchemaResolverV1 references)
    {
        var template = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1).WorldState;
        var replacements = new Dictionary<string, PartitionStateHeaderV1>(StringComparer.Ordinal)
        {
            [InfrastructureServiceQueuePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.InfrastructureServiceQueue, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    InfrastructureServiceQueuePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            [ParticipationControlModePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.ParticipationControlMode, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    ParticipationControlModePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            [ResidentBehaviorStatePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.ResidentBehaviorState, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    ResidentBehaviorStatePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            [PhysicalPresencePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.PhysicalPresence, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    PhysicalPresencePayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            [SocietyMarketTransactionRecordSchemaV2.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.MarketTransaction.State, 1, basisStep, DetailLevelV1.D2RegionalAggregate,
                payload => SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(payload, references)),
            [GovernanceSecurityIncidentPayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.GovernanceSecurityIncident, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    GovernanceSecurityIncidentPayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
            [EnvironmentHazardPayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.EnvironmentHazard, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    EnvironmentHazardPayloadV1.PartitionId,
                    payload.ToStandardPayload(),
                    references: references)),
        };

        var partitions = template.Partitions.CanonicalEntries
            .Select(entry => replacements.TryGetValue(entry.Header.PartitionId.Value, out var replacement)
                ? new PartitionStateRefV1(replacement)
                : entry)
            .ToArray();
        var header = new WorldStateHeaderV1(
            template.Header.WorldId,
            basisStep,
            template.Header.WorldSeedDigest,
            template.Header.ConfigGeneration,
            template.Header.MasterGeneration,
            template.Header.RateGeneration);
        return new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitions),
            template.SchedulerState,
            template.OperationState,
            template.DetailState,
            template.DomainRegistryState,
            template.Diagnostic.ConfigDigest);
    }

    private static Qa04PhysicalD0RecordMaterialV1 PhysicalMaterial(ulong ordinal)
        => Qa04PhysicalD0MaterializerV1.Create(ordinal, PhysicalPresenceBinding(ordinal), TerrainBinding);

    private static Qa04PhysicalPresenceGenesisBindingV1 PhysicalPresenceBinding(ulong ordinal)
        => new(
            new PartitionRecordRefV1("resident.identity_lifecycle", Id(checked((byte)(1 + ordinal % 200)))),
            new PartitionRecordRefV1("spatial.world_frame", Id(0xF0)),
            new Vec3Int64V1(1000, 2000, 3000),
            new MachiVerse.Simulation.Core.Domains.QuaternionQ30V1(0, 0, 0, 1 << 30),
            new Vec3Int64V1(0, 0, 0),
            new Vec3Int64V1(0, 0, 0),
            null,
            new StableToken("active"));

    private static Qa04PhysicalTerrainRootBindingV1 TerrainBinding(ushort _)
        => new(
            new PartitionRecordRefV1("spatial.terrain_geometry", Id(0xE0)),
            new Vec3Int64V1(-500, -500, -500),
            new Vec3Int64V1(500, 500, 500));

    private static OpaqueId128 Id(byte suffix)
    {
        var bytes = new byte[16];
        bytes[^1] = suffix;
        return OpaqueId128.FromBytes(bytes);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RegistryResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference)
            => !reference.RecordId.IsZero && TryResolve(reference, out _);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => TryResolve(reference, out schema);

        private static bool TryResolve(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = default!;
            if (reference.RecordId.IsZero) return false;
            try
            {
                schema = reference.PartitionId.Value == SocietyMarketTransactionRecordSchemaV2.PartitionId
                    ? SocietyMarketTransactionRecordSchemaV2.RecordSchema
                    : StandardDomainPartitionRegistry.Get(reference.PartitionId.Value).RecordSchema;
                return true;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }
    }
}
