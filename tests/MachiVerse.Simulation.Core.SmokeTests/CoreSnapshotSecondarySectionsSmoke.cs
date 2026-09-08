using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class CoreSnapshotSecondarySectionsSmoke
{
    internal static void Run()
    {
        const ulong step = 30;
        var worldId = OpaqueId128.Parse("000000000000000000000000000000f1");
        var triggerId = OpaqueId128.Parse("000000000000000000000000000000f2");
        var regionId = OpaqueId128.Parse("000000000000000000000000000000f3");
        var scopeId = OpaqueId128.Parse("000000000000000000000000000000f4");
        var intentId = OpaqueId128.Parse("000000000000000000000000000000f5");
        var triggerOrder = new SameStepOrderKey(1, 50, SHA256.HashData("detail-trigger"u8), 0, intentId);
        var scheduledTrigger = new ScheduledOperationRefV1(triggerId, step, triggerOrder);
        var frozenInput = new FrozenStepInputV1(
            worldId,
            step,
            1,
            SHA256.HashData("config-placeholder"u8),
            new[] { scheduledTrigger });
        var triggerAuthority = DetailTransitionTriggerAuthorityV1.FromStep(frozenInput);
        var resident = new StableToken("resident");
        var pending = DetailTransitionAdmissionV1.Admit(
            new DetailTransitionRequestV1(
                regionId,
                resident,
                DetailLevelV1.D2RegionalAggregate,
                DetailLevelV1.D0Entity,
                step + 1,
                -10,
                DetailTransitionTriggerSourceV1.ScheduledOperation,
                triggerId,
                step,
                100),
            triggerAuthority);
        var levels = StandardDomainExecutionPlanV1.Create().Entries
            .OrderBy(static entry => entry.DomainToken.Value, StringComparer.Ordinal)
            .Select(static entry => new KeyValuePair<StableToken, DetailLevelV1>(entry.DomainToken, DetailLevelV1.D2RegionalAggregate))
            .ToArray();
        var directory = new DetailDirectoryV1(
            new[]
            {
                new DetailRegionStateV1(
                    regionId,
                    scopeId,
                    levels,
                    1,
                    0,
                    new[] { DetailTransitionGuardV1.ActiveTransaction })
            },
            new[] { pending });
        var config = new CoreConfigCoordinator().LoadStartup(
            """
            [meta]
            format = "machiverse-config"
            schema_version = "1.0"
            component = "simulation-core"
            """);

        var detailAuthority = DetailDirectorySubstateV1.Canonicalize(directory);
        var domainRegistryDigest = HashSuite.DomainHash("mv.test-domain-registry-owner.v1", writer =>
        {
            writer.WriteArrayStart(1); writer.WriteUnsigned(step);
        });
        var state = new WorldStateV1(
            new WorldStateHeaderV1(
                worldId,
                step,
                SHA256.HashData("world-seed"u8),
                config.Generation,
                1,
                1,
                SHA256.HashData("state-29"u8)),
            EmptyPartitions(),
            OperationSchedulerSubstateV1.Canonicalize(new OperationSchedulerStateV1(step, null), step),
            DurableOperationSubstateV1.Canonicalize(Array.Empty<DurableOperationStateV1>()),
            detailAuthority,
            new WorldSubstateRefV1(new SchemaRefV1("core.domain-registry-state"), domainRegistryDigest),
            config.Digest);

        var detailOwner = FrozenDetailDirectorySnapshotOwnerV1.Freeze(step, directory);
        var configOwner = FrozenCoreConfigSnapshotOwnerV1.Freeze(step, config);
        var registryOwner = new FixedOwnerMaterial(
            CoreSnapshotOwnerSectionRegistryV1.DomainRegistry,
            step,
            state.DomainRegistryState.Schema,
            domainRegistryDigest);
        var cut = CoreSnapshotOwnerMaterialCutV1.Create(
            state,
            Array.Empty<DurableOperationStateV1>(),
            Array.Empty<ScheduledOperationRefV1>(),
            new IFrozenCoreSnapshotOwnerMaterialV1[] { detailOwner, registryOwner, configOwner });

        var detailSection = CoreSnapshotSecondarySectionProviderV1.CreateDetail(cut);
        var configSection = CoreSnapshotSecondarySectionProviderV1.CreateConfig(cut);
        Verify(detailSection, CoreSnapshotSecondarySemanticVerifierV1.Detail(step));
        Verify(configSection, CoreSnapshotSecondarySemanticVerifierV1.Config(step));

        if (detailSection.LogicalItemCount != 2 || configSection.LogicalItemCount != (ulong)CoreConfigSchema.Fields.Count)
            throw new InvalidOperationException("Secondary Core snapshot item counts are incorrect.");
        if (detailSection.Fragments.Any(static fragment => fragment.FirstRecordId is not null || fragment.LastRecordId is not null) ||
            configSection.Fragments.Any(static fragment => fragment.FirstRecordId is not null || fragment.LastRecordId is not null))
            throw new InvalidOperationException("Core section fragments must not fabricate record-id ranges.");

        var staleDetail = CoreSnapshotSecondarySemanticVerifierV1.Detail(step + 1);
        RequireRejected(
            () => staleDetail.Verify(detailSection.Fragments),
            "snapshot-core.detail.step-mismatch",
            "Stale detail snapshot Step must be rejected.");
        var staleConfig = CoreSnapshotSecondarySemanticVerifierV1.Config(step + 1);
        RequireRejected(
            () => staleConfig.Verify(configSection.Fragments),
            "snapshot-core.config.step-mismatch",
            "Stale config snapshot Step must be rejected.");

        var unknownConfigPayload = configSection.Fragments[0].FragmentPayload.Concat(new byte[] { 0x30, 0x00 }).ToArray();
        var unknownConfigFragments = configSection.Fragments
            .Select((fragment, index) => index == 0 ? fragment with { FragmentPayload = unknownConfigPayload } : fragment)
            .ToArray();
        RequireRejected(
            () => CoreSnapshotSecondarySemanticVerifierV1.Config(step).Verify(unknownConfigFragments),
            "snapshot-core.config.unknown-field",
            "Unknown authoritative config field must be rejected.");

        var nonCanonical = new Dictionary<string, object>(config.Fields, StringComparer.Ordinal)
        {
            ["simulation.step-rate.numerator"] = 60L,
            ["simulation.step-rate.denominator"] = 2L,
        };
        RequireRejected(
            () => CoreConfigSnapshotRehydrationV1.Rehydrate(1, new ReadOnlyDictionary<string, object>(nonCanonical)),
            "snapshot-core.config.noncanonical-normalization",
            "Non-reduced config step rate must be rejected during snapshot recovery.");

        if (config.Fields is IDictionary<string, object> mutable)
        {
            mutable["runtime.worker-count"] = 16L;
            var frozenConfigAuthority = configOwner.RecomputeAuthority();
            if (!CryptographicOperations.FixedTimeEquals(frozenConfigAuthority.CanonicalDigest, state.Diagnostic.ConfigDigest))
                throw new InvalidOperationException("Frozen config owner observed caller-side mutation after freeze.");
        }

        var fixedDetailCut = CoreSnapshotOwnerMaterialCutV1.Create(
            state,
            Array.Empty<DurableOperationStateV1>(),
            Array.Empty<ScheduledOperationRefV1>(),
            new IFrozenCoreSnapshotOwnerMaterialV1[]
            {
                new FixedOwnerMaterial(CoreSnapshotOwnerSectionRegistryV1.DetailDirectory, step, state.DetailState.Schema, state.DetailState.CanonicalDigest),
                registryOwner,
                configOwner,
            });
        RequireRejected(
            () => CoreSnapshotSecondarySectionProviderV1.CreateDetail(fixedDetailCut),
            "snapshot-core.detail.production-owner-required",
            "Digest-only detail owner must not serialize as recovery material.");
    }

    private static OrderedPartitionDirectoryV1 EmptyPartitions()
        => new(StandardDomainPartitionRegistry.Entries.Select(identity => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                identity,
                1,
                0,
                DetailLevelV1.D0Entity,
                0,
                SHA256.HashData(Encoding.ASCII.GetBytes(identity.PartitionId.Value))))));

    private static void Verify(CanonicalSnapshotSectionMaterialV1 section, SnapshotSectionSemanticVerifierV1 verifier)
    {
        var verified = verifier.Verify(section.Fragments);
        if (verified.LogicalItemCount != section.LogicalItemCount ||
            !CryptographicOperations.FixedTimeEquals(verified.LogicalContentDigest, section.LogicalContentDigest))
            throw new InvalidOperationException($"Secondary Core snapshot verifier mismatch: {section.SectionId}");
    }

    private static void RequireRejected<T>(Func<T> action, string expectedMessage, string failureMessage)
    {
        var rejected = false;
        try { _ = action(); }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage) { rejected = true; }
        if (!rejected) throw new InvalidOperationException(failureMessage);
    }

    private sealed class FixedOwnerMaterial : IFrozenCoreSnapshotOwnerMaterialV1
    {
        private readonly CoreSnapshotOwnerAuthorityV1 _authority;

        internal FixedOwnerMaterial(string sectionId, ulong basisStep, SchemaRefV1 schema, byte[] digest)
        {
            SectionId = sectionId;
            BasisStep = basisStep;
            _authority = new CoreSnapshotOwnerAuthorityV1(schema, digest);
        }

        public string SectionId { get; }
        public ulong BasisStep { get; }
        public CoreSnapshotOwnerAuthorityV1 RecomputeAuthority()
            => new(_authority.Schema, _authority.CanonicalDigest);
    }
}
