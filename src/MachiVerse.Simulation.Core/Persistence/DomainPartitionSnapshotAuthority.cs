using System.Collections.ObjectModel;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Untyped production authority boundary for a frozen authoritative domain partition.
/// A snapshot provider must consume this material rather than trusting WorldState header counts/digests.
/// The authority also exposes the actual canonical record-id set used to build the all-97 reference resolver.
/// </summary>
public interface IDomainPartitionSnapshotAuthorityV1 : IDomainPartitionSnapshotReferenceSourceV1
{
    DomainPartitionIdentityV1 Identity { get; }
    PartitionStateHeaderV1 Header { get; }
    void VerifyBoundAuthority();
}

public sealed class DomainPartitionSnapshotAuthorityV1<TPayload> : IDomainPartitionSnapshotAuthorityV1
{
    private readonly Func<TPayload, byte[]> _canonicalPayloadDigest;

    public DomainPartitionSnapshotAuthorityV1(
        DomainPartitionStateV1<TPayload> partition,
        PartitionStateHeaderV1 header,
        Func<TPayload, byte[]> canonicalPayloadDigest)
    {
        Partition = partition ?? throw new ArgumentNullException(nameof(partition));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        _canonicalPayloadDigest = canonicalPayloadDigest ?? throw new ArgumentNullException(nameof(canonicalPayloadDigest));
        var recordIds = Partition.RecordsCanonical.Select(static record => record.RecordId).ToArray();
        if (checked((ulong)recordIds.Length) != Partition.ItemCount)
            throw new InvalidDataException($"persistence.snapshot.partition-authority-record-count:{Partition.Identity.PartitionId.Value}");
        RecordIdsCanonical = Array.AsReadOnly(recordIds);
        VerifyBoundAuthority();
    }

    public DomainPartitionStateV1<TPayload> Partition { get; }
    public StableToken PartitionId => Partition.Identity.PartitionId;
    public DomainPartitionIdentityV1 Identity => Partition.Identity;
    public PartitionStateHeaderV1 Header { get; }
    public ulong ActualItemCount => Partition.ItemCount;
    public SchemaRefV1 RecordSchema => Identity.RecordSchema;
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }

    public void VerifyBoundAuthority()
    {
        var standard = StandardDomainPartitionRegistry.Get(PartitionId.Value);
        if (Identity != standard)
            throw new InvalidDataException($"persistence.snapshot.partition-authority-identity-mismatch:{PartitionId.Value}");
        if (Header.PartitionId != Identity.PartitionId ||
            Header.OwnerDomain != Identity.OwnerDomain ||
            Header.Schema != Identity.PartitionSchema)
        {
            throw new InvalidDataException($"persistence.snapshot.partition-header-identity-mismatch:{PartitionId.Value}");
        }
        if (Header.ItemCount != Partition.ItemCount)
            throw new InvalidDataException($"persistence.snapshot.partition-header-item-count-mismatch:{PartitionId.Value}");
        if (ActualItemCount != checked((ulong)RecordIdsCanonical.Count))
            throw new InvalidDataException($"persistence.snapshot.partition-authority-record-count:{PartitionId.Value}");

        OpaqueId128? previous = null;
        foreach (var recordId in RecordIdsCanonical)
        {
            if (recordId.IsZero)
                throw new InvalidDataException($"persistence.snapshot.partition-authority-record-zero:{PartitionId.Value}");
            if (previous is { } prior && prior.CompareTo(recordId) >= 0)
                throw new InvalidDataException($"persistence.snapshot.partition-authority-record-order:{PartitionId.Value}");
            previous = recordId;
        }

        var recomputed = PartitionStateHeaderV1.CreateCanonical(
            Partition,
            Header.Revision,
            Header.BasisStep,
            Header.DetailLevel,
            payload =>
            {
                var digest = _canonicalPayloadDigest(payload)
                    ?? throw new InvalidDataException($"persistence.snapshot.partition-payload-digest-null:{PartitionId.Value}");
                if (digest.Length != 32)
                    throw new InvalidDataException($"persistence.snapshot.partition-payload-digest-length:{PartitionId.Value}");
                return digest;
            });

        if (recomputed.Revision != Header.Revision ||
            recomputed.BasisStep != Header.BasisStep ||
            recomputed.DetailLevel != Header.DetailLevel ||
            recomputed.ItemCount != Header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recomputed.CanonicalDigest, Header.CanonicalDigest))
        {
            throw new InvalidDataException($"persistence.snapshot.partition-header-material-mismatch:{PartitionId.Value}");
        }
    }
}

