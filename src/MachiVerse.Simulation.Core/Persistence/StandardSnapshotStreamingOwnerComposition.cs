using System.Security.Cryptography;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Parallel exact-103 owner composition for large sections that must remain streaming. Core owners
/// keep their existing canonical materialized sections; Domain composition replaces only the Terrain
/// v2 section with its bounded-memory fragment source.
/// </summary>
public static class StandardSnapshotStreamingOwnerCompositionV1
{
    public static IReadOnlyList<CanonicalSnapshotStreamingSectionV1> CreateAll103WithTerrainV2(
        CoreSnapshotOwnerMaterialCutV1 coreCut,
        DomainPartitionSnapshotAuthoritySetV1 domainAuthorities,
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> domainProviders)
    {
        ArgumentNullException.ThrowIfNull(coreCut);
        ArgumentNullException.ThrowIfNull(domainAuthorities);
        ArgumentNullException.ThrowIfNull(domainProviders);
        RequireSameFrozenHeader(coreCut.Header, domainAuthorities.FrozenState.Header);

        var coreSections = CoreSnapshotProductionSectionProviderV1.CreateAllSix(coreCut);
        CoreSnapshotProductionSectionProviderV1.VerifyAllSix(
            coreSections,
            coreCut.BasisStep,
            coreCut.Header.ConfigGeneration);
        var domainSections = DomainPartitionSnapshotStreamingProductionProviderV1.CreateAll97WithTerrainV2(
            domainAuthorities,
            domainProviders);

        var combined = coreSections
            .Select(CanonicalSnapshotStreamingSectionV1.FromMaterialized)
            .Concat(domainSections);
        var sections = CanonicalSnapshotStreamingSectionValidationV1.ValidateStandardMetadata(
            combined,
            domainAuthorities.FrozenState);

        if (sections.Count != SnapshotManifestValidation.StandardRequiredSectionCount || sections.Count != 103)
            throw new InvalidDataException("persistence.snapshot.standard-stream-section-count-not-103");
        if (sections.Count(static section => StandardSnapshotSectionSetV1.IsCoreSection(section.SectionId)) != 6)
            throw new InvalidDataException("persistence.snapshot.standard-stream-core-section-count-not-6");
        if (sections.Count(static section => StandardDomainPartitionRegistry.TryGet(section.SectionId, out _)) !=
            StandardDomainPartitionRegistry.StandardPartitionCount)
        {
            throw new InvalidDataException("persistence.snapshot.standard-stream-domain-section-count-not-97");
        }
        return sections;
    }

    private static void RequireSameFrozenHeader(WorldStateHeaderV1 core, WorldStateHeaderV1 domain)
    {
        if (core.WorldId != domain.WorldId ||
            core.Step != domain.Step ||
            core.ConfigGeneration != domain.ConfigGeneration ||
            core.MasterGeneration != domain.MasterGeneration ||
            core.RateGeneration != domain.RateGeneration ||
            !CryptographicOperations.FixedTimeEquals(core.WorldSeedDigest, domain.WorldSeedDigest) ||
            !SameOptionalDigest(core.PreviousStateDigest, domain.PreviousStateDigest))
        {
            throw new InvalidDataException("persistence.snapshot.standard-owner-cut-mismatch");
        }
    }

    private static bool SameOptionalDigest(byte[]? left, byte[]? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.Length == 32 && right.Length == 32 &&
               CryptographicOperations.FixedTimeEquals(left, right);
    }
}
