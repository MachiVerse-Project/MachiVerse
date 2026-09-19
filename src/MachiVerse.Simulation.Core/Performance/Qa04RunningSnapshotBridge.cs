using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04RunningSnapshotProbeResultV1
{
    public string SchemaVersion { get; init; } = "1.0";
    public ulong StandardIntervalSteps { get; init; }
    public bool StandardTriggerAt18000 { get; init; }
    public ulong FrozenSnapshotStep { get; init; }
    public ulong LaterFinalizedStepBeforeDrain { get; init; }
    public ulong FinalizedStepAfterDrain { get; init; }
    public ulong FrozenHistoryAnchorSequence { get; init; }
    public ulong SnapshotCommitHistorySequence { get; init; }
    public int FrozenOperationCount { get; init; }
    public int FrozenScheduledOperationCount { get; init; }
    public bool SnapshotIdDeterministic { get; init; }
    public bool SingleInFlightEnforced { get; init; }
    public bool HistoricalCutCommittedAfterLaterStep { get; init; }
    public bool FrozenHistoryAnchorStillDurable { get; init; }
    public bool SnapshotCatalogPreservedFrozenCut { get; init; }
    public bool LaterAuthorityPreservedAfterDrain { get; init; }
    public bool SnapshotHistoryAppendedAtDrainHead { get; init; }
    public bool PhysicalSnapshotAtomicallyFinalized { get; init; }
    public bool ReducedRunningSnapshotAuthorityAvailable { get; init; }
    public bool ReferenceWorldMaterialized { get; init; }
    public bool ReleaseEvidenceCapable { get; init; }
    public string[] BlockingFailureCodes { get; init; } = [];
}

public static class Qa04RunningSnapshotBridgeV1
{
    public static async Task<Qa04RunningSnapshotProbeResultV1> RunReducedAsync(
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(persistenceRoot))
            throw new ArgumentException("persistenceRoot is required.", nameof(persistenceRoot));

        var standard = new RunningSnapshotCoordinatorV1();
        var standardTrigger = !standard.IsDue(0) &&
                              !standard.IsDue(17_999) &&
                              standard.IsDue(18_000) &&
                              !standard.IsDue(18_000, newestCommittedSnapshotStep: 18_000);
        if (!standardTrigger)
            throw new InvalidDataException("qa04.snapshot.standard-trigger-contract-mismatch");

        var residentSeed = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var config = new CoreConfigCoordinator().LoadStartup(
            """
            [meta]
            format = "machiverse-config"
            schema_version = "1.0"
            component = "simulation-core"
            """);
        var registry = StandardDomainRegistryAuthorityV1.Generation1;
        var detail = new DetailDirectoryV1(
            Array.Empty<DetailRegionStateV1>(),
            Array.Empty<DetailTransitionCandidateV1>());
        var worldId = Qa04ReferenceLoadV1.WorldId;
        var worldSeed = Qa04ReferenceLoadV1.WorldSeed;
        var state = CreateState(
            residentSeed,
            config,
            registry,
            detail,
            step: 0,
            previousStateDigest: null);
        var paths = PersistenceLayout.Resolve(persistenceRoot, worldId, 1);
        PersistenceLayout.EnsureGenerationDirectories(paths);
        await PersistenceLayout.WriteCurrentAsync(paths, 1, cancellationToken).ConfigureAwait(false);

