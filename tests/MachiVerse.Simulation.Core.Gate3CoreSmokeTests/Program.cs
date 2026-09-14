using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

const ulong basisStep = Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep;
var config = Qa04ReferenceConfigAuthorityV1.CreateCanonical();
var detailDirectory = new DetailDirectoryV1(Array.Empty<DetailRegionStateV1>());
var registry = StandardDomainRegistryAuthorityV1.Generation1;
var transactionState = CreateActiveTransaction();
var sourceTransactions = new[] { transactionState };
var state = CreateState(sourceTransactions, config, detailDirectory);

var root = Path.Combine(Path.GetTempPath(), "machiverse-gate3-core-fast-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = PersistenceLayout.Resolve(root, Qa04ReferenceLoadV1.WorldId, 1);
    PersistenceLayout.EnsureGenerationDirectories(paths);
    await PersistenceLayout.WriteCurrentAsync(paths, 1);
    await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths);
    var initialContinuity = await InitializeGenesisAsync(store, state);

    _ = await Qa04ProductionBasisPersistenceV1.PersistAsync(
        store,
        state,
        initialContinuity,
        sourceTransactions);

    var recoveryCut = await store.ReadSnapshotRecoveryCutAsync();
    Require(recoveryCut.FinalizedStep == basisStep, "Fast Gate3 recovery cut finalized Step drifted.");
    Require(recoveryCut.DurableOperations.Count == 0, "Fast Gate3 fixture unexpectedly contains Operations.");
    Require(recoveryCut.ScheduledOperations.Count == 0, "Fast Gate3 fixture unexpectedly contains scheduled Operations.");
    Require(recoveryCut.CrossDomainTransactions.Count == 1, "Fast Gate3 recovery cut lost transaction authority.");

    var recoveredTransactions = CoreOperationStateSnapshotCutV2.DecodeTransactions(
        recoveryCut.CrossDomainTransactions,
        recoveryCut.FinalizedStep);
    Require(recoveredTransactions.Count == 1 && recoveredTransactions[0].TransactionId == transactionState.TransactionId,
        "Fast Gate3 recovery transaction decode drifted.");

    var supplemental = CreateSupplemental(state, config, detailDirectory, registry);

    var mismatchObserved = false;
    try
    {
        var mismatchedState = CreateState(Array.Empty<CrossDomainTransactionStateV1>(), config, detailDirectory);
        _ = CoreSnapshotOwnerMaterialCutV1.CreateV2(
            mismatchedState,
            recoveryCut.DurableOperations,
            recoveryCut.ScheduledOperations,
            recoveredTransactions,
            CreateSupplemental(mismatchedState, config, detailDirectory, registry));
    }
    catch (InvalidDataException ex) when (ex.Message == "snapshot-running.operation-v2-owner-material-mismatch")
    {
        mismatchObserved = true;
    }
    Require(mismatchObserved,
        "Fast Gate3 contract must reject a WorldState/recovery-cut Operation V2 authority mismatch.");

    var ownerCut = CoreSnapshotOwnerMaterialCutV1.CreateV2(
        state,
        recoveryCut.DurableOperations,
        recoveryCut.ScheduledOperations,
        recoveredTransactions,
        supplemental);

    var authorityBeforeMutation = ownerCut.RecomputeOperationAuthorityV2().CanonicalDigest.ToArray();
    recoveredTransactions[0].Participants[0].CandidateEffectDigest[0] ^= 0xff;
    recoveredTransactions[0].RootCausality.Id[0] ^= 0xff;
    var authorityAfterMutation = ownerCut.RecomputeOperationAuthorityV2().CanonicalDigest;
    Require(CryptographicOperations.FixedTimeEquals(authorityBeforeMutation, authorityAfterMutation),
        "Fast Gate3 owner cut observed source transaction mutation after freeze.");

    var sections = CoreSnapshotProductionSectionProviderV1.CreateAllSixV2(ownerCut);
    CoreSnapshotProductionSectionProviderV1.VerifyAllSixV2(
        sections,
        basisStep,
        state.Header.ConfigGeneration);
    Require(sections.Count == 6,
        "Fast Gate3 contract must emit exactly the six canonical Core Snapshot sections.");

    Console.WriteLine(
        $"gate3-core-fast-pass step={basisStep} sections={sections.Count} transactions=1 operations=0 standardPartitions={state.Partitions.Count} reduced=true releaseEvidence=false");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static WorldStateV1 CreateState(
    IReadOnlyCollection<CrossDomainTransactionStateV1> transactions,
    MachiVerse.Simulation.Core.Configuration.EffectiveCoreConfig config,
    DetailDirectoryV1 detailDirectory)
{
    var partitions = StandardDomainPartitionRegistry.Entries
        .Select(identity => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                identity,
                revision: 1,
                basisStep: Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(Encoding.ASCII.GetBytes(
                    $"gate3.fast.partition:{identity.PartitionId.Value}")))))
        .ToArray();
    var scheduler = new OperationSchedulerStateV1(
        Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep,
        null,
        Array.Empty<ScheduledOperationRefV1>());
    return new WorldStateV1(
        new WorldStateHeaderV1(
            Qa04ReferenceLoadV1.WorldId,
            Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep,
            SHA256.HashData(Qa04ReferenceLoadV1.WorldSeed.ToBytes()),
            config.Generation,
            masterGeneration: 1,
            rateGeneration: 1),
        new OrderedPartitionDirectoryV1(partitions),
        OperationSchedulerSubstateV1.Canonicalize(
            scheduler,
            Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep),
        CoreOperationStateSubstateV2.Canonicalize(
            Array.Empty<DurableOperationStateV1>(),
            transactions,
            Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep),
        DetailDirectorySubstateV1.Canonicalize(detailDirectory),
        StandardDomainRegistryAuthorityV1.Generation1SubstateRef(),
        config.Digest);
}

