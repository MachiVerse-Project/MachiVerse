using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class ProductionSnapshot103CanarySmoke
{
    internal static void VerifyComposition()
    {
        var qa = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
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
        var frozenState = BuildFrozenState(qa, config, registry, detail);

        var resident = CreateResidentState(qa).BindSnapshotMaterial(frozenState);
        var participation = ParticipationDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var physicalBuilt = PhysicalBuiltDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var spatial = SpatialDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var environment = EnvironmentDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var societyEconomy = SocietyEconomyDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var infrastructureInformation = InfrastructureInformationDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var governanceSecurity = GovernanceSecurityDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);

        var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
        var authorities = StandardDomainSnapshotOwnerCompositionV1.CreateAuthoritySet(
            frozenState,
            resident,
            participation,
            physicalBuilt,
            spatial,
            environment,
            societyEconomy,
            infrastructureInformation,
            governanceSecurity);
        Require(authorities.CanonicalAuthorities.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "103-section composition must bind exactly 97 typed Domain authorities.");
        Require(authorities.CanonicalAuthorities.Sum(static value => checked((long)value.ActualItemCount)) == 1,
            "103-section composition must contain exactly one actual Domain record.");

        var coreOwnerCut = CoreSnapshotOwnerMaterialCutV1.Create(
            frozenState,
            Array.Empty<DurableOperationStateV1>(),
            Array.Empty<ScheduledOperationRefV1>(),
            new IFrozenCoreSnapshotOwnerMaterialV1[]
            {
                FrozenDetailDirectorySnapshotOwnerV1.Freeze(frozenState.Header.Step, detail),
                FrozenDomainRegistrySnapshotOwnerV1.Freeze(frozenState.Header.Step, registry),
                FrozenCoreConfigSnapshotOwnerV1.Freeze(frozenState.Header.Step, config),
            });
        var sections = StandardSnapshotOwnerCompositionV1.CreateAll103(
            coreOwnerCut,
            authorities,
            providers);
        Require(sections.Count == SnapshotManifestValidation.StandardRequiredSectionCount && sections.Count == 103,
            "Production composition must assemble exactly 103 canonical sections.");
        Require(sections.Count(static value => StandardSnapshotSectionSetV1.IsCoreSection(value.SectionId)) == 6,
            "Production composition must contain exactly six Core sections.");
        Require(sections.Count(static value => StandardDomainPartitionRegistry.TryGet(value.SectionId, out _)) == 97,
            "Production composition must contain exactly 97 Domain sections.");
        var residentSection = sections.Single(static value => value.SectionId == ResidentIdentityLifecyclePayloadV1.PartitionId);
        Require(residentSection.LogicalItemCount == 1 &&
                CryptographicOperations.FixedTimeEquals(residentSection.LogicalContentDigest, qa.PartitionHeader.CanonicalDigest),
            "Production composition must retain the actual Resident authority digest.");
    }

    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-production-103-canary-" + Guid.NewGuid().ToString("N"));
        try
        {
            var qa = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
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
            var frozenState = BuildFrozenState(qa, config, registry, detail);

            var resident = CreateResidentState(qa).BindSnapshotMaterial(frozenState);
            var participation = ParticipationDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
            var physicalBuilt = PhysicalBuiltDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
            var spatial = SpatialDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
            var environment = EnvironmentDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
            var societyEconomy = SocietyEconomyDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
            var infrastructureInformation = InfrastructureInformationDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
            var governanceSecurity = GovernanceSecurityDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);

            var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
            var authorities = StandardDomainSnapshotOwnerCompositionV1.CreateAuthoritySet(
                frozenState,
                resident,
                participation,
                physicalBuilt,
                spatial,
                environment,
                societyEconomy,
                infrastructureInformation,
                governanceSecurity);
            Require(authorities.CanonicalAuthorities.Sum(static value => checked((long)value.ActualItemCount)) == 1,
                "103-section canary must contain exactly one actual Domain record.");

            var coreOwnerCut = CoreSnapshotOwnerMaterialCutV1.Create(
                frozenState,
                Array.Empty<DurableOperationStateV1>(),
                Array.Empty<ScheduledOperationRefV1>(),
                new IFrozenCoreSnapshotOwnerMaterialV1[]
                {
                    FrozenDetailDirectorySnapshotOwnerV1.Freeze(frozenState.Header.Step, detail),
                    FrozenDomainRegistrySnapshotOwnerV1.Freeze(frozenState.Header.Step, registry),
                    FrozenCoreConfigSnapshotOwnerV1.Freeze(frozenState.Header.Step, config),
                });
            var sections = StandardSnapshotOwnerCompositionV1.CreateAll103(
                coreOwnerCut,
                authorities,
                providers);
            Require(sections.Count == SnapshotManifestValidation.StandardRequiredSectionCount && sections.Count == 103,
                "Production canary must assemble exactly 103 canonical sections.");
            Require(sections.Count(static value => StandardSnapshotSectionSetV1.IsCoreSection(value.SectionId)) == 6,
                "Production canary must contain exactly six Core sections.");
            Require(sections.Count(static value => StandardDomainPartitionRegistry.TryGet(value.SectionId, out _)) == 97,
                "Production canary must contain exactly 97 Domain sections.");
            var residentSection = sections.Single(static value => value.SectionId == ResidentIdentityLifecyclePayloadV1.PartitionId);
            Require(residentSection.LogicalItemCount == 1 &&
                    CryptographicOperations.FixedTimeEquals(residentSection.LogicalContentDigest, qa.PartitionHeader.CanonicalDigest),
                "Production canary must retain the actual Resident authority digest.");

            var historyAnchor = new HistoryAnchor(1, SHA256.HashData("production-103-canary-history"u8));
            var continuity = SHA256.HashData("production-103-canary-continuity"u8);
            var snapshotId = RunningSnapshotCoordinatorV1.DeriveSnapshotId(
                frozenState.Header.WorldId,
                frozenState.Header.Step,
                historyAnchor.Sequence,
                historyAnchor.Digest,
                continuity);
            var cut = new RunningSnapshotCutV1(
                frozenState,
                snapshotId,
                historyAnchor,
                continuity,
                Array.Empty<DurableOperationStateV1>(),
                Array.Empty<ScheduledOperationRefV1>())
            {
                CoreOwnerMaterial = coreOwnerCut,
            };

            var paths = PersistenceLayout.Resolve(root, frozenState.Header.WorldId, 1);
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
                "Production 103-section canary must durably write manifest.pb.");
            Require(staged.Manifest.Logical.Sections.Count == 103,
                "Production 103-section manifest must contain exactly 103 logical sections.");
            Require(staged.Chunks.Count > 0,
                "Production 103-section canary must emit at least one physical chunk.");

            var semanticVerifiers = StandardSnapshotOwnerCompositionV1.CreateSemanticVerifierRegistry(
                coreOwnerCut,
                authorities,
                providers);
            await CanonicalSnapshotStagingValidatorV1.ValidateAsync(
                physical,
                sections,
                semanticVerifiers,
                frozenState,
                CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(zstd),
                expectedCut: cut,
                expectedSnapshotDigest: staged.SnapshotDigest,
                expectedPhysicalManifestDigest: staged.PhysicalManifestDigest);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static WorldStateV1 BuildFrozenState(
        Qa04ResidentIdentityMaterializationV1 qa,
        EffectiveCoreConfig config,
        DomainRegistryStateV1 registry,
        DetailDirectoryV1 detail)
    {
        var partitions = StandardDomainPartitionRegistry.Entries.Select(identity => new PartitionStateRefV1(
            qa.WorldState.Partitions.Get(identity.PartitionId.Value).Header));
        var scheduler = OperationSchedulerSubstateV1.Canonicalize(
            new OperationSchedulerStateV1(
                nextSchedulableStep: qa.WorldState.Header.Step,
                freezeStep: null,
                scheduled: Array.Empty<ScheduledOperationRefV1>()),
            qa.WorldState.Header.Step);
        var operations = DurableOperationSubstateV1.Canonicalize(Array.Empty<DurableOperationStateV1>());
        var detailAuthority = DetailDirectorySubstateV1.Canonicalize(detail);

        return new WorldStateV1(
            new WorldStateHeaderV1(
                qa.WorldState.Header.WorldId,
                qa.WorldState.Header.Step,
                SHA256.HashData(Qa04ReferenceLoadV1.WorldSeed.ToBytes()),
                config.Generation,
                masterGeneration: 1,
                rateGeneration: 1),
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
