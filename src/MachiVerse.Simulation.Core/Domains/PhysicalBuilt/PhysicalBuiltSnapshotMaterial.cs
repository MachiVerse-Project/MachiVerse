using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

public sealed class PhysicalBuiltDomainStateV1
{
    public PhysicalBuiltDomainStateV1(
        DomainPartitionStateV1<PhysicalPresencePayloadV1> presence,
        DomainPartitionStateV1<PhysicalOccupancyPayloadV1> occupancy,
        DomainPartitionStateV1<BuiltStructurePayloadV1> structure,
        DomainPartitionStateV1<BuiltSpacePayloadV1> space,
        DomainPartitionStateV1<BuiltOpeningPayloadV1> opening,
        DomainPartitionStateV1<PhysicalContainerLocationPayloadV1> containerLocation,
        DomainPartitionStateV1<BuiltWorksitePayloadV1> worksite,
        DomainPartitionStateV1<PhysicalConditionPayloadV1> condition,
        DomainPartitionStateV1<PhysicalCombustionPayloadV1> combustion,
        DomainPartitionStateV1<PhysicalMaterialHandoffPayloadV1> materialHandoff,
        DomainPartitionStateV1<PhysicalLineagePayloadV1> lineage)
    {
        Presence = RequireIdentity(presence, PhysicalPresencePayloadV1.PartitionId);
        Occupancy = RequireIdentity(occupancy, PhysicalOccupancyPayloadV1.PartitionId);
        Structure = RequireIdentity(structure, BuiltStructurePayloadV1.PartitionId);
        Space = RequireIdentity(space, BuiltSpacePayloadV1.PartitionId);
        Opening = RequireIdentity(opening, BuiltOpeningPayloadV1.PartitionId);
        ContainerLocation = RequireIdentity(containerLocation, PhysicalContainerLocationPayloadV1.PartitionId);
        Worksite = RequireIdentity(worksite, BuiltWorksitePayloadV1.PartitionId);
        Condition = RequireIdentity(condition, PhysicalConditionPayloadV1.PartitionId);
        Combustion = RequireIdentity(combustion, PhysicalCombustionPayloadV1.PartitionId);
        MaterialHandoff = RequireIdentity(materialHandoff, PhysicalMaterialHandoffPayloadV1.PartitionId);
        Lineage = RequireIdentity(lineage, PhysicalLineagePayloadV1.PartitionId);
    }

    public DomainPartitionStateV1<PhysicalPresencePayloadV1> Presence { get; }
    public DomainPartitionStateV1<PhysicalOccupancyPayloadV1> Occupancy { get; }
    public DomainPartitionStateV1<BuiltStructurePayloadV1> Structure { get; }
    public DomainPartitionStateV1<BuiltSpacePayloadV1> Space { get; }
    public DomainPartitionStateV1<BuiltOpeningPayloadV1> Opening { get; }
    public DomainPartitionStateV1<PhysicalContainerLocationPayloadV1> ContainerLocation { get; }
    public DomainPartitionStateV1<BuiltWorksitePayloadV1> Worksite { get; }
    public DomainPartitionStateV1<PhysicalConditionPayloadV1> Condition { get; }
    public DomainPartitionStateV1<PhysicalCombustionPayloadV1> Combustion { get; }
    public DomainPartitionStateV1<PhysicalMaterialHandoffPayloadV1> MaterialHandoff { get; }
    public DomainPartitionStateV1<PhysicalLineagePayloadV1> Lineage { get; }

    public static PhysicalBuiltDomainStateV1 CreateEmpty()
        => new(
            Empty<PhysicalPresencePayloadV1>(PhysicalPresencePayloadV1.PartitionId),
            Empty<PhysicalOccupancyPayloadV1>(PhysicalOccupancyPayloadV1.PartitionId),
            Empty<BuiltStructurePayloadV1>(BuiltStructurePayloadV1.PartitionId),
            Empty<BuiltSpacePayloadV1>(BuiltSpacePayloadV1.PartitionId),
            Empty<BuiltOpeningPayloadV1>(BuiltOpeningPayloadV1.PartitionId),
            Empty<PhysicalContainerLocationPayloadV1>(PhysicalContainerLocationPayloadV1.PartitionId),
            Empty<BuiltWorksitePayloadV1>(BuiltWorksitePayloadV1.PartitionId),
            Empty<PhysicalConditionPayloadV1>(PhysicalConditionPayloadV1.PartitionId),
            Empty<PhysicalCombustionPayloadV1>(PhysicalCombustionPayloadV1.PartitionId),
            Empty<PhysicalMaterialHandoffPayloadV1>(PhysicalMaterialHandoffPayloadV1.PartitionId),
            Empty<PhysicalLineagePayloadV1>(PhysicalLineagePayloadV1.PartitionId));

    public PhysicalBuiltDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState)
        => PhysicalBuiltDomainSnapshotMaterialV1.Bind(frozenState, this);

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(
            StandardDomainPartitionRegistry.Get(partitionId),
            Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static DomainPartitionStateV1<TPayload> RequireIdentity<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        string partitionId)
    {
        ArgumentNullException.ThrowIfNull(partition);
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"physical-built.runtime-state.partition-identity:{partitionId}");
        return partition;
    }
}

