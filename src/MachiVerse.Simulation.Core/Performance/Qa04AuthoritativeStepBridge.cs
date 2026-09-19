using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04AuthoritativeStepProbeV1(
    ulong BasisStep,
    ulong ResultingStep,
    int WorkerCount,
    ulong ResidentRecordCount,
    int DomainOutputCount,
    int PartitionCandidateCount,
    bool CandidatePublishableBeforeCommit,
    bool PreparedStatePublishableBeforeCommit,
    bool DurableReceiptPublishable,
    bool ResultingWorldStatePublishable,
    bool SchedulerReopenedAfterCommit,
    bool RealSqliteCommitObserved,
    ulong ResultingResidentPartitionRevision,
    ulong ResultingResidentPartitionBasisStep,
    string CandidateDiagnosticDigest,
    string BasisStateDigest,
    string PreviousStateDigest,
    string ResultingStateDigest,
    string ResultingContinuityToken,
    string[] BlockingFailureCodes)
{
    public bool ReferenceWorldMaterialized => false;
    public bool AuthoritativeStepLoopAvailable => false;
    public bool ReleaseEvidenceCapable => false;
}

/// <summary>
/// Reduced QA-04 structural authority-path proof.
///
/// This bridge deliberately does not emit performance evidence and does not claim the canonical
/// reference world or full 27,000-Step benchmark loop is assembled. It exists to prove that a
/// QA-04 materialized WorldState can traverse the ordinary SIM-06 freeze/domain/candidate/invariant
/// path, prepare a deterministic State(S+1), cross the real SIM-03 SQLite transition COMMIT
/// authority boundary, and only then publish that exact State(S+1).
/// </summary>
public static class Qa04AuthoritativeStepBridgeV1
{
    private static readonly StableToken StructuralInvariant = new("qa04.structural-authority-path");

    public static async Task<Qa04AuthoritativeStepProbeV1> RunReducedAsync(
        int workerCount,
        ulong residentRecordCount,
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        if (!Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.Contains(workerCount))
            throw new InvalidDataException("qa04.structural.worker-count-not-canonical");
        if (residentRecordCount is 0 or > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount)
            throw new ArgumentOutOfRangeException(nameof(residentRecordCount));
        if (string.IsNullOrWhiteSpace(persistenceRoot))
            throw new ArgumentException("Persistence root is required.", nameof(persistenceRoot));

        var materialized = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(residentRecordCount);
        var state = materialized.WorldState;
        if (state.Header.Step != 0)
            throw new InvalidDataException("qa04.structural.genesis-step-mismatch");

        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: state.Header.Step,
            freezeStep: null,
            scheduled: Array.Empty<ScheduledOperationRefV1>());
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        var plan = StandardDomainExecutionPlanV1.Create();
        var runtimes = CreateStructuralRuntimes(materialized.Partition);
        var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(
            plan,
            state,
            frozen,
            runtimes,
            workerCount,
            cancellationToken).ConfigureAwait(false);

        var candidateId = DeriveCandidateId(state.Header.Step);
        var candidate = StepCandidateV1.Build(
            candidateId,
            state,
            frozen,
            outputs,
            Array.Empty<ConflictGroupResolutionV1>(),
            invariantResults:
            [
                new InvariantResultV1(
                    StructuralInvariant,
                    InvariantSeverityV1.CommitBlocking,
                    InvariantOutcomeV1.Pass),
            ]);
        if (!candidate.CommitDecision.CanCommit || candidate.IsPublishable)
            throw new InvalidDataException("qa04.structural.candidate-authority-boundary-invalid");
        if (candidate.PartitionCandidates.Count != 1 ||
            candidate.PartitionCandidates[0].PartitionId.Value != "resident.identity_lifecycle")
            throw new InvalidDataException("qa04.structural.resident-partition-candidate-missing");

        var basisResident = state.Partitions.Get("resident.identity_lifecycle").Header;
        var resultingResident = CreateResultingResidentHeader(
            materialized.Partition,
            basisResident,
            candidate.TargetStep);
        var prepared = StepStateApplicationV1.Prepare(
            state,
            candidate,
            [new StepPartitionStateMaterialV1(resultingResident)]);
        if (prepared.IsPublishable)
            throw new InvalidDataException("qa04.structural.prepared-state-premature-authority");
        if (prepared.ResultingState.Header.Step != candidate.TargetStep ||
            prepared.ResultingState.Header.PreviousStateDigest is null ||
            !CryptographicOperations.FixedTimeEquals(
                prepared.ResultingState.Header.PreviousStateDigest,
                state.Diagnostic.StateDigest))
            throw new InvalidDataException("qa04.structural.resulting-state-chain-invalid");

