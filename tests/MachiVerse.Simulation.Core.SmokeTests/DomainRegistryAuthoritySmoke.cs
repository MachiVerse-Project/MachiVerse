using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class DomainRegistryAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        const ulong step = 30;
        var registry = StandardDomainRegistryAuthorityV1.Generation1;
        Require(registry.RegistryGeneration == 1, "DomainRegistry generation 1 is required.");
        Require(registry.Domains.Count == 8, "DomainRegistry must contain exactly eight standard domains.");
        Require(registry.Domains.SelectMany(static domain => domain.OwnedPartitions).Count() == 97,
            "DomainRegistry must own exactly 97 authoritative partitions.");
        Require(registry.Domains.SelectMany(static domain => domain.OwnedPartitions).Distinct().Count() == 97,
            "Every authoritative partition must have exactly one owner.");
        Require(registry.Domains.All(static domain => domain.StateReadDependencies.Count == 7),
            "Generation 1 runtime exposes the full immutable WorldState, so each domain has seven foreign state-read dependencies.");
        Require(registry.Domains.All(static domain => domain.SameStepDependencies.Count == 0),
            "Generation 1 standard runtime has no production same-Step dependency edges.");
        Require(registry.Domains.All(static domain => domain.EmittedEventKinds.Count == 0),
            "Generation 1 must not invent domain event producers.");
        Require(registry.Domains.All(static domain => domain.InvariantIds.Count == 0),
            "Generation 1 must not invent domain invariant producers.");

        var capabilities = StandardDomainRuntimeCapabilityRegistryV1.Generation1IntentCapabilities;
        Require(capabilities.Count == 4, "Generation 1 must contain only the four actual Intent producer capabilities.");
        Require(capabilities.Count(static value => value.SourceDomain.Value == "physical_built") == 3,
            "Physical/Built must expose exactly the three implemented spatial geometry Intent kinds.");
        Require(capabilities.Count(static value => value.SourceDomain.Value == "resident") == 1,
            "Resident must expose exactly the implemented physical move Intent kind.");

        ExpectInvalid("duplicate domain", () =>
        {
            var domains = registry.Domains.Select(static source => Clone(source)).ToArray();
            domains[^1] = Clone(domains[^1], domainToken: domains[^2].DomainToken);
            _ = new DomainRegistryStateV1(1, domains);
        });
        ExpectInvalid("duplicate partition owner", () =>
        {
            var domains = registry.Domains.Select(static source => Clone(source)).ToArray();
            var targetIndex = Array.FindIndex(domains, static value => value.DomainToken.Value == "environment");
            var foreign = registry.Get(new StableToken("governance_security")).OwnedPartitions[0];
            var owned = domains[targetIndex].OwnedPartitions.Append(foreign)
                .OrderBy(static value => value.Value, StringComparer.Ordinal)
                .ToArray();
            domains[targetIndex] = Clone(domains[targetIndex], ownedPartitions: owned);
            _ = new DomainRegistryStateV1(1, domains);
        });
        ExpectInvalid("missing partition owner", () =>
        {
            var domains = registry.Domains.Select(static source => Clone(source)).ToArray();
            var targetIndex = Array.FindIndex(domains, static value => value.DomainToken.Value == "environment");
            domains[targetIndex] = Clone(domains[targetIndex], ownedPartitions: domains[targetIndex].OwnedPartitions.Skip(1).ToArray());
            _ = new DomainRegistryStateV1(1, domains);
        });
        ExpectInvalid("unknown domain token", () =>
        {
            var domains = registry.Domains.Select(static source => Clone(source)).ToArray();
            domains[^1] = Clone(domains[^1], domainToken: new StableToken("zz_unknown"));
            _ = new DomainRegistryStateV1(1, domains);
        });
        ExpectInvalid("generation mismatch", () => _ = new DomainRegistryStateV1(2, registry.Domains));
        ExpectInvalid("noncanonical ordering", () =>
        {
            var domains = registry.Domains.Select(static source => Clone(source)).ToArray();
            (domains[0], domains[1]) = (domains[1], domains[0]);
            _ = new DomainRegistryStateV1(1, domains);
        });

        var staleDomains = registry.Domains.Select(static source => Clone(source)).ToArray();
        var staleIndex = Array.FindIndex(staleDomains, static value => value.DomainToken.Value == "environment");
        staleDomains[staleIndex] = Clone(
            staleDomains[staleIndex],
            domainSchema: new SchemaRefV1("domain.environment.runtime.stale"));
        var staleRegistry = new DomainRegistryStateV1(1, staleDomains);
        ExpectInvalid("stale registry material", staleRegistry.RequireExactStandardGeneration1);

        var worldId = OpaqueId128.Parse("000000000000000000000000000000a1");
        var config = new CoreConfigCoordinator().LoadStartup(
            """
            [meta]
            format = "machiverse-config"
            schema_version = "1.0"
            component = "simulation-core"
            """);
        var state = BuildState(worldId, step, config, registry);
        var frozenInput = new FrozenStepInputV1(worldId, step, config.Generation, config.Digest, Array.Empty<ScheduledOperationRefV1>());

        var unauthorized = CreateUnauthorizedIntent(step);
        ExpectInvalid("unauthorized emitted intent", () => registry.RequireAuthorizedEmission(unauthorized));

        var plan = StandardDomainExecutionPlanV1.Create();
        var runtimes = plan.Entries.Select(entry => (IDomainRuntimeV1)new TestRuntime(
            entry.DomainToken,
            entry.DomainToken.Value == "resident" ? unauthorized : null)).ToArray();
        var executorRejected = false;
        try
        {
            await DomainRuntimeExecutorV1.ExecuteAsync(plan, state, frozenInput, runtimes, workerCount: 4);
        }
        catch (InvalidDataException ex) when (ex.Message == "domain-runtime.unauthorized-emitted-intent")
        {
            executorRejected = true;
        }
        Require(executorRejected, "DomainRuntimeExecutorV1 must fail closed on an unregistered emitted Intent.");

        var detail = new DetailDirectoryV1(Array.Empty<DetailRegionStateV1>(), Array.Empty<DetailTransitionCandidateV1>());
        var cut = CoreSnapshotOwnerMaterialCutV1.Create(
            state,
            Array.Empty<DurableOperationStateV1>(),
            Array.Empty<ScheduledOperationRefV1>(),
            new IFrozenCoreSnapshotOwnerMaterialV1[]
            {
                FrozenDetailDirectorySnapshotOwnerV1.Freeze(step, detail),
                FrozenDomainRegistrySnapshotOwnerV1.Freeze(step, registry),
                FrozenCoreConfigSnapshotOwnerV1.Freeze(step, config),
            });
        var sections = CoreSnapshotProductionSectionProviderV1.CreateAllSix(cut);
        Require(sections.Count == 6, "Core snapshot must materialize all six production Core sections.");
        CoreSnapshotProductionSectionProviderV1.VerifyAllSix(sections, step, config.Generation);

        var registrySection = sections.Single(static section => section.SectionId == CoreSnapshotOwnerSectionRegistryV1.DomainRegistry);
        var restored = CoreSnapshotDomainRegistrySemanticVerifierV1.Restore(registrySection.Fragments, step);
        Require(restored.CanonicalDigest.SequenceEqual(registry.CanonicalDigest),
            "DomainRegistry snapshot recovery must recompute the same authority digest.");

        ExpectInvalid("snapshot payload tamper", () =>
        {
            var payload = registrySection.Fragments[0].FragmentPayload.ToArray();
            payload[^1] ^= 0x01;
            var fragment = registrySection.Fragments[0] with { FragmentPayload = payload };
            var tampered = ReplaceSection(sections, registrySection with
            {
                Fragments = Array.AsReadOnly(new[] { fragment })
            });
            CoreSnapshotProductionSectionProviderV1.VerifyAllSix(tampered, step, config.Generation);
        });

        ExpectInvalid("snapshot semantic digest mismatch", () =>
        {
            var mismatched = ReplaceSection(sections, registrySection with
            {
                LogicalContentDigest = SHA256.HashData("wrong-domain-registry-digest"u8)
            });
            CoreSnapshotProductionSectionProviderV1.VerifyAllSix(mismatched, step, config.Generation);
        });

        ExpectInvalid("stale registry snapshot material", () =>
        {
            var payload = CoreDomainRegistrySnapshotWireCodecV1.Encode(step, staleRegistry.RegistryGeneration, staleRegistry.Domains);
            var staleFragment = new SnapshotSectionFragmentMaterialV1(
                CoreSnapshotOwnerSectionRegistryV1.DomainRegistry,
                0,
                1,
                null,
                null,
                8,
                payload);
            var staleSection = registrySection with
            {
                LogicalContentDigest = staleRegistry.CanonicalDigest.ToArray(),
                Fragments = Array.AsReadOnly(new[] { staleFragment })
            };
            CoreSnapshotProductionSectionProviderV1.VerifyAllSix(
                ReplaceSection(sections, staleSection),
                step,
                config.Generation);
        });
    }

    private static WorldStateV1 BuildState(
        OpaqueId128 worldId,
        ulong step,
        EffectiveCoreConfig config,
        DomainRegistryStateV1 registry)
    {
        var partitions = StandardDomainPartitionRegistry.Entries.Select(identity => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                identity,
                revision: 1,
                basisStep: step,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(identity.PartitionId.Value)))));
        var scheduler = OperationSchedulerSubstateV1.Canonicalize(
            new OperationSchedulerStateV1(step, null, Array.Empty<ScheduledOperationRefV1>()),
            step);
        var operations = DurableOperationSubstateV1.Canonicalize(Array.Empty<DurableOperationStateV1>());
        var detail = DetailDirectorySubstateV1.Canonicalize(
            new DetailDirectoryV1(Array.Empty<DetailRegionStateV1>(), Array.Empty<DetailTransitionCandidateV1>()));
        return new WorldStateV1(
            new WorldStateHeaderV1(
                worldId,
                step,
                SHA256.HashData("domain-registry-smoke-seed"u8),
                config.Generation,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            scheduler,
            operations,
            detail,
            registry.ToWorldSubstateRef(),
            config.Digest);
    }

    private static MutationIntentCandidateV1 CreateUnauthorizedIntent(ulong step)
    {
        var physical = new StableToken("physical_built");
        var partition = new StableToken("physical.presence");
        return new MutationIntentCandidateV1(
            OpaqueId128.Parse("000000000000000000000000000000a2"),
            phase: 1,
            sourceDomain: new StableToken("resident"),
            targetDomain: physical,
            targetPartitionId: partition,
            basisStep: step,
            mutationKind: new StableToken("physical.intent.future-unimplemented"),
            targetScope: new ConflictScopeV1(
                physical,
                partition,
                new byte[] { 1 },
                new StableToken("resident.move")),
            semanticPriority: 0,
            resolutionMode: ConflictResolutionModeV1.CustomDeterministic,
            semanticPayloadDigest: SHA256.HashData("unauthorized-intent"u8));
    }

    private static IReadOnlyList<CanonicalSnapshotSectionMaterialV1> ReplaceSection(
        IReadOnlyList<CanonicalSnapshotSectionMaterialV1> sections,
        CanonicalSnapshotSectionMaterialV1 replacement)
        => Array.AsReadOnly(sections
            .Select(section => string.Equals(section.SectionId, replacement.SectionId, StringComparison.Ordinal) ? replacement : section)
            .OrderBy(static section => section.SectionId, StringComparer.Ordinal)
            .ToArray());

    private static DomainRuntimeDescriptorV1 Clone(
        DomainRuntimeDescriptorV1 source,
        StableToken? domainToken = null,
        SchemaRefV1? domainSchema = null,
        IReadOnlyList<StableToken>? ownedPartitions = null)
        => new(
            domainToken ?? source.DomainToken,
            source.DomainRank,
            domainSchema ?? source.DomainSchema,
            ownedPartitions ?? source.OwnedPartitions,
            source.StateReadDependencies,
            source.SameStepDependencies,
            source.AcceptedIntentKinds,
            source.EmittedIntentKinds,
            source.EmittedEventKinds,
            source.InvariantIds);

    private static void ExpectInvalid(string name, Action action)
    {
        var rejected = false;
        try { action(); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, $"Negative test must reject: {name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestRuntime(StableToken domainToken, MutationIntentCandidateV1? emittedIntent) : IDomainRuntimeV1
    {
        public StableToken DomainToken { get; } = domainToken;

        public ValueTask<DomainCandidateOutputV1> ExecuteAsync(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<MutationIntentCandidateV1> intents = emittedIntent is null
                ? Array.Empty<MutationIntentCandidateV1>()
                : new[] { emittedIntent };
            return ValueTask.FromResult(new DomainCandidateOutputV1(DomainToken, context.FrozenInput.BasisStep, intents));
        }
    }
}
