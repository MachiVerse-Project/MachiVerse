using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record CanonicalSnapshotSemanticRecoveryResultV1(
    WorldStateHeaderV1 Header,
    int SectionCount,
    int CoreSectionCount,
    int DomainSectionCount,
    ulong DomainLogicalRecordCount,
    byte[] StateDigest);

/// <summary>
/// Gate3 semantic recovery boundary for one already-selected durable exact-103 Snapshot.
/// Inputs are limited to the durable recovery result, the current persistence-generation paths and
/// production chunk decoders. The authoritative in-memory WorldState is deliberately not accepted.
/// Domain references are rebuilt from recovered record identities before schema-owner semantic
/// verification; Terrain v2 remains streaming so a complete Terrain payload set is never retained.
/// </summary>
public static class CanonicalSnapshotSemanticRecoveryV1
{
    private sealed record RecoveredDomainSourceV1(
        IDomainPartitionSnapshotReferenceSourceV1 Source,
        PartitionStateHeaderV1 Header);

    public static async Task<CanonicalSnapshotSemanticRecoveryResultV1> RecoverAndRehashAsync(
        CanonicalSnapshotDurableRecoveryResultV1 recovered,
        WorldPersistencePaths world,
        IEnumerable<ISnapshotChunkCompressionDecoderV1>? compressionDecoders = null,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recovered);
        ArgumentNullException.ThrowIfNull(world);
        if (recovered.Sections.Count != SnapshotManifestValidation.StandardRequiredSectionCount)
            throw new InvalidDataException("persistence.snapshot.semantic-recovery-section-count");

        var physical = CreateFinalReadAlias(recovered, world);
        var terrainBuilder = new SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2.Builder();
        var sources = new List<RecoveredDomainSourceV1>(StandardDomainPartitionRegistry.StandardPartitionCount);
        WorldStateHeaderV1? header = null;

        await foreach (var section in ReadMaterializedNonTerrainSectionsAsync(
            physical,
            recovered.Manifest,
            terrainBuilder,
            compressionDecoders,
            cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(
                    section.SectionId,
                    CoreSnapshotOwnerSectionRegistryV1.WorldStateHeader,
                    StringComparison.Ordinal))
            {
                if (section.Fragments.Count != 1 || section.LogicalItemCount != 1)
                    throw new InvalidDataException("persistence.snapshot.semantic-recovery-world-header-shape");
                header = CoreWorldStateHeaderSnapshotWireCodecV1.Decode(section.Fragments[0].FragmentPayload);
                continue;
            }

            if (!StandardDomainPartitionRegistry.TryGet(section.SectionId, out var identity) || identity is null)
                continue;

            var source = CreateRecoveredSource(identity, section, nestedCodecs);
            var sourceHeader = GetRecoveredHeader(source, identity.PartitionId.Value);
            RequireSectionMatchesRecoveredAuthority(section, source, sourceHeader);
            sources.Add(new RecoveredDomainSourceV1(source, sourceHeader));
        }

        var terrain = terrainBuilder.Complete();
        var terrainIdentity = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        var terrainLogical = FindLogicalSection(recovered.Manifest.Logical, terrainIdentity.PartitionId.Value);
        RequireLogicalMatchesRecoveredAuthority(terrainLogical, terrain, terrain.Header);
        sources.Add(new RecoveredDomainSourceV1(terrain, terrain.Header));

        if (header is null)
            throw new InvalidDataException("persistence.snapshot.semantic-recovery-world-header-missing");
        RequireHeaderMatchesManifest(header, recovered.Manifest.Logical, recovered.Catalog);

        var orderedSources = sources
            .OrderBy(static entry => entry.Source.PartitionId.Value, StringComparer.Ordinal)
            .ToArray();
        if (orderedSources.Length != StandardDomainPartitionRegistry.StandardPartitionCount ||
            orderedSources.Select(static entry => entry.Source.PartitionId.Value).Distinct(StringComparer.Ordinal).Count() != orderedSources.Length)
            throw new InvalidDataException("persistence.snapshot.semantic-recovery-domain-source-count");