/// <summary>
/// Exact frozen 97-partition material set. Construction fails unless every standard partition has
/// exactly one actual material owner and every owner is bound to the frozen WorldState header.
/// A record-schema migration is accepted only when it is explicitly registered; all other identity
/// fields remain byte-for-byte equivalent to the standard partition identity.
/// </summary>
public sealed class DomainPartitionSnapshotAuthoritySetV1
{
    private readonly IReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1> _byPartition;

    public DomainPartitionSnapshotAuthoritySetV1(
        WorldStateV1 frozenState,
        IEnumerable<IDomainPartitionSnapshotAuthorityV1> authorities)
    {
        FrozenState = frozenState ?? throw new ArgumentNullException(nameof(frozenState));
        ArgumentNullException.ThrowIfNull(authorities);

        var materialized = authorities.ToArray();
        if (materialized.Length != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("persistence.snapshot.partition-authority-count-mismatch");

        var map = new Dictionary<string, IDomainPartitionSnapshotAuthorityV1>(StringComparer.Ordinal);
        foreach (var authority in materialized)
        {
            ArgumentNullException.ThrowIfNull(authority);
            authority.VerifyBoundAuthority();
            if (!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedPartitionIdentity(authority.Identity))
                throw new InvalidDataException($"persistence.snapshot.partition-authority-identity-mismatch:{authority.PartitionId.Value}");
            if (!map.TryAdd(authority.PartitionId.Value, authority))
                throw new InvalidDataException($"persistence.snapshot.partition-authority-duplicate:{authority.PartitionId.Value}");
        }

        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            if (!map.TryGetValue(identity.PartitionId.Value, out var authority))
                throw new InvalidDataException($"persistence.snapshot.partition-authority-missing:{identity.PartitionId.Value}");
            if (!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedPartitionIdentity(authority.Identity) ||
                authority.Identity.PartitionId != identity.PartitionId)
                throw new InvalidDataException($"persistence.snapshot.partition-authority-identity-mismatch:{identity.PartitionId.Value}");

            var frozenHeader = FrozenState.Partitions.Get(identity.PartitionId.Value).Header;
            RequireSameHeader(frozenHeader, authority.Header, identity.PartitionId.Value);
        }

        _byPartition = new ReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1>(map);
    }

    public WorldStateV1 FrozenState { get; }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> CanonicalAuthorities
        => StandardDomainPartitionRegistry.Entries.Select(identity => _byPartition[identity.PartitionId.Value]).ToArray();

    public IDomainPartitionSnapshotAuthorityV1 Get(string partitionId)
        => _byPartition.TryGetValue(partitionId, out var value)
            ? value
            : throw new KeyNotFoundException(partitionId);

    private static void RequireSameHeader(
        PartitionStateHeaderV1 frozen,
        PartitionStateHeaderV1 actual,
        string partitionId)
    {
        if (frozen.PartitionId != actual.PartitionId ||
            frozen.OwnerDomain != actual.OwnerDomain ||
            frozen.Schema != actual.Schema ||
            frozen.Revision != actual.Revision ||
            frozen.BasisStep != actual.BasisStep ||
            frozen.DetailLevel != actual.DetailLevel ||
            frozen.ItemCount != actual.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(frozen.CanonicalDigest, actual.CanonicalDigest))
        {
            throw new InvalidDataException($"persistence.snapshot.partition-frozen-header-mismatch:{partitionId}");
        }
    }
}

/// <summary>
/// Schema-owner provider seam for the 97 domain sections. Structural-only standalone calls may omit
/// references; exact-97 production composition always supplies the resolver built from actual records.
/// </summary>
public interface IDomainPartitionSnapshotSectionProviderV1
{
    string SectionId { get; }
    SchemaRefV1 SectionSchema { get; }
    CanonicalSnapshotSectionMaterialV1 Create(
        IDomainPartitionSnapshotAuthorityV1 authority,
        IDomainRecordSchemaResolverV1? references = null);
    SnapshotSectionSemanticVerifierV1 CreateSemanticVerifier(
        PartitionStateHeaderV1 expectedHeader,
        IDomainRecordSchemaResolverV1? references = null);
}

