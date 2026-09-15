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
    private const string ProductionOptInVariable = "MACHIVERSE_QA04_PRODUCTION_CLOSURE";

    internal static async Task RunAsync()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(ProductionOptInVariable),
                "1",
                StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"qa04-production-authoritative-step-skip reason=explicit-opt-in-required env={ProductionOptInVariable}");
            return;
        }

        Console.WriteLine("[qa04-production] contracts start");
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

        Console.WriteLine("[qa04-production] reference-world assembly start");
        var assembly = Qa04ProductionReferenceWorldAssemblerV1.AssembleCanonical(effectiveStep);
        var initial = assembly.MutationState;
        var references = assembly.References;
        var partitionAuthorityState = assembly.PartitionAuthorityState;
        var activeTransactions = assembly.ActiveTransactions;
        Require(partitionAuthorityState.Partitions.CanonicalEntries.Count() == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Gate2 production closure requires the full 97-partition WorldState surface.");
        Require(assembly.BasisDomainAuthorities.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Gate3 Step2 requires the exact 97 production material authorities at State(S).");
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
        Console.WriteLine(
            $"[qa04-production] reference-world assembly complete records={assembly.Validation.CanonicalInitialRecordCount} transactions={activeTransactions.Count} domainAuthorities={assembly.BasisDomainAuthorities.Count}");

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
            var initialContinuity = await InitializePersistenceGenesisAsync(
                store,
                partitionAuthorityState,
                activeTransactions);

            Console.WriteLine($"[qa04-production] durable admission start operations={bindings.Length}");
            var policy = Qa04CanonicalOperationDurableAdmissionV1.CreateCanonicalPolicy(1);
            foreach (var binding in bindings)
            {
                var admission = await Qa04CanonicalOperationDurableAdmissionV1.AdmitAndScheduleAsync(
                    store, binding, policy, nextSchedulableStep: effectiveStep);
                Require(admission.Passed,
                    "Gate2 production workload did not cross durable ACCEPTED/SCHEDULED authority.");
            }
            Console.WriteLine($"[qa04-production] durable admission complete operations={bindings.Length}");

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

            Console.WriteLine("[qa04-production] basis durability start");
            _ = await Qa04ProductionBasisPersistenceV1.PersistAsync(
                store,
                basisState,
                initialContinuity,
                activeTransactions);
            Console.WriteLine("[qa04-production] basis durability complete");

            var recoveryAtBasis = await store.ReadRecoveryHeadAsync();
            Require(recoveryAtBasis.FinalizedStep == effectiveStep,
                "Gate2 production State(S) must be the durable finalized Step-1 authority before freeze.");

            var frozen = StepInputFreezerV1.Freeze(basisState, scheduler);
            Require(frozen.ScheduledOperations.Count == bindings.Length,
                "Gate2 production closure must freeze all 5,000 canonical Operations.");

            Console.WriteLine("[qa04-production] domain runtime start");
            var runtimeOutputs = await DomainRuntimeExecutorV1.ExecuteAsync(
                StandardDomainExecutionPlanV1.Create(),
                basisState,
                frozen,
                CreateProductionRuntimes(),
                workerCount: 4);
            Require(runtimeOutputs.Count == 8 && runtimeOutputs.All(output => output.BasisStep == effectiveStep),
                "Gate2 production closure must execute the ordinary eight-domain runtime path.");
            Console.WriteLine($"[qa04-production] domain runtime complete domains={runtimeOutputs.Count}");

            Console.WriteLine("[qa04-production] typed mutation start");
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
            Console.WriteLine($"[qa04-production] typed mutation complete changes={mutation.Changes.Count} families={mutation.AppliedCountByFamily.Count}");

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
            Console.WriteLine("[qa04-production] authoritative COMMIT start");
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
            Console.WriteLine($"[qa04-production] authoritative COMMIT complete resultingStep={verification.ResultingStep}");

            Console.WriteLine("[qa04-production] Gate3 Step1 six-Core recovery proof start");
            var gate3Core = await Qa04ProductionCoreSnapshotSectionProofRunnerV1.VerifyAsync(
                finalized.AuthoritativeState,
                store);
            Require(gate3Core.BasisStep == verification.ResultingStep &&
                    gate3Core.CoreSectionCount == 6 &&
                    gate3Core.DurableOperationCount == bindings.Length &&
                    gate3Core.ScheduledOperationCount == 0 &&
                    gate3Core.CrossDomainTransactionCount == activeTransactions.Count,
                "Gate3 Step1 production six-Core-section proof failed.");

            Console.WriteLine("[qa04-production] Gate3 Step2 exact-97 Domain section proof start");
            var gate3Domains = Qa04ProductionDomainSnapshotSectionProofRunnerV1.Verify(
                finalized.AuthoritativeState,
                assembly.BasisDomainAuthorities,
                mutation.State);
            Require(gate3Domains.BasisStep == verification.ResultingStep &&
                    gate3Domains.DomainSectionCount == StandardDomainPartitionRegistry.StandardPartitionCount &&
                    gate3Domains.ChangedPartitionCount == verification.ChangedPartitionCount,
                "Gate3 Step2 production exact-97 Domain-section proof failed.");
            Console.WriteLine(
                $"[qa04-production] Gate3 Step2 exact-97 Domain section proof complete sections={gate3Domains.DomainSectionCount} logicalRecords={gate3Domains.LogicalRecordCount}");

            Console.WriteLine("[qa04-production] Gate3 Step3 exact-103 durable Snapshot proof start");
            var gate3Snapshot = await Qa04ProductionExact103SnapshotPersistenceProofRunnerV1.VerifyAsync(
                finalized.AuthoritativeState,
                assembly.BasisDomainAuthorities,
                mutation.State,
                store,
                paths);
            Require(gate3Snapshot.SnapshotStep == verification.ResultingStep &&
                    gate3Snapshot.SectionCount == SnapshotManifestValidation.StandardRequiredSectionCount &&
                    gate3Snapshot.CoreSectionCount == gate3Core.CoreSectionCount &&
                    gate3Snapshot.DomainSectionCount == gate3Domains.DomainSectionCount &&
                    gate3Snapshot.DomainLogicalRecordCount == gate3Domains.LogicalRecordCount &&
                    gate3Snapshot.ChunkCount > 0 &&
                    gate3Snapshot.RecoveredStateDigest.Length == 32,
                "Gate3 Step3-5 production exact-103 Snapshot/recovery proof failed.");
            Console.WriteLine(
                $"[qa04-production] Gate3 Step3 exact-103 durable Snapshot proof complete sections={gate3Snapshot.SectionCount} chunks={gate3Snapshot.ChunkCount} logicalRecords={gate3Snapshot.DomainLogicalRecordCount}");

            Console.WriteLine("[qa04-production] Gate3 Step6 replay / equivalence proof start");
            var gate3Replay = await Qa04ProductionGate3Step6ReplayEquivalenceProofRunnerV1.VerifyAsync(
                finalized,
                initial,
                bindings,
                references,
                gate3Snapshot,
                store,
                paths);
            Require(gate3Replay.BasisStep == effectiveStep &&
                    gate3Replay.ResultingStep == verification.ResultingStep &&
                    gate3Replay.HistorySequence == finalized.DurableReceipt.HistorySequence &&
                    gate3Replay.TerminalOperationCount == bindings.Length &&
                    gate3Replay.CrossDomainTransactionCount == activeTransactions.Count &&
                    gate3Replay.ReplayedStateDigest.AsSpan().SequenceEqual(gate3Replay.RecoveredStateDigest),
                "Gate3 Step6 production replay/equivalence proof failed.");
            Console.WriteLine(
                $"[qa04-production] Gate3 Step6 replay / equivalence proof complete resultingStep={gate3Replay.ResultingStep} terminalOperations={gate3Replay.TerminalOperationCount} transactions={gate3Replay.CrossDomainTransactionCount}");

            Console.WriteLine(
                $"qa04-production-authoritative-step-pass operations={bindings.Length} domains={runtimeOutputs.Count} changedPartitions={verification.ChangedPartitionCount} partitions={verification.PartitionCount} activeTransactions={activeTransactions.Count} referenceRecords={assembly.Validation.CanonicalInitialRecordCount} resultingStep={verification.ResultingStep} coreSections={gate3Core.CoreSectionCount} domainSections={gate3Domains.DomainSectionCount} domainLogicalRecords={gate3Domains.LogicalRecordCount} snapshotSections={gate3Snapshot.SectionCount} snapshotChunks={gate3Snapshot.ChunkCount} replayOperations={gate3Replay.TerminalOperationCount}");
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
        WorldStateV1 state,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions)
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
        await store.InitializeWorldMetadataWithCanonicalCrossDomainTransactionsAsync(
            new WorldPersistenceMetadataSeed(
                Qa04ReferenceLoadV1.WorldId,
                PersistenceGeneration: 1,
                Qa04ReferenceLoadV1.WorldSeed,
                initialContinuity,
                state.Header.ConfigGeneration,
                state.Diagnostic.ConfigDigest,
                state.Header.MasterGeneration),
            genesis,
            activeTransactions);
        return initialContinuity;
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
