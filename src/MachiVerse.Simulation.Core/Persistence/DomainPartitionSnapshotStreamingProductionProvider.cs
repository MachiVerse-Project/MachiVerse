using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Parallel exact-97 production composition for a frozen Terrain v2 authority whose payloads are
/// supplied as a repeatable stream. The other 96 partitions continue to use their existing providers;
/// only spatial.terrain_geometry bypasses the materialized section boundary.
/// </summary>
public static class DomainPartitionSnapshotStreamingProductionProviderV1
{
    public static IReadOnlyList<CanonicalSnapshotStreamingSectionV1> CreateAll97WithTerrainV2(
        DomainPartitionSnapshotAuthoritySetV1 authorities,
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> providers)
    {
        ArgumentNullException.ThrowIfNull(authorities);
        ArgumentNullException.ThrowIfNull(providers);

        var byId = ValidateProviderSet(providers);
        var references = new DomainSnapshotCompactReferenceResolverV1(authorities.CanonicalAuthorities);
        var sections = new List<CanonicalSnapshotStreamingSectionV1>(
            StandardDomainPartitionRegistry.StandardPartitionCount);

        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            var authority = authorities.Get(identity.PartitionId.Value);
            CanonicalSnapshotStreamingSectionV1 section;

            if (string.Equals(
                    identity.PartitionId.Value,
                    SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                    StringComparison.Ordinal))
            {
                if (authority is not SpatialTerrainGeometryStreamingSnapshotAuthorityV2 terrain)
                    throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-authority-type");
                terrain.VerifyBoundAuthority();

                var fragments = new SpatialTerrainGeometryStreamingFragmentSourceV2(
                    terrain.Header,
                    terrain.ActualItemCount,
                    terrain.EnumerateCanonicalRecords,
                    references);
                section = new CanonicalSnapshotStreamingSectionV1(
                    identity.PartitionId.Value,
                    identity.PartitionSchema,
                    terrain.ActualItemCount,
                    terrain.Header.CanonicalDigest,
                    fragments.EnumerateFragments);
            }
            else
            {
                var provider = DomainSnapshotRecordSchemaMigrationProviderRegistryV1.Resolve(
                    authority,
                    byId[identity.PartitionId.Value]);
                var materialized = provider.Create(authority, references)
                    ?? throw new InvalidDataException(
                        $"persistence.snapshot.partition-provider-null:{identity.PartitionId.Value}");
                RequireSectionMatchesAuthority(identity, authority, materialized);
                section = CanonicalSnapshotStreamingSectionV1.FromMaterialized(materialized);
            }

            if (!string.Equals(section.SectionId, identity.PartitionId.Value, StringComparison.Ordinal) ||
                section.SectionSchema != identity.PartitionSchema ||
                section.LogicalItemCount != authority.ActualItemCount ||
                !CryptographicOperations.FixedTimeEquals(
                    section.LogicalContentDigest,
                    authority.Header.CanonicalDigest))
            {
                throw new InvalidDataException(
                    $"persistence.snapshot.partition-stream-provider-material-mismatch:{identity.PartitionId.Value}");
            }
            sections.Add(section);
        }

        return Array.AsReadOnly(
            sections.OrderBy(static section => section.SectionId, StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyDictionary<string, IDomainPartitionSnapshotSectionProviderV1> ValidateProviderSet(
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> providers)
    {
        var materialized = providers.ToArray();
        if (materialized.Length != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("persistence.snapshot.partition-provider-count-mismatch");

        var map = new Dictionary<string, IDomainPartitionSnapshotSectionProviderV1>(StringComparer.Ordinal);
        foreach (var provider in materialized)
        {
            ArgumentNullException.ThrowIfNull(provider);
            var identity = StandardDomainPartitionRegistry.Get(provider.SectionId);
            if (provider.SectionSchema != identity.PartitionSchema)
                throw new InvalidDataException(
                    $"persistence.snapshot.partition-provider-schema-mismatch:{provider.SectionId}");
            if (!map.TryAdd(provider.SectionId, provider))
                throw new InvalidDataException(
                    $"persistence.snapshot.partition-provider-duplicate:{provider.SectionId}");
        }

        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            if (!map.ContainsKey(identity.PartitionId.Value))
                throw new InvalidDataException(
                    $"persistence.snapshot.partition-provider-missing:{identity.PartitionId.Value}");
        }
        return map;
    }

    private static void RequireSectionMatchesAuthority(
        DomainPartitionIdentityV1 identity,
        IDomainPartitionSnapshotAuthorityV1 authority,
        CanonicalSnapshotSectionMaterialV1 section)
    {
        if (!string.Equals(section.SectionId, identity.PartitionId.Value, StringComparison.Ordinal) ||
            section.SectionSchema != identity.PartitionSchema ||
            section.LogicalItemCount != authority.ActualItemCount ||
            !CryptographicOperations.FixedTimeEquals(
                section.LogicalContentDigest,
                authority.Header.CanonicalDigest))
        {
            throw new InvalidDataException(
                $"persistence.snapshot.partition-provider-material-mismatch:{identity.PartitionId.Value}");
        }
    }
}
