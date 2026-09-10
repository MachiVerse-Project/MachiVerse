using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04CrossDomainTransactionOperationStateV2Exact103ProductionCanaryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
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

        var sections = StandardSnapshotOwnerCompositionV1.CreateAll103V2(
            coreCut,
            [transaction],
            authorities,
            providers);
        Require(sections.Count == 103,
            "Cross-domain transaction v2 canary must compose exactly 103 Snapshot sections.");
        Require(sections.Count(section => StandardSnapshotSectionSetV1.IsCoreSection(section.SectionId)) == 6,
            "Cross-domain transaction v2 canary must retain exactly six Core sections.");
        Require(sections.Count(section => StandardDomainPartitionRegistry.TryGet(section.SectionId, out _)) == 97,
            "Cross-domain transaction v2 canary must retain exactly 97 Domain sections.");

        var operation = sections.Single(section => section.SectionId == CoreSnapshotOwnerSectionRegistryV1.OperationState);
        Require(operation.SectionSchema == CoreOperationStateSnapshotAuthorityV2.Schema &&
                operation.SectionSchema.Version == new SchemaVersionV1(2, 0),
            "Exact-103 operation-state section must use canonical /2.0 schema.");
        Require(operation.LogicalItemCount == 1,
            "Exact-103 operation-state v2 logical count must include the transaction item.");

        var recovered = CoreOperationStateSnapshotSectionProviderV2.Recover(operation, frozen.Header.Step);
        Require(recovered.Operations.Count == 0 && recovered.Transactions.Count == 1,
            "Exact-103 recovery must reconstruct one cross-domain transaction.");
        Require(recovered.Transactions[0].TransactionId == transaction.TransactionId &&
                recovered.Transactions[0].CanonicalDigest().SequenceEqual(transaction.CanonicalDigest()),
            "Exact-103 recovery must preserve transaction identity and semantic digest.");
        Require(recovered.LogicalContentDigest.SequenceEqual(operation.LogicalContentDigest),
            "Exact-103 recovered operation-state semantic digest must match section authority.");

        var semanticVerifiers = StandardSnapshotOwnerCompositionV1.CreateSemanticVerifierRegistryV2(
            coreCut,
            authorities,
            providers);
        semanticVerifiers.VerifyAll(sections);

        var tampered = operation with
        {
            LogicalContentDigest = Enumerable.Repeat((byte)0x6d, 32).ToArray(),
        };
        ExpectReject(
            () => CoreOperationStateSnapshotSectionProviderV2.Recover(tampered, frozen.Header.Step),
            "Exact-103 operation-state v2 recovery must reject semantic digest tamper.");
    }

    private static CrossDomainTransactionStateV1 CreateTransaction(ulong snapshotStep)
    {
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var resident = StandardDomainExecutionPlanV1.Create().Entries
            .Single(entry => entry.DomainToken.Value == "resident");
        var participant = new PersistentTransactionParticipantV1(
            resident.DomainToken,
            resident.OwnedPartitions[0],
            [OpaqueId128.Parse("0000000000000000000000000006b101")],
            required: true,
            TransactionParticipantOutcomeV1.Ready,
            Enumerable.Repeat((byte)0x3a, 32).ToArray());
        var invariant = new InvariantResultV1(
            CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind).Single(),
            InvariantSeverityV1.CommitBlocking,
            InvariantOutcomeV1.Pass,
            [new CausalityRefV1(
                CausalityRefKindV1.Entity,
                OpaqueId128.Parse("0000000000000000000000000006b102").ToBytes(),
                snapshotStep)]);
        return new CrossDomainTransactionStateV1(
            OpaqueId128.Parse("0000000000000000000000000006b001"),
            kind,
            TransactionLifecycleV1.Active,
            snapshotStep,
            snapshotStep,
            null,
            new CausalityRefV1(
                CausalityRefKindV1.Operation,
                OpaqueId128.Parse("0000000000000000000000000006b200").ToBytes(),
                snapshotStep),
            [OpaqueId128.Parse("0000000000000000000000000006b010")],
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

    private static void ExpectReject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
