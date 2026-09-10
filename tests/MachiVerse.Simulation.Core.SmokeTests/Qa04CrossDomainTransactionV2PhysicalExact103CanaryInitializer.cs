using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04CrossDomainTransactionV2PhysicalExact103CanaryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
        => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "machiverse-cross-domain-v2-physical-103-" + Guid.NewGuid().ToString("N"));
        try
        {
            var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
            var config = new CoreConfigCoordinator().LoadStartup(
                """
                [meta]
                format = "machiverse-config"
                schema_version = "1.0"
                component = "simulation-core"
                """);
            Require(config.Get<string>("persistence.snapshot-compression") == "zstd",
                "Physical exact-103 canary requires the canonical zstd Snapshot policy.");

            var registry = StandardDomainRegistryAuthorityV1.Generation1;
            var detail = new DetailDirectoryV1(
                Array.Empty<DetailRegionStateV1>(),
                Array.Empty<DetailTransitionCandidateV1>());
            var frozen = BuildFrozenState(resident, config, registry, detail);
            var authorities = Qa04ReducedWorldTypedAuthorityV1.BindAll97(resident);
            var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
            var coreCut = CoreSnapshotOwnerMaterialCutV1.Create(
                frozen,
                Array.Empty<DurableOperationStateV1>(),
                Array.Empty<ScheduledOperationRefV1>(),
                new IFrozenCoreSnapshotOwnerMaterialV1[]
                {
                    FrozenDetailDirectorySnapshotOwnerV1.Freeze(frozen.Header.Step, detail),
                    FrozenDomainRegistrySnapshotOwnerV1.Freeze(frozen.Header.Step, registry),
                    FrozenCoreConfigSnapshotOwnerV1.Freeze(frozen.Header.Step, config),
                });

            var transaction = CreateTransaction(frozen.Header.Step);
            var durableTransaction = new DurableCrossDomainTransactionStateV1(
                transaction.TransactionId,
                transaction.Lifecycle,
                transaction.CreatedStep,
                transaction.UpdatedStep,
                transaction.TerminalStep,
                CrossDomainTransactionPersistentWireV1.Encode(transaction),
                transaction.CanonicalDigest());

            var historyAnchor = new HistoryAnchor(
                1,
                SHA256.HashData("cross-domain-v2-physical-103-history"u8));
            var continuity = SHA256.HashData("cross-domain-v2-physical-103-continuity"u8);
            var snapshotId = RunningSnapshotCoordinatorV1.DeriveSnapshotId(
                frozen.Header.WorldId,
                frozen.Header.Step,
                historyAnchor.Sequence,
                historyAnchor.Digest,
                continuity);
            var cut = new RunningSnapshotCutV1(
                frozen,
                snapshotId,
                historyAnchor,
                continuity,
                Array.Empty<DurableOperationStateV1>(),
                Array.Empty<ScheduledOperationRefV1>())
            {
                CrossDomainTransactions = [durableTransaction],
                CoreOwnerMaterial = coreCut,
            };

            var sections = StandardSnapshotOwnerCompositionV1.CreateAll103V2(
                cut,
                authorities,
                providers);
            Require(sections.Count == 103,
                "Physical v2 canary must compose exactly 103 sections from RunningSnapshot custody.");
            var expectedOperation = sections.Single(
                section => section.SectionId == CoreSnapshotOwnerSectionRegistryV1.OperationState);
            Require(expectedOperation.SectionSchema == CoreOperationStateSnapshotAuthorityV2.Schema &&
                    expectedOperation.LogicalItemCount == 1,
                "Physical v2 canary operation-state must be /2.0 and contain the durable transaction.");

            var paths = PersistenceLayout.Resolve(root, frozen.Header.WorldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);
            var physical = SnapshotPhysicalStaging.Prepare(paths, cut.SnapshotId);
            var zstd = new ZstdSnapshotChunkCompressionCodecV1();
            var staged = await CanonicalSnapshotProductionManifestDrainV1.StageRunningCutAsync(
                cut,
                physical,
                sections,
                config,
                Qa04ReferenceLoadV1.WorldSeed,
                zstdCodec: zstd);

            Require(File.Exists(physical.StagingManifestPath),
                "Physical v2 canary must durably write manifest.pb.");
            Require(staged.Manifest.Logical.Sections.Count == 103,
                "Physical v2 manifest must retain exactly 103 logical sections.");
            Require(staged.Chunks.Count > 0 &&
                    staged.Chunks.All(chunk => chunk.Compression == SnapshotCompression.Zstd),
                "Physical v2 canary must stage every canonical chunk using zstd.");

            var semanticVerifiers = StandardSnapshotOwnerCompositionV1.CreateSemanticVerifierRegistryV2(
                cut,
                authorities,
                providers);
            await CanonicalSnapshotStagingValidatorV1.ValidateAsync(
                physical,
                sections,
                semanticVerifiers,
                frozen,
                CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(zstd),
                expectedCut: cut,
                expectedSnapshotDigest: staged.SnapshotDigest,
                expectedPhysicalManifestDigest: staged.PhysicalManifestDigest);

            var physicalOperation = await ReadOperationSectionFromStagingAsync(
                physical,
                staged.Chunks,
                expectedOperation,
                zstd);
            var recovered = CoreOperationStateSnapshotSectionProviderV2.Recover(
                physicalOperation,
                cut.SnapshotStep);
            Require(recovered.Operations.Count == 0 && recovered.Transactions.Count == 1,
                "Physical exact-103 recovery must reconstruct the durable cross-domain transaction.");
            Require(recovered.Transactions[0].TransactionId == transaction.TransactionId &&
                    recovered.Transactions[0].CanonicalDigest().SequenceEqual(transaction.CanonicalDigest()),
                "Physical exact-103 recovery must preserve transaction identity and semantic digest.");
            Require(recovered.LogicalContentDigest.SequenceEqual(expectedOperation.LogicalContentDigest),
                "Physical exact-103 semantic rehash must equal the pre-staging operation-state digest.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<CanonicalSnapshotSectionMaterialV1> ReadOperationSectionFromStagingAsync(
        SnapshotPhysicalPaths physical,
        IReadOnlyList<PhysicalSnapshotChunkDescriptor> chunks,
        CanonicalSnapshotSectionMaterialV1 expectedOperation,
        ISnapshotChunkCompressionCodecV1 zstd)
    {
        var fragments = new List<SnapshotSectionFragmentMaterialV1>();
        foreach (var descriptor in chunks.OrderBy(static chunk => chunk.ChunkIndex))
        {
            var path = Path.Combine(
                physical.StagingDirectory,
                descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var decoded = await CanonicalSnapshotChunkFileV1.ReadValidatedAsync(
                path,
                CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(zstd));
            fragments.AddRange(decoded.Payload.Fragments.Where(
                fragment => string.Equals(
                    fragment.SectionId,
                    CoreSnapshotOwnerSectionRegistryV1.OperationState,
                    StringComparison.Ordinal)));
        }
        Require(fragments.Count > 0,
            "Physical exact-103 recovery must find operation-state fragments in staged chunks.");
        return expectedOperation with
        {
            Fragments = Array.AsReadOnly(
                fragments.OrderBy(static fragment => fragment.FragmentIndex).ToArray()),
        };
    }

    private static CrossDomainTransactionStateV1 CreateTransaction(ulong snapshotStep)
    {
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var resident = StandardDomainExecutionPlanV1.Create().Entries
            .Single(entry => entry.DomainToken.Value == "resident");
        var participant = new PersistentTransactionParticipantV1(
            resident.DomainToken,
            resident.OwnedPartitions[0],
            [OpaqueId128.Parse("0000000000000000000000000006c101")],
            required: true,
            TransactionParticipantOutcomeV1.Ready,
            Enumerable.Repeat((byte)0x4a, 32).ToArray());
        var invariant = new InvariantResultV1(
            CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind).Single(),
            InvariantSeverityV1.CommitBlocking,
            InvariantOutcomeV1.Pass,
            [new CausalityRefV1(
                CausalityRefKindV1.Entity,
                OpaqueId128.Parse("0000000000000000000000000006c102").ToBytes(),
                snapshotStep)]);
        return new CrossDomainTransactionStateV1(
            OpaqueId128.Parse("0000000000000000000000000006c001"),
            kind,
            TransactionLifecycleV1.Active,
            snapshotStep,
            snapshotStep,
            null,
            new CausalityRefV1(
                CausalityRefKindV1.Operation,
                OpaqueId128.Parse("0000000000000000000000000006c200").ToBytes(),
                snapshotStep),
            [OpaqueId128.Parse("0000000000000000000000000006c010")],
            [participant],
            [invariant]);
    }

    private static WorldStateV1 BuildFrozenState(
        Qa04ResidentIdentityMaterializationV1 resident,
        EffectiveCoreConfig config,
        DomainRegistryStateV1 registry,
        DetailDirectoryV1 detail)
    {
        var scheduler = OperationSchedulerSubstateV1.Canonicalize(
            new OperationSchedulerStateV1(
                nextSchedulableStep: resident.WorldState.Header.Step,
                freezeStep: null,
                scheduled: Array.Empty<ScheduledOperationRefV1>()),
            resident.WorldState.Header.Step);
        var operations = DurableOperationSubstateV1.Canonicalize(Array.Empty<DurableOperationStateV1>());
        var detailAuthority = DetailDirectorySubstateV1.Canonicalize(detail);
        return new WorldStateV1(
            new WorldStateHeaderV1(
                resident.WorldState.Header.WorldId,
                resident.WorldState.Header.Step,
                SHA256.HashData(Qa04ReferenceLoadV1.WorldSeed.ToBytes()),
                config.Generation,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(
                resident.WorldState.Partitions.CanonicalEntries.Select(entry => new PartitionStateRefV1(entry.Header))),
            scheduler,
            operations,
            detailAuthority,
            registry.ToWorldSubstateRef(),
            config.Digest);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