        var references = new DomainSnapshotCompactReferenceResolverV1(
            orderedSources.Select(static entry => entry.Source));
        var sourceByPartition = orderedSources.ToDictionary(
            static entry => entry.Source.PartitionId.Value,
            StringComparer.Ordinal);
        var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders()
            .ToDictionary(static provider => provider.SectionId, StringComparer.Ordinal);
        var coreVerifiers = CreateCoreVerifiers(header);
        var coreSemantic = new Dictionary<string, SnapshotSectionSemanticVerificationV1>(StringComparer.Ordinal);
        var domainSemantic = new Dictionary<string, SnapshotSectionSemanticVerificationV1>(StringComparer.Ordinal);

        await foreach (var section in ReadMaterializedNonTerrainSectionsAsync(
            physical,
            recovered.Manifest,
            terrainBuilder: null,
            compressionDecoders: compressionDecoders,
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (StandardSnapshotSectionSetV1.IsCoreSection(section.SectionId))
            {
                if (!coreVerifiers.TryGetValue(section.SectionId, out var coreVerifier))
                    throw new InvalidDataException($"persistence.snapshot.semantic-verifier-missing:{section.SectionId}");
                var coreVerification = VerifySection(coreVerifier, section, references);
                RequireSemanticMatchesSection(section, coreVerification);
                if (!coreSemantic.TryAdd(section.SectionId, coreVerification))
                    throw new InvalidDataException($"persistence.snapshot.semantic-recovery-core-duplicate:{section.SectionId}");
                continue;
            }

            if (!StandardDomainPartitionRegistry.TryGet(section.SectionId, out var identity) || identity is null)
                throw new InvalidDataException($"persistence.snapshot.semantic-recovery-section-unexpected:{section.SectionId}");
            if (!sourceByPartition.TryGetValue(section.SectionId, out var recoveredSource))
                throw new InvalidDataException($"persistence.snapshot.semantic-recovery-domain-source-missing:{section.SectionId}");
            if (!providers.TryGetValue(section.SectionId, out var standardProvider))
                throw new InvalidDataException($"persistence.snapshot.partition-provider-missing:{section.SectionId}");

            var provider = ResolveRecoveredProvider(identity, recoveredSource.Source.RecordSchema, standardProvider);
            var domainVerifier = provider.CreateSemanticVerifier(recoveredSource.Header, references);
            var domainVerification = VerifySection(domainVerifier, section, references);
            RequireSemanticMatchesSection(section, domainVerification);
            RequireSemanticMatchesHeader(recoveredSource.Header, domainVerification);
            if (!domainSemantic.TryAdd(section.SectionId, domainVerification))
                throw new InvalidDataException($"persistence.snapshot.semantic-recovery-domain-duplicate:{section.SectionId}");
        }

        if (!sourceByPartition.TryGetValue(SpatialTerrainGeometryRecordSchemaV2.PartitionId, out var terrainSource) ||
            terrainSource.Source is not SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 terrainV2)
            throw new InvalidDataException("persistence.snapshot.semantic-recovery-terrain-v2-source");

        var terrainSemantic = await SpatialTerrainGeometryStreamingSemanticVerifierV2.VerifyAsync(
            terrainSource.Header,
            ReadSectionFragmentsAsync(
                physical,
                recovered.Manifest,
                SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                compressionDecoders,
                cancellationToken),
            terrainV2,
            references,
            cancellationToken).ConfigureAwait(false);
        RequireSemanticMatchesLogical(terrainLogical, terrainSemantic);
        RequireSemanticMatchesHeader(terrainSource.Header, terrainSemantic);
        if (!domainSemantic.TryAdd(SpatialTerrainGeometryRecordSchemaV2.PartitionId, terrainSemantic))
            throw new InvalidDataException("persistence.snapshot.semantic-recovery-domain-duplicate:spatial.terrain_geometry");

        if (coreSemantic.Count != 6 || domainSemantic.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("persistence.snapshot.semantic-recovery-verifier-coverage");

        var configDigest = GetCoreDigest(coreSemantic, CoreSnapshotOwnerSectionRegistryV1.ConfigState);
        if (header.ConfigGeneration != recovered.Manifest.Logical.SimulationConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(configDigest, recovered.Manifest.Logical.SimulationConfigDigest))
            throw new InvalidDataException("persistence.snapshot.semantic-recovery-config-manifest-mismatch");

        var stateDigest = ComputeWorldStateDigest(
            header,
            sourceByPartition,
            domainSemantic,
            configDigest,
            GetCoreDigest(coreSemantic, CoreSnapshotOwnerSectionRegistryV1.SchedulerState),
            GetCoreDigest(coreSemantic, CoreSnapshotOwnerSectionRegistryV1.OperationState),
            GetCoreDigest(coreSemantic, CoreSnapshotOwnerSectionRegistryV1.DetailDirectory),
            GetCoreDigest(coreSemantic, CoreSnapshotOwnerSectionRegistryV1.DomainRegistry));

        ulong domainLogicalRecordCount = 0;
        foreach (var semantic in domainSemantic.Values)
            domainLogicalRecordCount = checked(domainLogicalRecordCount + semantic.LogicalItemCount);

        return new CanonicalSnapshotSemanticRecoveryResultV1(
            header,
            recovered.Sections.Count,
            coreSemantic.Count,
            domainSemantic.Count,
            domainLogicalRecordCount,
            stateDigest);
    }

    private static IReadOnlyDictionary<string, SnapshotSectionSemanticVerifierV1> CreateCoreVerifiers(
        WorldStateHeaderV1 header)
    {
        var values = new SnapshotSectionSemanticVerifierV1[]
        {
            CoreSnapshotSecondarySemanticVerifierV1.Config(header.Step, header.ConfigGeneration),
            CoreSnapshotSecondarySemanticVerifierV1.Detail(header.Step),
            CoreSnapshotDomainRegistrySemanticVerifierV1.Create(header.Step),
            CoreOperationStateSnapshotSectionProviderV2.SemanticVerifier(header.Step),
            CoreSnapshotPrimarySemanticVerifierV1.Scheduler(header.Step),
            CoreSnapshotPrimarySemanticVerifierV1.WorldStateHeader(header.Step),
        };
        return values.ToDictionary(static value => value.SectionId, StringComparer.Ordinal);
    }

    private static SnapshotSectionSemanticVerificationV1 VerifySection(
        SnapshotSectionSemanticVerifierV1 verifier,
        CanonicalSnapshotSectionMaterialV1 section,
        IDomainRecordSchemaResolverV1 references)
    {
        if (!string.Equals(verifier.SectionId, section.SectionId, StringComparison.Ordinal) ||
            verifier.SectionSchema != section.SectionSchema)
            throw new InvalidDataException($"persistence.snapshot.semantic-verifier-schema-mismatch:{section.SectionId}");

        return verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(references))
            : verifier.Verify(section.Fragments);
    }