        var paths = PersistenceLayout.Resolve(persistenceRoot, state.Header.WorldId, 1);
        PersistenceLayout.EnsureGenerationDirectories(paths);
        await PersistenceLayout.WriteCurrentAsync(paths, 1, cancellationToken).ConfigureAwait(false);

        var genesis = CreateGenesisHistory(state);
        var initialContinuity = HistoryIntegrity.ComputeGenesisContinuityToken(
            state.Header.WorldId,
            genesis.RecordDigest);

        await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false);
        await store.InitializeWorldMetadataAsync(
            new WorldPersistenceMetadataSeed(
                state.Header.WorldId,
                PersistenceGeneration: 1,
                Qa04ReferenceLoadV1.WorldSeed,
                initialContinuity,
                ConfigGeneration: state.Header.ConfigGeneration,
                state.Diagnostic.ConfigDigest,
                MasterGeneration: state.Header.MasterGeneration),
            genesis,
            cancellationToken).ConfigureAwait(false);

        var transition = CreateTransitionHistory(candidate, prepared, genesis.RecordDigest);
        var resultingContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            state.Header.WorldId,
            candidate.TargetStep,
            initialContinuity,
            transition.RecordDigest);
        var finalizeMaterial = new StepFinalizeMaterialV1(
            candidate.ConfigGeneration,
            candidate.ConfigDigest,
            resultingContinuity,
            transition,
            Array.Empty<TerminalOperationCommit>());

        var receipt = await new StepFinalizationCoordinatorV1(new SqliteStepTransitionDurabilityV1(store))
            .FinalizeAsync(candidate, scheduler, finalizeMaterial, cancellationToken)
            .ConfigureAwait(false);
        var authoritative = StepStateApplicationV1.Publish(prepared, receipt);
        var recovery = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);

        if (recovery.FinalizedStep != authoritative.State.Header.Step ||
            !CryptographicOperations.FixedTimeEquals(recovery.ContinuityToken, resultingContinuity))
            throw new InvalidDataException("qa04.structural.sqlite-recovery-head-mismatch");
        if (!authoritative.IsPublishable || !receipt.IsPublishable)
            throw new InvalidDataException("qa04.structural.post-commit-authority-missing");

        var publishedResident = authoritative.State.Partitions.Get("resident.identity_lifecycle").Header;
        return new Qa04AuthoritativeStepProbeV1(
            candidate.BasisStep,
            authoritative.State.Header.Step,
            workerCount,
            residentRecordCount,
            outputs.Count,
            candidate.PartitionCandidates.Count,
            candidate.IsPublishable,
            prepared.IsPublishable,
            receipt.IsPublishable,
            authoritative.IsPublishable,
            scheduler.FreezeStep is null && scheduler.NextSchedulableStep == receipt.ResultingStep,
            recovery.FinalizedStep == receipt.ResultingStep,
            publishedResident.Revision,
            publishedResident.BasisStep,
            Convert.ToHexString(candidate.DiagnosticDigest).ToLowerInvariant(),
            Convert.ToHexString(state.Diagnostic.StateDigest).ToLowerInvariant(),
            Convert.ToHexString(authoritative.State.Header.PreviousStateDigest!).ToLowerInvariant(),
            Convert.ToHexString(authoritative.State.Diagnostic.StateDigest).ToLowerInvariant(),
            Convert.ToHexString(resultingContinuity).ToLowerInvariant(),
            [
                "qa04.target.reference-world-other-partitions-not-materialized",
                "qa04.target.core-substate-mutation-application-not-assembled",
                "qa04.target.authoritative-full-step-loop-not-assembled",
            ]);
    }

    private static IReadOnlyCollection<IDomainRuntimeV1> CreateStructuralRuntimes(
        DomainPartitionStateV1<Qa04ResidentIdentityLifecyclePayloadV1> residentPartition)
    {
        static ValueTask<IReadOnlyList<MutationIntentCandidateV1>> NoIntents(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>(
                Array.Empty<MutationIntentCandidateV1>());
        }

        ValueTask<IReadOnlyList<PartitionCandidateV1>> ResidentPartition(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = context.State.Partitions.Get("resident.identity_lifecycle").Header;
            var resulting = CreateResultingResidentHeader(
                residentPartition,
                current,
                checked(context.FrozenInput.BasisStep + 1));
            var changeSetDigest = StepPartitionStateMaterialV1.ComputeChangeSetDigest(
                current,
                resulting,
                context.FrozenInput.BasisStep,
                checked(context.FrozenInput.BasisStep + 1));
            return ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>(
            [
                ResidentParticipationPartitionCandidateFactoryV1.CreateResident(
                    context.State,
                    "resident.identity_lifecycle",
                    changeSetDigest),
            ]);
        }

        return
        [
            new SpatialDomainRuntimeV1(NoIntents),
            new EnvironmentDomainRuntimeV1(NoIntents),
            new PhysicalBuiltDomainRuntimeV1(NoIntents),
            new ParticipationDomainRuntimeV1(NoIntents),
            new ResidentDomainRuntimeV1(NoIntents, ResidentPartition),
            new SocietyEconomyDomainRuntimeV1(NoIntents),
            new GovernanceSecurityDomainRuntimeV1(NoIntents),
            new InfrastructureInformationDomainRuntimeV1(NoIntents),
        ];
    }

    private static PartitionStateHeaderV1 CreateResultingResidentHeader(
        DomainPartitionStateV1<Qa04ResidentIdentityLifecyclePayloadV1> partition,
        PartitionStateHeaderV1 basisHeader,
        ulong targetStep)
        => PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: checked(basisHeader.Revision + 1),
            basisStep: targetStep,
            detailLevel: basisHeader.DetailLevel,
            static payload => payload.CanonicalDigest());

    private static HistoryRecordMaterial CreateGenesisHistory(WorldStateV1 state)
        => HistoryRecordMaterial.Create(
            state.Header.WorldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "world.genesis.v1",
            payloadSchemaId: "core.world-genesis.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: state.Header.WorldId.ToBytes().Concat(Qa04ReferenceLoadV1.WorldSeed.ToBytes()).ToArray(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(5);
                writer.WriteUnsigned(0); writer.WriteBytes(state.Header.WorldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(Qa04ReferenceLoadV1.WorldSeed.ToBytes());
                writer.WriteUnsigned(2); writer.WriteUnsigned(state.Header.Step);
                writer.WriteUnsigned(3); writer.WriteBytes(state.Diagnostic.StateDigest);
                writer.WriteUnsigned(4); writer.WriteBytes(state.Diagnostic.ConfigDigest);
            });

    private static HistoryRecordMaterial CreateTransitionHistory(
        StepCandidateV1 candidate,
        PreparedStepWorldStateV1 prepared,
        ReadOnlySpan<byte> previousRecordDigest)
        => HistoryRecordMaterial.Create(
            candidate.WorldId,
            sequence: 2,
            previousRecordDigest: previousRecordDigest,
            recordType: "transition.committed.v1",
            payloadSchemaId: "persistence.transition-committed",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: candidate.DiagnosticDigest.Concat(prepared.ResultingState.Diagnostic.StateDigest).ToArray(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(7);
                writer.WriteUnsigned(0); writer.WriteUnsigned(candidate.BasisStep);
                writer.WriteUnsigned(1); writer.WriteUnsigned(candidate.TargetStep);
                writer.WriteUnsigned(2); writer.WriteBytes(candidate.CandidateId.ToBytes());
                writer.WriteUnsigned(3); writer.WriteBytes(candidate.DiagnosticDigest);
                writer.WriteUnsigned(4); writer.WriteBytes(prepared.BasisStateDigest);
                writer.WriteUnsigned(5); writer.WriteBytes(prepared.ResultingState.Diagnostic.StateDigest);
                writer.WriteUnsigned(6);
                writer.WriteArrayStart((ulong)candidate.PartitionCandidates.Count);
                foreach (var partition in candidate.PartitionCandidates)
                {
                    writer.WriteArrayStart(2);
                    writer.WriteAsciiText(partition.PartitionId.Value);
                    writer.WriteBytes(partition.CandidateDigest);
                }
            });

    private static OpaqueId128 DeriveCandidateId(ulong basisStep)
    {
        var digest = HashSuite.DomainHash("mv.qa04-structural-step-candidate.v1", writer =>
        {
            writer.WriteMapStart(3);
            writer.WriteUnsigned(0); writer.WriteBytes(Qa04ReferenceLoadV1.WorldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(basisStep);
            writer.WriteUnsigned(2); writer.WriteAsciiText(Qa04ReferenceLoadV1.BenchmarkProfileId);
        });
        var candidateId = HashSuite.Trunc128(digest);
        if (candidateId.IsZero)
            throw new InvalidDataException("qa04.structural.candidate-id-zero");
        return candidateId;
    }
}
