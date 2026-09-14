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

internal static class Qa04ProductionAuthoritativeStepClosureSmoke
{
    private const string ResidentFamily = "participation-control-resident-action";
    private const string PhysicalFamily = "physical-item-movement-work";

    internal static async Task RunAsync()
    {
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();
        Qa04CanonicalOperationBindingV1.ValidateCanonicalContract();

        var bindings = Qa04ReferenceLoadV1.OperationsForStep(0)
            .Select(static descriptor => Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1))
            .OrderBy(static binding => binding.OrderKey)
            .ToArray();
        Require(bindings.Length == checked((int)Qa04ReferenceLoadV1.SteadyOperationsPerStep),
            "Gate2 production closure requires the exact 5,000-operation steady workload.");

        var effectiveStep = bindings[0].ScheduledOperation.EffectiveStep;
        Require(effectiveStep == 1 && bindings.All(binding => binding.ScheduledOperation.EffectiveStep == effectiveStep),
            "Gate2 production workload must be effective at State(S)=1.");

        var residentBindings = bindings
            .Where(static binding => binding.SourceDescriptor.FamilyToken.Value == ResidentFamily)
            .ToArray();
        var physicalBindings = bindings
            .Where(static binding => binding.SourceDescriptor.FamilyToken.Value == PhysicalFamily)
            .ToArray();

