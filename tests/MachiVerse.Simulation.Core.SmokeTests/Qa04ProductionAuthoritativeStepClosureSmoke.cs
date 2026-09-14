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
        Require(effectiveStep == Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep &&
                bindings.All(binding => binding.ScheduledOperation.EffectiveStep == effectiveStep),
            "Gate2 production workload must be effective at canonical State(S)=1.");

        var assembly = Qa04ProductionReferenceWorldAssemblerV1.AssembleCanonical(effectiveStep);
        var initial = assembly.MutationState;
        var references = assembly.References;
        var partitionAuthorityState = assembly.PartitionAuthorityState;
        var activeTransactions = assembly.ActiveTransactions;
        Require(partitionAuthorityState.Partitions.CanonicalEntries.Count() == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Gate2 production closure requires the full 97-partition WorldState surface.");
        Require(assembly.Validation.ResidentCount == Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount &&
                assembly.Validation.ParticipationControlModeCount == Qa04ParticipationControlModeCanonicalAuthorityV1.CanonicalCount &&
                assembly.Validation.PhysicalPresenceCount == Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalPhysicalCount &&
                assembly.Validation.EnvironmentD0Count == Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count &&
                assembly.Validation.EnvironmentD1Count == Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count &&
                assembly.Validation.SocietyGovernanceCount == Qa04SocietyGovernanceReferenceDecompositionV1.CanonicalCount &&
                assembly.Validation.InfrastructureInformationCount == Qa04InfrastructureReferenceDecompositionV1.CanonicalCount &&
                assembly.Validation.ActiveTransactionCount == Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount &&
                assembly.Validation.CanonicalInitialRecordCount == Qa04ReferenceLoadV1.CanonicalInitialRecordCount,
            "Gate2 production State(S) did not satisfy the full reference-world authority contract.");

        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: effectiveStep,
            freezeStep: null,
            bindings.Select(static binding => binding.ScheduledOperation));

        var root = Path.Combine(Path.GetTempPath(), "machiverse-qa04-production-step-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = PersistenceLayout.Resolve(root, Qa04ReferenceLoadV1.WorldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);
            await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths);
            var initialContinuity = await InitializePersistenceGenesisAsync(store, partitionAuthorityState);

            var policy = Qa04CanonicalOperationDurableAdmissionV1.CreateCanonicalPolicy(1);
            foreach (var binding in bindings)
            {
                var admission = await Qa04CanonicalOperationDurableAdmissionV1.AdmitAndScheduleAsync(
                    store, binding, policy, nextSchedulableStep: effectiveStep);
                Require(admission.Passed,
                    "Gate2 production workload did not cross durable ACCEPTED/SCHEDULED authority.");
            }

            var durableBefore = await store.ListOperationStatesCanonicalAsync();
            Require(durableBefore.Count == bindings.Length &&
                    durableBefore.All(operation => operation.Lifecycle == DurableOperationLifecycleV1.ScheduledDurable),
                "Gate2 production durable catalog must contain all scheduled canonical Operations before State(S) freeze.");

            ExpectInvalid(
                () => Qa04ProductionStepBasisAuthorityV1.ValidateBoundCoreAuthorityV2(
                    partitionAuthorityState,
                    scheduler,
                    durableBefore,
                    activeTransactions),
                "qa04.production-step.scheduler-substate-mismatch");

            var basisState = Qa04ProductionStepBasisAuthorityV1.BindCoreAuthorityV2(
                partitionAuthorityState,
                scheduler,
                durableBefore,
                activeTransactions);
            Qa04ProductionStepBasisAuthorityV1.ValidateBoundCoreAuthorityV2(
                basisState,
                scheduler,
                durableBefore,
                activeTransactions);
            await PersistStepOneAsync(store, basisState, initialContinuity);

            var recoveryAtBasis = await store.ReadRecoveryHeadAsync();
            Require(recoveryAtBasis.FinalizedStep == effectiveStep,
                "Gate2 production State(S) must be the durable finalized Step-1 authority before freeze.");

            var frozen = StepInputFreezerV1.Freeze(basisState, scheduler);
            Require(frozen.ScheduledOperations.Count == bindings.Length,
                "Gate2 production closure must freeze all 5,000 canonical Operations.");

            var runtimeOutputs = await DomainRuntimeExecutorV1.ExecuteAsync(
                StandardDomainExecutionPlanV1.Create(),
                basisState,
                frozen,
                CreateProductionRuntimes(),
                workerCount: 4);
            Require(runtimeOutputs.Count == 8 && runtimeOutputs.All(output => output.BasisStep == effectiveStep),
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
                    !preparation.Candidate.IsPublishable && !preparation.PreparedState.IsPublishable,
                "Gate2 production preparation authority drifted before COMMIT.");

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
                terminals,
                crossDomainTransactions: activeTransactions);

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

            var gate3Core = await Qa04ProductionCoreSnapshotSectionProofRunnerV1.VerifyAsync(
                finalized.AuthoritativeState,
                store);
            Require(gate3Core.BasisStep == verification.ResultingStep &&
                    gate3Core.CoreSectionCount == 6 &&
                    gate3Core.DurableOperationCount == bindings.Length &&
                    gate3Core.ScheduledOperationCount == 0 &&
                    gate3Core.CrossDomainTransactionCount == activeTransactions.Count,
                "Gate3 Step1 production six-Core-section proof failed.");

            Console.WriteLine(
                $"qa04-production-authoritative-step-pass operations={bindings.Length} domains={runtimeOutputs.Count} changedPartitions={verification.ChangedPartitionCount} partitions={verification.PartitionCount} activeTransactions={activeTransactions.Count} referenceRecords={assembly.Validation.CanonicalInitialRecordCount} resultingStep={verification.ResultingStep} coreSections={gate3Core.CoreSectionCount}");
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
            return ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>(Array.Empty<MutationIntentCandidateV1>());
        }

        static ValueTask<IReadOnlyList<PartitionCandidateV1>> NoResidentPartitionCandidates(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>(Array.Empty<PartitionCandidateV1>());
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

    private static async Task<byte[]> InitializePersistenceGenesisAsync(
        SqlitePersistenceStore store,
        WorldStateV1 state)
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
            Qa04ReferenceLoadV1.WorldId, genesis.RecordDigest);
        await store.InitializeWorldMetadataAsync(
            new WorldPersistenceMetadataSeed(
                Qa04ReferenceLoadV1.WorldId,
                PersistenceGeneration: 1,
                Qa04ReferenceLoadV1.WorldSeed,
                initialContinuity,
                state.Header.ConfigGeneration,
                state.Diagnostic.ConfigDigest,
                state.Header.MasterGeneration),
            genesis);
        return initialContinuity;
    }

    private static async Task PersistStepOneAsync(
        SqlitePersistenceStore store,
        WorldStateV1 basisState,
        byte[] initialContinuity)
    {
        var anchor = await store.ReadHistoryAnchorAsync();
        var transition = HistoryRecordMaterial.Create(
            Qa04ReferenceLoadV1.WorldId,
            checked(anchor.Sequence + 1),
            anchor.Digest,
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

    private static void ExpectInvalid(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (string.Equals(ex.Message, expectedMessage, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Expected InvalidDataException '{expectedMessage}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