        var genesis = CreateGenesisHistory(state, worldSeed);
        var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);
        await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false);
        await store.InitializeWorldMetadataAsync(
            new WorldPersistenceMetadataSeed(
                worldId,
                PersistenceGeneration: 1,
                worldSeed,
                continuity,
                ConfigGeneration: config.Generation,
                config.Digest,
                MasterGeneration: 1),
            genesis,
            cancellationToken).ConfigureAwait(false);

        for (ulong step = 0; step < 30; step++)
        {
            (state, continuity) = await CommitTransitionAsync(
                store,
                state,
                continuity,
                config,
                worldSeed,
                residentSeed,
                registry,
                detail,
                cancellationToken).ConfigureAwait(false);
        }

        var coordinator = new RunningSnapshotCoordinatorV1(intervalSteps: 30);
        var cut = await coordinator.TryFreezeWithCoreOwnerMaterialIfDueAsync(
                state,
                store,
                new IFrozenCoreSnapshotOwnerMaterialV1[]
                {
                    FrozenDetailDirectorySnapshotOwnerV1.Freeze(state.Header.Step, detail),
                    FrozenDomainRegistrySnapshotOwnerV1.Freeze(state.Header.Step, registry),
                    FrozenCoreConfigSnapshotOwnerV1.Freeze(state.Header.Step, config),
                },
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("qa04.snapshot.due-cut-not-frozen");
        var deterministicId = cut.SnapshotId == RunningSnapshotCoordinatorV1.DeriveSnapshotId(
            worldId,
            cut.SnapshotStep,
            cut.HistoryAnchor.Sequence,
            cut.HistoryAnchor.Digest,
            cut.StateContinuityToken);
        var singleInFlight = await coordinator.TryFreezeIfDueAsync(state, store, cancellationToken).ConfigureAwait(false) is null;

        (state, continuity) = await CommitTransitionAsync(
            store,
            state,
            continuity,
            config,
            worldSeed,
            residentSeed,
            registry,
            detail,
            cancellationToken).ConfigureAwait(false);
        var recoveryBeforeDrain = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);

        var resident = CreateResidentState(residentSeed).BindSnapshotMaterial(cut.FrozenState);
        var participation = ParticipationDomainStateV1.CreateEmpty().BindSnapshotMaterial(cut.FrozenState);
        var physicalBuilt = PhysicalBuiltDomainStateV1.CreateEmpty().BindSnapshotMaterial(cut.FrozenState);
        var spatial = SpatialDomainStateV1.CreateEmpty().BindSnapshotMaterial(cut.FrozenState);
        var environment = EnvironmentDomainStateV1.CreateEmpty().BindSnapshotMaterial(cut.FrozenState);
        var societyEconomy = SocietyEconomyDomainStateV1.CreateEmpty().BindSnapshotMaterial(cut.FrozenState);
        var infrastructureInformation = InfrastructureInformationDomainStateV1.CreateEmpty().BindSnapshotMaterial(cut.FrozenState);
        var governanceSecurity = GovernanceSecurityDomainStateV1.CreateEmpty().BindSnapshotMaterial(cut.FrozenState);
        var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
        var authorities = StandardDomainSnapshotOwnerCompositionV1.CreateAuthoritySet(
            cut.FrozenState,
            resident,
            participation,
            physicalBuilt,
            spatial,
            environment,
            societyEconomy,
            infrastructureInformation,
            governanceSecurity);
        if (authorities.CanonicalAuthorities.Sum(static authority => checked((long)authority.ActualItemCount)) != 1)
            throw new InvalidDataException("qa04.snapshot.reduced-domain-material-count-mismatch");

        var coreCut = cut.CoreOwnerMaterial
            ?? throw new InvalidDataException("qa04.snapshot.core-owner-material-missing");
        var sections = StandardSnapshotOwnerCompositionV1.CreateAll103(coreCut, authorities, providers);
        var semanticVerifiers = StandardSnapshotOwnerCompositionV1.CreateSemanticVerifierRegistry(
            coreCut,
            authorities,
            providers);
        var physical = SnapshotPhysicalStaging.Prepare(paths, cut.SnapshotId);
        var zstd = new ZstdSnapshotChunkCompressionCodecV1();
        var staged = await CanonicalSnapshotProductionManifestDrainV1.StageRunningCutAsync(
            cut,
            physical,
            sections,
            config,
            worldSeed,
            zstdCodec: zstd,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (staged.Manifest.Logical.Sections.Count != SnapshotManifestValidation.StandardRequiredSectionCount)
            throw new InvalidDataException("qa04.snapshot.standard-section-count-not-103");

        var committed = await coordinator.CommitCanonicalDrainedAsync(
            cut,
            store,
            paths,
            physical,
            staged.SnapshotDigest,
            staged.PhysicalManifestDigest,
            sections,
            semanticVerifiers,
            compressionDecoders: CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(zstd),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var frozenAnchorDurable = await store.HistoryAnchorExistsAsync(
            cut.HistoryAnchor.Sequence,
            cut.HistoryAnchor.Digest,
            cancellationToken).ConfigureAwait(false);
        var candidates = await store.ListSnapshotCandidatesNewestFirstAsync(cancellationToken).ConfigureAwait(false);
        var catalogPreserved = candidates.Count == 1 &&
                               candidates[0].SnapshotId == cut.SnapshotId &&
                               candidates[0].SnapshotStep == 30 &&
                               candidates[0].HistoryAnchorSequence == cut.HistoryAnchor.Sequence &&
                               CryptographicOperations.FixedTimeEquals(candidates[0].SnapshotDigest, staged.SnapshotDigest) &&
                               CryptographicOperations.FixedTimeEquals(candidates[0].PhysicalManifestDigest, staged.PhysicalManifestDigest);
        var recoveryAfterDrain = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        var laterAuthorityPreserved = recoveryAfterDrain.FinalizedStep == 31 &&
                                      CryptographicOperations.FixedTimeEquals(recoveryAfterDrain.ContinuityToken, continuity);
        var finalHistory = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var historyAppendedAtDrainHead = finalHistory.Sequence == committed.HistorySequence &&
                                         committed.HistorySequence > cut.HistoryAnchor.Sequence + 1;
        var physicalFinalized = Directory.Exists(physical.FinalDirectory) && !Directory.Exists(physical.StagingDirectory);
        var historicalCommit = cut.SnapshotStep == 30 &&
                               recoveryBeforeDrain.FinalizedStep == 31 &&
                               committed.SnapshotStep == 30;

        var reducedAvailable = standardTrigger && deterministicId && singleInFlight && historicalCommit &&
                               frozenAnchorDurable && catalogPreserved && laterAuthorityPreserved &&
                               historyAppendedAtDrainHead && physicalFinalized;
        if (!reducedAvailable)
            throw new InvalidDataException("qa04.snapshot.reduced-running-snapshot-proof-incomplete");

        return new Qa04RunningSnapshotProbeResultV1
        {
            StandardIntervalSteps = RunningSnapshotCoordinatorV1.StandardIntervalSteps,
            StandardTriggerAt18000 = standardTrigger,
            FrozenSnapshotStep = cut.SnapshotStep,
            LaterFinalizedStepBeforeDrain = recoveryBeforeDrain.FinalizedStep,
            FinalizedStepAfterDrain = recoveryAfterDrain.FinalizedStep,
            FrozenHistoryAnchorSequence = cut.HistoryAnchor.Sequence,
            SnapshotCommitHistorySequence = committed.HistorySequence,
            FrozenOperationCount = cut.DurableOperations.Count,
            FrozenScheduledOperationCount = cut.ScheduledOperations.Count,
            SnapshotIdDeterministic = deterministicId,
            SingleInFlightEnforced = singleInFlight,
            HistoricalCutCommittedAfterLaterStep = historicalCommit,
            FrozenHistoryAnchorStillDurable = frozenAnchorDurable,
            SnapshotCatalogPreservedFrozenCut = catalogPreserved,
            LaterAuthorityPreservedAfterDrain = laterAuthorityPreserved,
            SnapshotHistoryAppendedAtDrainHead = historyAppendedAtDrainHead,
            PhysicalSnapshotAtomicallyFinalized = physicalFinalized,
            ReducedRunningSnapshotAuthorityAvailable = reducedAvailable,
            ReferenceWorldMaterialized = false,
            ReleaseEvidenceCapable = false,
            BlockingFailureCodes =
            [
                "qa04.target.reference-world-not-materialized",
                "qa04.target.authoritative-step-loop-not-assembled",
            ],
        };
    }

    private static async Task<(WorldStateV1 State, byte[] Continuity)> CommitTransitionAsync(
        SqlitePersistenceStore store,
        WorldStateV1 state,
        byte[] previousContinuity,
        EffectiveCoreConfig config,
        WorldSeed256 worldSeed,
        Qa04ResidentIdentityMaterializationV1 residentSeed,
        DomainRegistryStateV1 registry,
        DetailDirectoryV1 detail,
        CancellationToken cancellationToken)
    {
        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var targetStep = checked(state.Header.Step + 1);
        var nextState = CreateState(
            residentSeed,
            config,
            registry,
            detail,
            targetStep,
            state.Diagnostic.StateDigest);
        var history = HistoryRecordMaterial.Create(
            state.Header.WorldId,
            checked(anchor.Sequence + 1),
            anchor.Digest,
            "transition.committed.v1",
            "persistence.transition-committed",
            1,
            0,
            nextState.Diagnostic.StateDigest,
            writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteUnsigned(state.Header.Step);
                writer.WriteUnsigned(1); writer.WriteUnsigned(targetStep);
                writer.WriteUnsigned(2); writer.WriteBytes(nextState.Diagnostic.StateDigest);
            });
        var continuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            state.Header.WorldId,
            targetStep,
            previousContinuity,
            history.RecordDigest);
        await store.PersistTransitionCommitAsync(
            state.Header.Step,
            targetStep,
            continuity,
            activeConfigGeneration: config.Generation,
            config.Digest,
            history,
            Array.Empty<TerminalOperationCommit>(),
            cancellationToken).ConfigureAwait(false);
        return (nextState, continuity);
    }

    private static WorldStateV1 CreateState(
        Qa04ResidentIdentityMaterializationV1 residentSeed,
        EffectiveCoreConfig config,
        DomainRegistryStateV1 registry,
        DetailDirectoryV1 detail,
        ulong step,
        byte[]? previousStateDigest)
    {
        var partitions = StandardDomainPartitionRegistry.Entries.Select(identity => new PartitionStateRefV1(
            residentSeed.WorldState.Partitions.Get(identity.PartitionId.Value).Header));
        var scheduler = OperationSchedulerSubstateV1.Canonicalize(
            new OperationSchedulerStateV1(
                nextSchedulableStep: step,
                freezeStep: null,
                scheduled: Array.Empty<ScheduledOperationRefV1>()),
            step);
        var operations = DurableOperationSubstateV1.Canonicalize(Array.Empty<DurableOperationStateV1>());
        var detailAuthority = DetailDirectorySubstateV1.Canonicalize(detail);
        return new WorldStateV1(
            new WorldStateHeaderV1(
                Qa04ReferenceLoadV1.WorldId,
                step,
                SHA256.HashData(Qa04ReferenceLoadV1.WorldSeed.ToBytes()),
                config.Generation,
                masterGeneration: 1,
                rateGeneration: 1,
                previousStateDigest),
            new OrderedPartitionDirectoryV1(partitions),
            scheduler,
            operations,
            detailAuthority,
            registry.ToWorldSubstateRef(),
            config.Digest);
    }

    private static ResidentDomainStateV1 CreateResidentState(Qa04ResidentIdentityMaterializationV1 qa)
        => new(
            qa.Partition,
            Empty<ResidentBodyHealthPayloadV1>(ResidentBodyHealthPayloadV1.PartitionId),
            Empty<ResidentPhysiologyPayloadV1>(ResidentPhysiologyPayloadV1.PartitionId),
            Empty<ResidentPerceptionPayloadV1>(ResidentPerceptionPayloadV1.PartitionId),
            Empty<ResidentKnowledgeBeliefPayloadV1>(ResidentKnowledgeBeliefPayloadV1.PartitionId),
            Empty<ResidentMemoryPayloadV1>(ResidentMemoryPayloadV1.PartitionId),
            Empty<ResidentPsychologyPayloadV1>(ResidentPsychologyPayloadV1.PartitionId),
            Empty<ResidentGoalPlanPayloadV1>(ResidentGoalPlanPayloadV1.PartitionId),
            Empty<ResidentSkillAptitudePayloadV1>(ResidentSkillAptitudePayloadV1.PartitionId),
            Empty<ResidentRelationshipPayloadV1>(ResidentRelationshipPayloadV1.PartitionId),
            Empty<ResidentFamilyLineagePayloadV1>(ResidentFamilyLineagePayloadV1.PartitionId),
            Empty<ResidentBehaviorStatePayloadV1>(ResidentBehaviorStatePayloadV1.PartitionId),
            Empty<ResidentLineagePayloadV1>(ResidentLineagePayloadV1.PartitionId));

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(
            StandardDomainPartitionRegistry.Get(partitionId),
            Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static HistoryRecordMaterial CreateGenesisHistory(WorldStateV1 state, WorldSeed256 worldSeed)
        => HistoryRecordMaterial.Create(
            state.Header.WorldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "world.genesis.v1",
            payloadSchemaId: "core.world-genesis.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: state.Header.WorldId.ToBytes().Concat(worldSeed.ToBytes()).ToArray(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteBytes(state.Header.WorldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(worldSeed.ToBytes());
                writer.WriteUnsigned(2); writer.WriteUnsigned(0);
                writer.WriteUnsigned(3); writer.WriteBytes(state.Diagnostic.StateDigest);
            });
}
