using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Production composition boundary for the eight standard Domain owners. This only composes typed
/// owner material already supplied by the runtimes; it never creates partition material from headers.
/// Registered record-schema migrations may provide an owner-specific material generation while the
/// standard provider list and exact 97-partition boundary remain unchanged.
/// </summary>
public static class StandardDomainSnapshotOwnerCompositionV1
{
    public static IReadOnlyList<IDomainPartitionSnapshotSectionProviderV1> CreateAllProviders()
    {
        var providers = new List<IDomainPartitionSnapshotSectionProviderV1>(StandardDomainPartitionRegistry.StandardPartitionCount);
        providers.AddRange(ResidentDomainSnapshotProviderV1.CreateAll());
        providers.AddRange(ParticipationDomainSnapshotProviderV1.CreateAll());
        providers.AddRange(PhysicalBuiltDomainSnapshotProviderV1.CreateAll());
        providers.AddRange(SpatialDomainSnapshotProviderV1.CreateAll());
        providers.AddRange(EnvironmentDomainSnapshotProviderV1.CreateAll());
        providers.AddRange(SocietyEconomyDomainSnapshotProviderV1.CreateAll());
        providers.AddRange(InfrastructureInformationDomainSnapshotProviderV1.CreateAll());
        providers.AddRange(GovernanceSecurityDomainSnapshotProviderV1.CreateAll());

        var ordered = providers.OrderBy(static p => p.SectionId, StringComparer.Ordinal).ToArray();
        var expected = StandardDomainPartitionRegistry.Entries
            .OrderBy(static identity => identity.PartitionId.Value, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length != StandardDomainPartitionRegistry.StandardPartitionCount || ordered.Length != expected.Length)
            throw new InvalidDataException("persistence.snapshot.standard-provider-count-not-97");

        for (var i = 0; i < ordered.Length; i++)
        {
            var provider = ordered[i] ?? throw new InvalidDataException("persistence.snapshot.standard-provider-null");
            if (!string.Equals(provider.SectionId, expected[i].PartitionId.Value, StringComparison.Ordinal) ||
                provider.SectionSchema != expected[i].PartitionSchema)
                throw new InvalidDataException($"persistence.snapshot.standard-provider-set-mismatch:{expected[i].PartitionId.Value}");
            if (i > 0 && string.Equals(ordered[i - 1].SectionId, provider.SectionId, StringComparison.Ordinal))
                throw new InvalidDataException($"persistence.snapshot.standard-provider-duplicate:{provider.SectionId}");
        }
        return Array.AsReadOnly(ordered);
    }

    public static DomainPartitionSnapshotAuthoritySetV1 CreateAuthoritySet(
        WorldStateV1 frozenState,
        ResidentDomainSnapshotMaterialV1 resident,
        ParticipationDomainSnapshotMaterialV1 participation,
        PhysicalBuiltDomainSnapshotMaterialV1 physicalBuilt,
        SpatialDomainSnapshotMaterialV1 spatial,
        EnvironmentDomainSnapshotMaterialV1 environment,
        SocietyEconomyDomainSnapshotMaterialV1 societyEconomy,
        InfrastructureInformationDomainSnapshotMaterialV1 infrastructureInformation,
        GovernanceSecurityDomainSnapshotMaterialV1 governanceSecurity)
        => CreateAuthoritySetCore(
            frozenState,
            resident?.Authorities,
            participation?.Authorities,
            physicalBuilt?.Authorities,
            spatial?.Authorities,
            environment?.Authorities,
            societyEconomy?.Authorities,
            infrastructureInformation?.Authorities,
            governanceSecurity?.Authorities);

    public static DomainPartitionSnapshotAuthoritySetV1 CreateAuthoritySet(
        WorldStateV1 frozenState,
        ResidentDomainSnapshotMaterialV1 resident,
        ParticipationDomainSnapshotMaterialV1 participation,
        PhysicalBuiltDomainSnapshotMaterialV1 physicalBuilt,
        SpatialDomainSnapshotMaterialV2 spatial,
        EnvironmentDomainSnapshotMaterialV1 environment,
        SocietyEconomyDomainSnapshotMaterialV1 societyEconomy,
        InfrastructureInformationDomainSnapshotMaterialV1 infrastructureInformation,
        GovernanceSecurityDomainSnapshotMaterialV1 governanceSecurity)
        => CreateAuthoritySetCore(
            frozenState,
            resident?.Authorities,
            participation?.Authorities,
            physicalBuilt?.Authorities,
            spatial?.Authorities,
            environment?.Authorities,
            societyEconomy?.Authorities,
            infrastructureInformation?.Authorities,
            governanceSecurity?.Authorities);

    private static DomainPartitionSnapshotAuthoritySetV1 CreateAuthoritySetCore(
        WorldStateV1 frozenState,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1>? resident,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1>? participation,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1>? physicalBuilt,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1>? spatial,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1>? environment,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1>? societyEconomy,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1>? infrastructureInformation,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1>? governanceSecurity)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        var authorities = new List<IDomainPartitionSnapshotAuthorityV1>(StandardDomainPartitionRegistry.StandardPartitionCount);
        Add(authorities, resident, nameof(resident));
        Add(authorities, participation, nameof(participation));
        Add(authorities, physicalBuilt, nameof(physicalBuilt));
        Add(authorities, spatial, nameof(spatial));
        Add(authorities, environment, nameof(environment));
        Add(authorities, societyEconomy, nameof(societyEconomy));
        Add(authorities, infrastructureInformation, nameof(infrastructureInformation));
        Add(authorities, governanceSecurity, nameof(governanceSecurity));
        return new DomainPartitionSnapshotAuthoritySetV1(frozenState, authorities);
    }

    private static void Add(
        ICollection<IDomainPartitionSnapshotAuthorityV1> target,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1>? source,
        string name)
    {
        if (source is null) throw new ArgumentNullException(name);
        foreach (var authority in source) target.Add(authority);
    }
}