static IFrozenCoreSnapshotOwnerMaterialV1[] CreateSupplemental(
    WorldStateV1 state,
    MachiVerse.Simulation.Core.Configuration.EffectiveCoreConfig config,
    DetailDirectoryV1 detailDirectory,
    DomainRegistryStateV1 registry)
    =>
    [
        FrozenCoreConfigSnapshotOwnerV1.Freeze(state.Header.Step, config),
        FrozenDetailDirectorySnapshotOwnerV1.Freeze(state.Header.Step, detailDirectory),
        FrozenDomainRegistrySnapshotOwnerV1.Freeze(state.Header.Step, registry),
    ];

static CrossDomainTransactionStateV1 CreateActiveTransaction()
{
    const ulong transactionBasisStep = 0;
    var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
    var residentEntry = StandardDomainExecutionPlanV1.Create().Entries
        .Single(entry => entry.DomainToken.Value == "resident");
    var root = new CausalityRefV1(
        CausalityRefKindV1.Operation,
        OpaqueId128.Parse("0000000000000000000000000000f301").ToBytes(),
        transactionBasisStep);
    var participant = new TransactionParticipantCandidateV1(
        residentEntry.DomainToken,
        residentEntry.OwnedPartitions[0],
        [OpaqueId128.Parse("0000000000000000000000000000f302")],
        required: true,
        TransactionParticipantOutcomeV1.Ready,
        SHA256.HashData("gate3-fast-transaction"u8));
    var invariant = new InvariantResultV1(
        CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind).Single(),
        InvariantSeverityV1.CommitBlocking,
        InvariantOutcomeV1.Pass);
    var candidate = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
        Qa04ReferenceLoadV1.WorldId,
        kind,
        transactionBasisStep,
        root,
        [OpaqueId128.Parse("0000000000000000000000000000f303")],
        stableLocalOrdinal: 0,
        [participant],
        [invariant]);
    if (!candidate.CanFinalize)
        throw new InvalidOperationException("Fast Gate3 transaction fixture did not validate.");
    return CrossDomainTransactionStateV1.FromValidCandidate(candidate, transactionBasisStep + 1);
}

static async Task<byte[]> InitializeGenesisAsync(SqlitePersistenceStore store, WorldStateV1 state)
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
    var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(
        Qa04ReferenceLoadV1.WorldId,
        genesis.RecordDigest);
    await store.InitializeWorldMetadataAsync(
        new WorldPersistenceMetadataSeed(
            Qa04ReferenceLoadV1.WorldId,
            PersistenceGeneration: 1,
            Qa04ReferenceLoadV1.WorldSeed,
            continuity,
            state.Header.ConfigGeneration,
            state.Diagnostic.ConfigDigest,
            state.Header.MasterGeneration),
        genesis);
    return continuity;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