    private static IDomainPartitionSnapshotSectionProviderV1 ResolveRecoveredProvider(
        DomainPartitionIdentityV1 identity,
        SchemaRefV1 recordSchema,
        IDomainPartitionSnapshotSectionProviderV1 standardProvider)
    {
        if (recordSchema == identity.RecordSchema)
            return standardProvider;
        if (!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                identity.PartitionId.Value,
                recordSchema))
            throw new InvalidDataException($"persistence.snapshot.migrated-provider-unavailable:{identity.PartitionId.Value}");

        var registration = DomainSnapshotRecordSchemaMigrationProviderRegistryV1.Entries.SingleOrDefault(entry =>
            string.Equals(entry.PartitionId, identity.PartitionId.Value, StringComparison.Ordinal) &&
            entry.RecordSchema == recordSchema)
            ?? throw new InvalidDataException($"persistence.snapshot.migrated-provider-unavailable:{identity.PartitionId.Value}");
        var provider = registration.CreateProvider()
            ?? throw new InvalidDataException($"persistence.snapshot.migrated-provider-null:{identity.PartitionId.Value}");
        if (!string.Equals(provider.SectionId, identity.PartitionId.Value, StringComparison.Ordinal) ||
            provider.SectionSchema != identity.PartitionSchema)
            throw new InvalidDataException($"persistence.snapshot.migrated-provider-schema:{identity.PartitionId.Value}");
        return provider;
    }

    private static IDomainPartitionSnapshotReferenceSourceV1 CreateRecoveredSource(
        DomainPartitionIdentityV1 identity,
        CanonicalSnapshotSectionMaterialV1 section,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs)
    {
        if (section.LogicalItemCount == 0)
            return new DomainSnapshotRecoveredReferenceSourceV1(identity.PartitionId.Value, section.Fragments, nestedCodecs);

        if (string.Equals(identity.PartitionId.Value, InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, StringComparison.Ordinal))
            return TryMigratedSource(
                () => new InfrastructureNetworkTopologyRecoveredReferenceSourceV2(section.Fragments),
                () => new DomainSnapshotRecoveredReferenceSourceV1(identity.PartitionId.Value, section.Fragments, nestedCodecs),
                "persistence.snapshot.recovered-reference-infrastructure-network-schema-unrecognized");
        if (string.Equals(identity.PartitionId.Value, PhysicalOccupancyRecordSchemaV2.PartitionId, StringComparison.Ordinal))
            return TryMigratedSource(
                () => new PhysicalOccupancyRecoveredReferenceSourceV2(section.Fragments),
                () => new DomainSnapshotRecoveredReferenceSourceV1(identity.PartitionId.Value, section.Fragments, nestedCodecs),
                "persistence.snapshot.recovered-reference-physical-occupancy-schema-unrecognized");
        if (string.Equals(identity.PartitionId.Value, SocietyMarketTransactionRecordSchemaV2.PartitionId, StringComparison.Ordinal))
            return TryMigratedSource(
                () => new SocietyMarketTransactionRecoveredReferenceSourceV2(section.Fragments),
                () => new DomainSnapshotRecoveredReferenceSourceV1(identity.PartitionId.Value, section.Fragments, nestedCodecs),
                "persistence.snapshot.recovered-reference-market-schema-unrecognized");

        return new DomainSnapshotRecoveredReferenceSourceV1(identity.PartitionId.Value, section.Fragments, nestedCodecs);
    }

    private static IDomainPartitionSnapshotReferenceSourceV1 TryMigratedSource(
        Func<IDomainPartitionSnapshotReferenceSourceV1> migrated,
        Func<IDomainPartitionSnapshotReferenceSourceV1> fallback,
        string error)
    {
        InvalidDataException? migratedFailure = null;
        try
        {
            return migrated();
        }
        catch (InvalidDataException ex)
        {
            migratedFailure = ex;
        }

        try
        {
            return fallback();
        }
        catch (InvalidDataException fallbackFailure)
        {
            throw new InvalidDataException(error, new AggregateException(migratedFailure, fallbackFailure));
        }
    }

    private static PartitionStateHeaderV1 GetRecoveredHeader(
        IDomainPartitionSnapshotReferenceSourceV1 source,
        string partitionId)
        => source switch
        {
            DomainSnapshotRecoveredReferenceSourceV1 v1 => v1.Header,
            DomainSnapshotStreamingRecoveredReferenceSourceV1 streamingV1 => streamingV1.Header,
            InfrastructureNetworkTopologyRecoveredReferenceSourceV2 infrastructureV2 => infrastructureV2.Header,
            PhysicalOccupancyRecoveredReferenceSourceV2 physicalV2 => physicalV2.Header,
            SocietyMarketTransactionRecoveredReferenceSourceV2 marketV2 => marketV2.Header,
            SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 terrainV2 => terrainV2.Header,
            _ => throw new InvalidDataException($"persistence.snapshot.recovered-reference-source-type:{partitionId}"),
        };

    private static SnapshotPhysicalPaths CreateFinalReadAlias(
        CanonicalSnapshotDurableRecoveryResultV1 recovered,
        WorldPersistencePaths world)
    {
        var relative = recovered.Catalog.RelativeDirectory.Replace('/', Path.DirectorySeparatorChar);
        var finalDirectory = Path.GetFullPath(Path.Combine(world.GenerationDirectory, relative));
        var expected = Path.GetFullPath(Path.Combine(world.SnapshotsDirectory, recovered.Catalog.SnapshotId.ToString()));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(finalDirectory, expected, comparison))
            throw new InvalidDataException("persistence.snapshot.semantic-recovery-final-directory");
        if (!Directory.Exists(finalDirectory))
            throw new InvalidDataException("persistence.snapshot.recovery-material-missing");

        var chunks = Path.Combine(finalDirectory, "chunks");
        var manifest = Path.Combine(finalDirectory, "manifest.pb");
        return new SnapshotPhysicalPaths(
            recovered.Catalog.SnapshotId,
            finalDirectory,
            finalDirectory,
            manifest,
            manifest,
            chunks,
            chunks);
    }

    private static async IAsyncEnumerable<CanonicalSnapshotSectionMaterialV1> ReadMaterializedNonTerrainSectionsAsync(
        SnapshotPhysicalPaths physical,
        PhysicalSnapshotManifestMaterialV1 manifest,
        SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2.Builder? terrainBuilder,
        IEnumerable<ISnapshotChunkCompressionDecoderV1>? compressionDecoders,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var logicalById = manifest.Logical.Sections.ToDictionary(static section => section.SectionId, StringComparer.Ordinal);
        string? currentSection = null;
        List<SnapshotSectionFragmentMaterialV1>? fragments = null;

        await foreach (var fragment in CanonicalSnapshotStagedFragmentStreamV1.ReadValidatedAsync(
            physical,
            manifest,
            compressionDecoders,
            cancellationToken).ConfigureAwait(false))
        {
            if (!string.Equals(currentSection, fragment.SectionId, StringComparison.Ordinal))
            {
                if (currentSection is not null && fragments is not null)
                    yield return MaterializeSection(logicalById, currentSection, fragments);
                currentSection = fragment.SectionId;
                fragments = string.Equals(
                        currentSection,
                        SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                        StringComparison.Ordinal)
                    ? null
                    : [];
            }

            if (fragments is null)
            {
                terrainBuilder?.Add(fragment);
                continue;
            }
            fragments.Add(fragment);
        }

        if (currentSection is not null && fragments is not null)
            yield return MaterializeSection(logicalById, currentSection, fragments);
    }

    private static async IAsyncEnumerable<SnapshotSectionFragmentMaterialV1> ReadSectionFragmentsAsync(
        SnapshotPhysicalPaths physical,
        PhysicalSnapshotManifestMaterialV1 manifest,
        string sectionId,
        IEnumerable<ISnapshotChunkCompressionDecoderV1>? compressionDecoders,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var fragment in CanonicalSnapshotStagedFragmentStreamV1.ReadValidatedAsync(
            physical,
            manifest,
            compressionDecoders,
            cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(fragment.SectionId, sectionId, StringComparison.Ordinal))
                yield return fragment;
        }
    }

    private static CanonicalSnapshotSectionMaterialV1 MaterializeSection(
        IReadOnlyDictionary<string, LogicalSnapshotSection> logicalById,
        string sectionId,
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments)
    {
        if (!logicalById.TryGetValue(sectionId, out var logical))
            throw new InvalidDataException($"persistence.snapshot.semantic-recovery-logical-section-missing:{sectionId}");
        ulong itemCount = 0;
        foreach (var fragment in fragments)
            itemCount = checked(itemCount + fragment.ItemCount);
        if (itemCount != logical.LogicalItemCount)
            throw new InvalidDataException($"persistence.snapshot.fragment-item-count-mismatch:{sectionId}");

        return new CanonicalSnapshotSectionMaterialV1(
            sectionId,
            new SchemaRefV1(logical.SchemaId, logical.SchemaMajor, logical.SchemaMinor),
            logical.LogicalItemCount,
            logical.LogicalContentDigest.ToArray(),
            Array.AsReadOnly(fragments.ToArray()));
    }

    private static LogicalSnapshotSection FindLogicalSection(LogicalSnapshotManifest logical, string sectionId)
        => logical.Sections.SingleOrDefault(section => string.Equals(section.SectionId, sectionId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"persistence.snapshot-section-missing:{sectionId}");

    private static void RequireSectionMatchesRecoveredAuthority(
        CanonicalSnapshotSectionMaterialV1 section,
        IDomainPartitionSnapshotReferenceSourceV1 source,
        PartitionStateHeaderV1 header)
    {
        if (!string.Equals(section.SectionId, source.PartitionId.Value, StringComparison.Ordinal) ||
            section.LogicalItemCount != source.ActualItemCount ||
            section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"persistence.snapshot.recovered-reference-section-authority:{section.SectionId}");
    }

    private static void RequireLogicalMatchesRecoveredAuthority(
        LogicalSnapshotSection section,
        IDomainPartitionSnapshotReferenceSourceV1 source,
        PartitionStateHeaderV1 header)
    {
        if (!string.Equals(section.SectionId, source.PartitionId.Value, StringComparison.Ordinal) ||
            section.LogicalItemCount != source.ActualItemCount ||
            section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"persistence.snapshot.recovered-reference-section-authority:{section.SectionId}");
    }

    private static void RequireHeaderMatchesManifest(
        WorldStateHeaderV1 header,
        LogicalSnapshotManifest logical,
        SnapshotCatalogEntry catalog)
    {
        if (header.WorldId != logical.WorldId ||
            header.Step != logical.SnapshotStep ||
            header.Step != catalog.SnapshotStep ||
            header.ConfigGeneration != logical.SimulationConfigGeneration ||
            header.MasterGeneration != logical.MasterGeneration)
            throw new InvalidDataException("persistence.snapshot.semantic-recovery-world-header-manifest-mismatch");
    }

    private static void RequireSemanticMatchesSection(
        CanonicalSnapshotSectionMaterialV1 section,
        SnapshotSectionSemanticVerificationV1 semantic)
    {
        if (semantic.LogicalItemCount != section.LogicalItemCount ||
            !CryptographicOperations.FixedTimeEquals(semantic.LogicalContentDigest, section.LogicalContentDigest))
            throw new InvalidDataException($"persistence.snapshot.section-semantic-digest-mismatch:{section.SectionId}");
    }

    private static void RequireSemanticMatchesLogical(
        LogicalSnapshotSection section,
        SnapshotSectionSemanticVerificationV1 semantic)
    {
        if (semantic.LogicalItemCount != section.LogicalItemCount ||
            !CryptographicOperations.FixedTimeEquals(semantic.LogicalContentDigest, section.LogicalContentDigest))
            throw new InvalidDataException($"persistence.snapshot.section-semantic-digest-mismatch:{section.SectionId}");
    }

    private static void RequireSemanticMatchesHeader(
        PartitionStateHeaderV1 header,
        SnapshotSectionSemanticVerificationV1 semantic)
    {
        if (semantic.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(semantic.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"persistence.snapshot.partition-restored-header-mismatch:{header.PartitionId.Value}");
    }

    private static byte[] GetCoreDigest(
        IReadOnlyDictionary<string, SnapshotSectionSemanticVerificationV1> semantic,
        string sectionId)
        => semantic.TryGetValue(sectionId, out var value)
            ? value.LogicalContentDigest
            : throw new InvalidDataException($"persistence.snapshot.semantic-recovery-core-missing:{sectionId}");

    private static byte[] ComputeWorldStateDigest(
        WorldStateHeaderV1 header,
        IReadOnlyDictionary<string, RecoveredDomainSourceV1> recoveredSources,
        IReadOnlyDictionary<string, SnapshotSectionSemanticVerificationV1> domainSemantic,
        byte[] configDigest,
        byte[] schedulerDigest,
        byte[] operationDigest,
        byte[] detailDigest,
        byte[] domainRegistryDigest)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(recoveredSources);
        ArgumentNullException.ThrowIfNull(domainSemantic);

        if (recoveredSources.Count != StandardDomainPartitionRegistry.StandardPartitionCount ||
            domainSemantic.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("persistence.snapshot.semantic-recovery-domain-count");

        var partitionRefs = recoveredSources
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                if (!domainSemantic.TryGetValue(pair.Key, out var semantic))
                    throw new InvalidDataException($"persistence.snapshot.semantic-recovery-domain-semantic-missing:{pair.Key}");
                if (semantic.LogicalItemCount != pair.Value.Header.ItemCount ||
                    !CryptographicOperations.FixedTimeEquals(
                        semantic.LogicalContentDigest,
                        pair.Value.Header.CanonicalDigest))
                    throw new InvalidDataException($"persistence.snapshot.semantic-recovery-domain-header-drift:{pair.Key}");
                return new PartitionStateRefV1(pair.Value.Header);
            })
            .ToArray();

        // Reconstruct only the digest-bearing WorldState surface and delegate the final state
        // diagnostic calculation to WorldStateV1 itself. This preserves the exact runtime
        // legacy-vs-hierarchy algorithm selection and the per-partition digest algorithm identity.
        var reconstructed = new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitionRefs),
            new WorldSubstateRefV1(new SchemaRefV1("core.scheduler-state"), schedulerDigest),
            new WorldSubstateRefV1(new SchemaRefV1("core.operation-state"), operationDigest),
            new WorldSubstateRefV1(new SchemaRefV1("core.detail-directory"), detailDigest),
            new WorldSubstateRefV1(new SchemaRefV1("core.domain-registry-state"), domainRegistryDigest),
            configDigest);

        return reconstructed.Diagnostic.StateDigest.ToArray();
    }}
}