public sealed class PhysicalBuiltDomainSnapshotMaterialV1
{
    private PhysicalBuiltDomainSnapshotMaterialV1(IEnumerable<IDomainPartitionSnapshotAuthorityV1> authorities)
    {
        var materialized = authorities?.ToArray() ?? throw new ArgumentNullException(nameof(authorities));
        if (materialized.Length != 11)
            throw new InvalidDataException("physical-built.snapshot-material.authority-count");

        var byId = new Dictionary<string, IDomainPartitionSnapshotAuthorityV1>(StringComparer.Ordinal);
        foreach (var authority in materialized)
        {
            ArgumentNullException.ThrowIfNull(authority);
            authority.VerifyBoundAuthority();
            if (!string.Equals(authority.Identity.OwnerDomain.Value, "physical_built", StringComparison.Ordinal))
                throw new InvalidDataException($"physical-built.snapshot-material.foreign-owner:{authority.PartitionId.Value}");
            if (!byId.TryAdd(authority.PartitionId.Value, authority))
                throw new InvalidDataException($"physical-built.snapshot-material.duplicate:{authority.PartitionId.Value}");
        }

        Presence = Require<PhysicalPresencePayloadV1>(byId, PhysicalPresencePayloadV1.PartitionId);
        Occupancy = Require<PhysicalOccupancyPayloadV1>(byId, PhysicalOccupancyPayloadV1.PartitionId);
        Structure = Require<BuiltStructurePayloadV1>(byId, BuiltStructurePayloadV1.PartitionId);
        Space = Require<BuiltSpacePayloadV1>(byId, BuiltSpacePayloadV1.PartitionId);
        Opening = Require<BuiltOpeningPayloadV1>(byId, BuiltOpeningPayloadV1.PartitionId);
        ContainerLocation = Require<PhysicalContainerLocationPayloadV1>(byId, PhysicalContainerLocationPayloadV1.PartitionId);
        Worksite = Require<BuiltWorksitePayloadV1>(byId, BuiltWorksitePayloadV1.PartitionId);
        Condition = Require<PhysicalConditionPayloadV1>(byId, PhysicalConditionPayloadV1.PartitionId);
        Combustion = Require<PhysicalCombustionPayloadV1>(byId, PhysicalCombustionPayloadV1.PartitionId);
        MaterialHandoff = Require<PhysicalMaterialHandoffPayloadV1>(byId, PhysicalMaterialHandoffPayloadV1.PartitionId);
        Lineage = Require<PhysicalLineagePayloadV1>(byId, PhysicalLineagePayloadV1.PartitionId);
        Authorities = Array.AsReadOnly(materialized.OrderBy(static value => value.PartitionId.Value, StringComparer.Ordinal).ToArray());
    }

    public DomainPartitionSnapshotAuthorityV1<PhysicalPresencePayloadV1> Presence { get; }
    public DomainPartitionSnapshotAuthorityV1<PhysicalOccupancyPayloadV1> Occupancy { get; }
    public DomainPartitionSnapshotAuthorityV1<BuiltStructurePayloadV1> Structure { get; }
    public DomainPartitionSnapshotAuthorityV1<BuiltSpacePayloadV1> Space { get; }
    public DomainPartitionSnapshotAuthorityV1<BuiltOpeningPayloadV1> Opening { get; }
    public DomainPartitionSnapshotAuthorityV1<PhysicalContainerLocationPayloadV1> ContainerLocation { get; }
    public DomainPartitionSnapshotAuthorityV1<BuiltWorksitePayloadV1> Worksite { get; }
    public DomainPartitionSnapshotAuthorityV1<PhysicalConditionPayloadV1> Condition { get; }
    public DomainPartitionSnapshotAuthorityV1<PhysicalCombustionPayloadV1> Combustion { get; }
    public DomainPartitionSnapshotAuthorityV1<PhysicalMaterialHandoffPayloadV1> MaterialHandoff { get; }
    public DomainPartitionSnapshotAuthorityV1<PhysicalLineagePayloadV1> Lineage { get; }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> Authorities { get; }

    public static PhysicalBuiltDomainSnapshotMaterialV1 Bind(WorldStateV1 frozenState, PhysicalBuiltDomainStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        ArgumentNullException.ThrowIfNull(state);
        return new PhysicalBuiltDomainSnapshotMaterialV1(
        [
            Bind(frozenState, state.Presence, PhysicalPresencePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Occupancy, PhysicalOccupancyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Structure, BuiltStructurePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Space, BuiltSpacePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Opening, BuiltOpeningPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.ContainerLocation, PhysicalContainerLocationPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Worksite, BuiltWorksitePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Condition, PhysicalConditionPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Combustion, PhysicalCombustionPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.MaterialHandoff, PhysicalMaterialHandoffPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Lineage, PhysicalLineagePayloadV1.PartitionId, static value => value.CanonicalDigest()),
        ]);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Bind<TPayload>(
        WorldStateV1 frozenState,
        DomainPartitionStateV1<TPayload> partition,
        string partitionId,
        Func<TPayload, byte[]> digest)
    {
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"physical-built.snapshot-material.partition-identity:{partitionId}");
        return new DomainPartitionSnapshotAuthorityV1<TPayload>(
            partition,
            frozenState.Partitions.Get(partitionId).Header,
            digest);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Require<TPayload>(
        IReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1> byId,
        string partitionId)
    {
        if (!byId.TryGetValue(partitionId, out var authority))
            throw new InvalidDataException($"physical-built.snapshot-material.missing:{partitionId}");
        return authority as DomainPartitionSnapshotAuthorityV1<TPayload>
            ?? throw new InvalidDataException($"physical-built.snapshot-material.payload-type:{partitionId}");
    }
}