public static class DomainPartitionSnapshotProductionProviderV1
{
    public static IReadOnlyList<CanonicalSnapshotSectionMaterialV1> CreateAll97(
        DomainPartitionSnapshotAuthoritySetV1 authorities,
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> providers)
    {
        ArgumentNullException.ThrowIfNull(authorities);
        ArgumentNullException.ThrowIfNull(providers);
        var byId = ValidateProviderSet(providers);
        var references = new DomainSnapshotReferenceResolverV1(authorities.CanonicalAuthorities);
        var sections = new List<CanonicalSnapshotSectionMaterialV1>(StandardDomainPartitionRegistry.StandardPartitionCount);
        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            var authority = authorities.Get(identity.PartitionId.Value);
            var provider = DomainSnapshotRecordSchemaMigrationProviderRegistryV1.Resolve(
                authority,
                byId[identity.PartitionId.Value]);
            var section = provider.Create(authority, references)
                ?? throw new InvalidDataException($"persistence.snapshot.partition-provider-null:{identity.PartitionId.Value}");
            if (!string.Equals(section.SectionId, identity.PartitionId.Value, StringComparison.Ordinal) ||
                section.SectionSchema != identity.PartitionSchema)
                throw new InvalidDataException($"persistence.snapshot.partition-provider-schema-mismatch:{identity.PartitionId.Value}");
            if (section.LogicalItemCount != authority.ActualItemCount ||
                !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, authority.Header.CanonicalDigest))
                throw new InvalidDataException($"persistence.snapshot.partition-provider-material-mismatch:{identity.PartitionId.Value}");
            sections.Add(section);
        }
        return Array.AsReadOnly(sections.OrderBy(static value => value.SectionId, StringComparer.Ordinal).ToArray());
    }

    public static CanonicalSnapshotSemanticVerifierRegistryV1 CreateSemanticVerifierRegistry(
        IEnumerable<SnapshotSectionSemanticVerifierV1> coreVerifiers,
        DomainPartitionSnapshotAuthoritySetV1 authorities,
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> providers)
    {
        ArgumentNullException.ThrowIfNull(coreVerifiers);
        ArgumentNullException.ThrowIfNull(authorities);
        var byId = ValidateProviderSet(providers);
        var references = new DomainSnapshotReferenceResolverV1(authorities.CanonicalAuthorities);
        var verifiers = coreVerifiers.ToList();
        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            var authority = authorities.Get(identity.PartitionId.Value);
            var provider = DomainSnapshotRecordSchemaMigrationProviderRegistryV1.Resolve(
                authority,
                byId[identity.PartitionId.Value]);
            var verifier = provider.CreateSemanticVerifier(authority.Header, references)
                ?? throw new InvalidDataException($"persistence.snapshot.partition-verifier-null:{identity.PartitionId.Value}");
            if (!string.Equals(verifier.SectionId, identity.PartitionId.Value, StringComparison.Ordinal) ||
                verifier.SectionSchema != identity.PartitionSchema)
                throw new InvalidDataException($"persistence.snapshot.partition-verifier-schema-mismatch:{identity.PartitionId.Value}");
            verifiers.Add(verifier);
        }
        return new CanonicalSnapshotSemanticVerifierRegistryV1(verifiers);
    }

    private static IReadOnlyDictionary<string, IDomainPartitionSnapshotSectionProviderV1> ValidateProviderSet(
        IEnumerable<IDomainPartitionSnapshotSectionProviderV1> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var materialized = providers.ToArray();
        if (materialized.Length != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("persistence.snapshot.partition-provider-count-mismatch");
        var map = new Dictionary<string, IDomainPartitionSnapshotSectionProviderV1>(StringComparer.Ordinal);
        foreach (var provider in materialized)
        {
            ArgumentNullException.ThrowIfNull(provider);
            var identity = StandardDomainPartitionRegistry.Get(provider.SectionId);
            if (provider.SectionSchema != identity.PartitionSchema)
                throw new InvalidDataException($"persistence.snapshot.partition-provider-schema-mismatch:{provider.SectionId}");
            if (!map.TryAdd(provider.SectionId, provider))
                throw new InvalidDataException($"persistence.snapshot.partition-provider-duplicate:{provider.SectionId}");
        }
        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            if (!map.ContainsKey(identity.PartitionId.Value))
                throw new InvalidDataException($"persistence.snapshot.partition-provider-missing:{identity.PartitionId.Value}");
        }
        return new ReadOnlyDictionary<string, IDomainPartitionSnapshotSectionProviderV1>(map);
    }
}