        var controlIdentity = StandardDomainPartitionRegistry.Get(ParticipationControlModePayloadV1.PartitionId);
        var controlModes = new DomainPartitionStateV1<ParticipationControlModePayloadV1>(
            controlIdentity,
            residentBindings.Select(binding =>
            {
                var ordinal = binding.SourceDescriptor.FamilyOrdinal;
                var resident = Qa04ReferenceLoadV1.Record(new StableToken("resident.persistent-identity"), ordinal);
                return new DomainRecordEnvelopeV1<ParticipationControlModePayloadV1>(
                    Qa04ParticipationControlModeCanonicalAuthorityV1.RecordId(ordinal),
                    controlIdentity.RecordSchema,
                    revision: 1,
                    createdStep: 0,
                    retiredStep: null,
                    Qa04ReferenceLoadV1.ResidentDetailLevel(ordinal),
                    lineageRef: null,
                    new ParticipationControlModePayloadV1(
                        new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId),
                        BindingRef: null,
                        Qa04ParticipationControlModeCanonicalAuthorityV1.Autonomous,
                        Qa04ParticipationControlModeCanonicalAuthorityV1.InitialEffectiveFrom,
                        Qa04ParticipationControlModeCanonicalAuthorityV1.InitialInputAuthorityGeneration));
            }));

        var physicalRecords = physicalBindings
            .Select(binding =>
            {
                var ordinal = binding.SourceDescriptor.FamilyOrdinal;
                return Qa04PhysicalD0MaterializerV1.Create(
                    ordinal,
                    Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.CreateCanonicalPresenceBinding(ordinal),
                    Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.CreateCanonicalTerrainBinding);
            })
            .ToArray();
        var presence = new DomainPartitionStateV1<PhysicalPresencePayloadV1>(
            StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId),
            physicalRecords.Select(static material => material.Presence));

        var markets = Enumerable.Range(0, checked((int)Qa04MarketMaterializerV1.CanonicalMarketStateCount))
            .Select(scope => Qa04MarketMaterializerV1.CreateMarketState(
                checked((uint)scope),
                Qa04SpatialTileScopeAuthorityV1.ScopeRef,
                out _))
            .ToArray();

        var initial = new Qa04CanonicalOperationMutationStateV1(
            new DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1>(
                StandardDomainPartitionRegistry.Get(InfrastructureServiceQueuePayloadV1.PartitionId),
                Array.Empty<DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1>>()),
            controlModes,
            new DomainPartitionStateV1<ResidentBehaviorStatePayloadV1>(
                StandardDomainPartitionRegistry.Get(ResidentBehaviorStatePayloadV1.PartitionId),
                Array.Empty<DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>>()),
            presence,
            new SocietyMarketTransactionPartitionStateV2(markets),
            new DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1>(
                StandardDomainPartitionRegistry.Get(GovernanceSecurityIncidentPayloadV1.PartitionId),
                Array.Empty<DomainRecordEnvelopeV1<GovernanceSecurityIncidentPayloadV1>>()),
            new DomainPartitionStateV1<EnvironmentHazardPayloadV1>(
                StandardDomainPartitionRegistry.Get(EnvironmentHazardPayloadV1.PartitionId),
                Array.Empty<DomainRecordEnvelopeV1<EnvironmentHazardPayloadV1>>()));

        var references = new RegistryResolver();
        var basisState = BuildBasisState(effectiveStep, initial, references);
        Require(basisState.Partitions.CanonicalEntries.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Gate2 production closure requires the full 97-partition WorldState surface.");

        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: effectiveStep,
            freezeStep: null,
            bindings.Select(static binding => binding.ScheduledOperation));
        var frozen = StepInputFreezerV1.Freeze(basisState, scheduler);
        Require(frozen.ScheduledOperations.Count == bindings.Length,
            "Gate2 production closure must freeze all 5,000 canonical Operations.");

        var runtimeOutputs = await DomainRuntimeExecutorV1.ExecuteAsync(
            StandardDomainExecutionPlanV1.Create(),
            basisState,
            frozen,
            CreateProductionRuntimes(),
            workerCount: 4);
        Require(runtimeOutputs.Count == 8 &&
                runtimeOutputs.All(output => output.BasisStep == effectiveStep),
            "Gate2 production closure must execute the ordinary eight-domain runtime path.");

        var mutation = Qa04CanonicalOperationMutationBatchV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            bindings,
            initial,
            references);
        Require(mutation.AppliedOperationIds.Count == bindings.Length && mutation.Changes.Count == bindings.Length,
            "Gate2 production closure must apply every canonical Operation through a typed handler.");
        Require(mutation.AppliedCountByFamily.Count == 6,
            "Gate2 production closure must apply all six canonical Operation families.");

        var preparation = Qa04ProductionAuthoritativeStepPreparationV1.Prepare(
            OpaqueId128.Parse("0000000000000000000000000000f213"),
            basisState,
            frozen,
            bindings,
            mutation,
            references,
            runtimeOutputs);
        Require(preparation.Candidate.DomainOutputs.Count == 8 &&
                preparation.Candidate.PartitionCandidates.Count == 6 &&
                preparation.Candidate.FrozenInput.ScheduledOperations.Count == bindings.Length &&
                !preparation.Candidate.IsPublishable &&
                !preparation.PreparedState.IsPublishable,
            "Gate2 production preparation authority drifted before COMMIT.");

        var root = Path.Combine(Path.GetTempPath(), "machiverse-qa04-production-step-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = PersistenceLayout.Resolve(root, Qa04ReferenceLoadV1.WorldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);
            await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths);
            await InitializePersistenceAtStepOneAsync(store, basisState);

            var policy = Qa04CanonicalOperationDurableAdmissionV1.CreateCanonicalPolicy(1);
            foreach (var binding in bindings)
            {
                var admission = await Qa04CanonicalOperationDurableAdmissionV1.AdmitAndScheduleAsync(
                    store,
                    binding,
                    policy,
                    nextSchedulableStep: effectiveStep);
                Require(admission.Passed,
                    "Gate2 production workload did not cross durable ACCEPTED/SCHEDULED authority.");
            }

            var durableBefore = await store.ListOperationStatesCanonicalAsync();
            Require(durableBefore.Count == bindings.Length &&
                    durableBefore.All(operation => operation.Lifecycle == DurableOperationLifecycleV1.ScheduledDurable),
                "Gate2 production durable catalog must contain all scheduled canonical Operations before COMMIT.");

            var terminals = bindings
                .Select(static binding => new TerminalOperationCommit(
                    binding.SourceDescriptor.OperationId,
                    (int)CoreOperationResultStatusV1.Success,
                    "operation.succeeded"))
                .ToArray();
            var finalized = await Qa04CanonicalOperationStepFinalizationV1.CommitAndPublishAsync(
                store,
                scheduler,
                preparation,
                terminals);

            var verification = finalized.PostCommitVerification
                ?? throw new InvalidOperationException("Gate2 production post-COMMIT verification missing.");
            Require(finalized.DurableReceipt.IsPublishable &&
                    finalized.AuthoritativeState.IsPublishable &&
                    finalized.TerminalOperations.Count == bindings.Length &&
                    verification.ResultingStep == effectiveStep + 1UL &&
                    verification.PartitionCount == StandardDomainPartitionRegistry.StandardPartitionCount &&
                    verification.ChangedPartitionCount == 6 &&
                    verification.TerminalOperationCount == bindings.Length &&
                    scheduler.FreezeStep is null &&
                    scheduler.NextSchedulableStep == effectiveStep + 1UL,
                "Gate2 production full-runtime post-COMMIT authority closure failed.");

            Console.WriteLine(
                $"qa04-production-authoritative-step-pass operations={bindings.Length} domains={runtimeOutputs.Count} changedPartitions={verification.ChangedPartitionCount} partitions={verification.PartitionCount} resultingStep={verification.ResultingStep}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static IReadOnlyCollection<IDomainRuntimeV1> CreateProductionRuntimes()
    {
        static ValueTask<IReadOnlyList<MutationIntentCandidateV1>> NoIntents(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.FrozenInput.ScheduledOperations.Count != checked((int)Qa04ReferenceLoadV1.SteadyOperationsPerStep))
                throw new InvalidDataException("qa04.production-step.runtime-workload-count-drift");
            return ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>(
                Array.Empty<MutationIntentCandidateV1>());
        }

        static ValueTask<IReadOnlyList<PartitionCandidateV1>> NoResidentPartitionCandidates(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>(
                Array.Empty<PartitionCandidateV1>());
        }

        return
        [
            new SpatialDomainRuntimeV1(NoIntents),
            new EnvironmentDomainRuntimeV1(NoIntents),
            new PhysicalBuiltDomainRuntimeV1(NoIntents),
            new ParticipationDomainRuntimeV1(NoIntents),
            new ResidentDomainRuntimeV1(NoIntents, NoResidentPartitionCandidates),
            new SocietyEconomyDomainRuntimeV1(NoIntents),
            new GovernanceSecurityDomainRuntimeV1(NoIntents),
            new InfrastructureInformationDomainRuntimeV1(NoIntents),
        ];
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

    private static WorldStateV1 BuildBasisState(
        ulong basisStep,
        Qa04CanonicalOperationMutationStateV1 initial,
        IDomainRecordSchemaResolverV1 references)
    {
        // Materialize the resident slice actually referenced by this Step. Completion of the other
        // production reference-world classes is required above through the canonical material
        // contract and is not replaced by a synthetic/reduced completion claim here.
        var template = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(
            Qa04ReferenceLoadV1.SteadyOperationsPerStep).WorldState;
        var replacements = new Dictionary<string, PartitionStateHeaderV1>(StringComparer.Ordinal)
        {
            [InfrastructureServiceQueuePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.InfrastructureServiceQueue, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    InfrastructureServiceQueuePayloadV1.PartitionId, payload.ToStandardPayload(), references: references)),
            [ParticipationControlModePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.ParticipationControlMode, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    ParticipationControlModePayloadV1.PartitionId, payload.ToStandardPayload(), references: references)),
            [ResidentBehaviorStatePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.ResidentBehaviorState, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    ResidentBehaviorStatePayloadV1.PartitionId, payload.ToStandardPayload(), references: references)),
            [PhysicalPresencePayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.PhysicalPresence, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    PhysicalPresencePayloadV1.PartitionId, payload.ToStandardPayload(), references: references)),
            [SocietyMarketTransactionRecordSchemaV2.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.MarketTransaction.State, 1, basisStep, DetailLevelV1.D2RegionalAggregate,
                payload => SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(payload, references)),
            [GovernanceSecurityIncidentPayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.GovernanceSecurityIncident, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    GovernanceSecurityIncidentPayloadV1.PartitionId, payload.ToStandardPayload(), references: references)),
            [EnvironmentHazardPayloadV1.PartitionId] = PartitionStateHeaderV1.CreateCanonical(
                initial.EnvironmentHazard, 1, basisStep, DetailLevelV1.D0Entity,
                payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                    EnvironmentHazardPayloadV1.PartitionId, payload.ToStandardPayload(), references: references)),
        };
        var partitions = template.Partitions.CanonicalEntries
            .Select(entry => replacements.TryGetValue(entry.Header.PartitionId.Value, out var replacement)
                ? new PartitionStateRefV1(replacement)
                : entry)
            .ToArray();
        return new WorldStateV1(
            new WorldStateHeaderV1(
                template.Header.WorldId,
                basisStep,
                template.Header.WorldSeedDigest,
                template.Header.ConfigGeneration,
                template.Header.MasterGeneration,
                template.Header.RateGeneration),
            new OrderedPartitionDirectoryV1(partitions),
            template.SchedulerState,
            template.OperationState,
            template.DetailState,
            template.DomainRegistryState,
            template.Diagnostic.ConfigDigest);
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
