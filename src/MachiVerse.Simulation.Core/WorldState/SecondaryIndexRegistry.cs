using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

public enum IndexAuthorityV1
{
    Authoritative = 0,
    DerivedRebuildable = 1
}

public sealed record RequiredSecondaryIndexRegistrationV1(
    StableToken IndexId,
    StableToken PartitionId,
    IndexAuthorityV1 Authority);

public static class StandardSecondaryIndexRegistry
{
    private static readonly RequiredSecondaryIndexRegistrationV1[] CanonicalEntries = BuildEntries();
    private static readonly IReadOnlyDictionary<string, RequiredSecondaryIndexRegistrationV1> ById =
        CanonicalEntries.ToDictionary(static entry => entry.IndexId.Value, StringComparer.Ordinal);

    public static IReadOnlyList<RequiredSecondaryIndexRegistrationV1> Entries => CanonicalEntries;

    public static IReadOnlyList<RequiredSecondaryIndexRegistrationV1> ForPartition(string partitionId)
    {
        _ = StandardDomainPartitionRegistry.Get(partitionId);
        return CanonicalEntries
            .Where(entry => string.Equals(entry.PartitionId.Value, partitionId, StringComparison.Ordinal))
            .ToArray();
    }

    public static RequiredSecondaryIndexRegistrationV1 Get(string indexId)
        => ById.TryGetValue(indexId, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Unknown standard secondary IndexId: {indexId}");

    private static RequiredSecondaryIndexRegistrationV1[] BuildEntries()
    {
        var entries = new List<RequiredSecondaryIndexRegistrationV1>();

        Add(entries, "spatial.world_frame", "spatial.frame-by-parent");
        Add(entries, "spatial.scope_registry", "spatial.scope-by-parent", "spatial.scope-by-class");
        Add(entries, "spatial.terrain_geometry", "spatial.terrain-by-scope");
        Add(entries, "spatial.void_geometry", "spatial.void-by-entrance", "spatial.void-by-lifecycle");
        Add(entries, "spatial.containment_topology", "spatial.containment-by-subject", "spatial.containment-by-container");
        Add(entries, "spatial.boundary_topology", "spatial.boundary-by-scope");
        Add(entries, "spatial.detail_regions", "spatial.detail-region-by-scope");
        Add(entries, "spatial.geometry_lineage", "spatial.lineage-by-subject");

        Add(entries, "environment.geology", "environment.geology-by-scope");
        Add(entries, "environment.soil", "environment.soil-by-scope");
        Add(entries, "environment.resource_deposit", "environment.resource-by-kind", "environment.resource-by-scope");
        Add(entries, "environment.groundwater", "environment.groundwater-by-scope");
        Add(entries, "environment.atmosphere", "environment.atmosphere-by-scope");
        Add(entries, "environment.climate", "environment.climate-by-scope");
        Add(entries, "environment.weather", "environment.weather-by-scope", "environment.weather-by-class");
        Add(entries, "environment.surface_water", "environment.surface-water-by-scope");
        Add(entries, "environment.ocean", "environment.ocean-by-scope");
        Add(entries, "environment.ecosystem", "environment.ecosystem-by-scope", "environment.ecosystem-by-species");
        Add(entries, "environment.contaminant", "environment.contaminant-by-scope", "environment.contaminant-by-kind");
        Add(entries, "environment.hazard", "environment.hazard-by-scope", "environment.hazard-by-kind");
        Add(entries, "environment.environment_lineage", "environment.lineage-by-subject");

        Add(entries, "physical.presence", "physical.presence-by-subject", "physical.presence-by-scope");
        Add(entries, "physical.occupancy", "physical.occupancy-grid");
        Add(entries, "built.structure", "built.structure-by-scope", "built.structure-by-class");
        Add(entries, "built.space", "built.space-by-structure");
        Add(entries, "built.opening", "built.opening-by-space");
        Add(entries, "physical.container_location", "physical.container-by-subject", "physical.contents-by-container");
        Add(entries, "built.worksite", "built.worksite-by-target", "built.worksite-by-status");
        Add(entries, "physical.condition", "physical.condition-by-subject");
        Add(entries, "physical.combustion", "physical.combustion-by-subject", "physical.combustion-active");
        Add(entries, "physical.material_handoff", "physical.handoff-by-transaction");
        Add(entries, "physical.lineage", "physical.lineage-by-subject");

        Add(entries, "participation.binding", "participation.binding-by-diver", "participation.binding-by-resident");
        Add(entries, "participation.absence_policy", "participation.policy-by-diver");
        Add(entries, "participation.control_mode", "participation.control-by-resident");
        Add(entries, "participation.history", "participation.history-by-binding");
        Add(entries, "participation.detail_requirement", "participation.detail-by-resident");

        Add(entries, "resident.identity_lifecycle", "resident.lifecycle-by-status");
        Add(entries, "resident.body_health", "resident.health-by-resident");
        Add(entries, "resident.physiology", "resident.physiology-by-resident");
        Add(entries, "resident.perception", "resident.perception-by-resident", "resident.perception-by-subject");
        Add(entries, "resident.knowledge_belief", "resident.belief-by-resident", "resident.belief-by-subject");
        Add(entries, "resident.memory", "resident.memory-by-resident", "resident.memory-by-subject");
        Add(entries, "resident.psychology", "resident.psychology-by-resident");
        Add(entries, "resident.goal_plan", "resident.goal-by-resident", "resident.goal-by-status");
        Add(entries, "resident.skill_aptitude", "resident.skill-by-resident", "resident.skill-by-token");
        Add(entries, "resident.relationship", "resident.relation-by-subject", "resident.relation-by-object");
        Add(entries, "resident.family_lineage", "resident.family-by-resident", "resident.children-by-parent");
        Add(entries, "resident.behavior_state", "resident.behavior-by-resident", "resident.behavior-by-mode");
        Add(entries, "resident.lineage", "resident.lineage-by-resident");

        Add(entries, "society.organization", "society.org-by-class", "society.org-by-parent");
        Add(entries, "society.membership_role", "society.membership-by-org", "society.membership-by-member");
        Add(entries, "society.employment", "society.employment-by-employer", "society.employment-by-worker");
        Add(entries, "society.household", "society.household-by-member");
        Add(entries, "society.contract_claim", "society.contract-by-party", "society.contract-by-status");
        Add(entries, "society.property_right", "society.property-by-asset", "society.property-by-holder");
        Add(entries, "society.currency_money", "society.currency-by-token");
        Add(entries, "society.finance_account", "society.account-by-owner", "society.account-by-currency");
        Add(entries, "society.market_transaction", "society.market-by-market", "society.market-by-party", "society.market-by-status");
        Add(entries, "society.business_production", "society.production-by-org", "society.production-by-status");
        Add(entries, "society.logistics_obligation", "society.logistics-by-party", "society.logistics-by-status");
        Add(entries, "society.education", "society.education-by-learner", "society.education-by-provider");
        Add(entries, "society.culture", "society.culture-by-subject", "society.culture-by-trait");
        Add(entries, "society.reputation", "society.reputation-by-subject", "society.reputation-by-dimension");
        Add(entries, "society.information_claim", "society.claim-by-claimant", "society.claim-by-subject");
        Add(entries, "society.history_lineage", "society.history-by-subject");

        Add(entries, "governance.polity", "governance.polity-by-org", "governance.polity-by-status");
        Add(entries, "governance.institution", "governance.institution-by-polity", "governance.institution-by-kind");
        Add(entries, "governance.law_rule", "governance.rule-by-jurisdiction", "governance.rule-by-effective-step");
        Add(entries, "governance.jurisdiction", "governance.jurisdiction-by-scope", "governance.jurisdiction-by-polity");
        Add(entries, "governance.territorial_claim", "governance.claim-by-scope", "governance.claim-by-polity");
        Add(entries, "governance.effective_control", "governance.control-by-scope", "governance.control-by-controller");
        Add(entries, "governance.public_authority", "governance.authority-by-holder", "governance.authority-by-institution");
        Add(entries, "governance.tax_fiscal", "governance.tax-by-polity", "governance.tax-by-debtor");
        Add(entries, "governance.permission_license", "governance.permission-by-subject", "governance.permission-by-kind");
        Add(entries, "governance.diplomacy", "governance.diplomacy-by-party", "governance.diplomacy-by-kind");
        Add(entries, "governance.security_incident", "governance.incident-by-scope", "governance.incident-by-subject");
        Add(entries, "governance.investigation", "governance.investigation-by-incident", "governance.investigation-by-status");
        Add(entries, "governance.judicial_case", "governance.case-by-party", "governance.case-by-status");
        Add(entries, "governance.enforcement", "governance.enforcement-by-subject", "governance.enforcement-by-status");
        Add(entries, "governance.military_authority", "governance.military-by-polity", "governance.military-by-status");
        Add(entries, "governance.border_control", "governance.border-by-boundary", "governance.border-by-jurisdiction");
        Add(entries, "governance.lineage", "governance.lineage-by-subject");

        Add(entries, "infrastructure.network_topology", "infrastructure.network-by-kind", "infrastructure.network-by-node");
        Add(entries, "infrastructure.transport_service", "infrastructure.transport-by-network", "infrastructure.transport-by-route");
        Add(entries, "infrastructure.water_service", "infrastructure.water-by-scope", "infrastructure.water-by-network");
        Add(entries, "infrastructure.power_service", "infrastructure.power-by-scope", "infrastructure.power-by-network");
        Add(entries, "infrastructure.communication_service", "infrastructure.communication-by-scope");
        Add(entries, "infrastructure.dependency", "infrastructure.dependency-by-consumer", "infrastructure.dependency-by-provider");
        Add(entries, "infrastructure.facility_service", "infrastructure.facility-by-facility", "infrastructure.facility-by-kind");
        Add(entries, "infrastructure.service_queue", "infrastructure.queue-by-service", "infrastructure.queue-by-requester");
        Add(entries, "information.delivery", "information.delivery-by-recipient", "information.delivery-by-status");
        Add(entries, "information.media_distribution", "information.media-by-claim", "information.media-by-publisher");
        Add(entries, "information.record_store", "information.record-by-subject", "information.record-by-kind");
        Add(entries, "information.address_place_index", "information.address-by-token", "information.place-by-scope");
        Add(entries, "infrastructure.failure_recovery", "infrastructure.failure-by-subject", "infrastructure.failure-by-status");
        Add(entries, "infrastructure.lineage", "infrastructure.lineage-by-subject");

        var canonical = entries.OrderBy(static entry => entry.IndexId.Value, StringComparer.Ordinal).ToArray();
        Validate(canonical);
        return canonical;
    }

    private static void Add(List<RequiredSecondaryIndexRegistrationV1> entries, string partitionId, params string[] indexIds)
    {
        var partition = StandardDomainPartitionRegistry.Get(partitionId);
        foreach (var indexId in indexIds)
        {
            entries.Add(new RequiredSecondaryIndexRegistrationV1(
                new StableToken(indexId),
                partition.PartitionId,
                IndexAuthorityV1.DerivedRebuildable));
        }
    }

    private static void Validate(IReadOnlyList<RequiredSecondaryIndexRegistrationV1> entries)
    {
        if (entries.Count < StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidOperationException("Every standard partition requires at least one secondary index registration.");

        var duplicateIndex = entries
            .GroupBy(static entry => entry.IndexId.Value, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() != 1);
        if (duplicateIndex is not null)
            throw new InvalidOperationException($"Duplicate secondary IndexId: {duplicateIndex.Key}");

        foreach (var partition in StandardDomainPartitionRegistry.Entries)
        {
            if (!entries.Any(entry => entry.PartitionId == partition.PartitionId))
                throw new InvalidOperationException($"Missing required secondary index registration for {partition.PartitionId.Value}.");
        }

        foreach (var entry in entries)
        {
            if (entry.Authority != IndexAuthorityV1.DerivedRebuildable)
                throw new InvalidOperationException($"Standard secondary index must be DERIVED_REBUILDABLE: {entry.IndexId.Value}.");
            _ = StandardDomainPartitionRegistry.Get(entry.PartitionId.Value);
        }

        for (var index = 1; index < entries.Count; index++)
        {
            if (string.CompareOrdinal(entries[index - 1].IndexId.Value, entries[index].IndexId.Value) >= 0)
                throw new InvalidOperationException("Secondary index registry is not in canonical IndexId order.");
        }
    }
}

public sealed class DerivedRecordIndexV1<TKey> where TKey : notnull
{
    private readonly SortedDictionary<TKey, SortedSet<OpaqueId128>> _entries;

    private DerivedRecordIndexV1(
        RequiredSecondaryIndexRegistrationV1 registration,
        IComparer<TKey> keyComparer)
    {
        Registration = registration;
        _entries = new SortedDictionary<TKey, SortedSet<OpaqueId128>>(keyComparer);
    }

    public RequiredSecondaryIndexRegistrationV1 Registration { get; }

    public static DerivedRecordIndexV1<TKey> Rebuild<TPayload>(
        string indexId,
        DomainPartitionStateV1<TPayload> source,
        Func<DomainRecordEnvelopeV1<TPayload>, IEnumerable<TKey>> selectKeys,
        IComparer<TKey>? keyComparer = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selectKeys);
        var registration = StandardSecondaryIndexRegistry.Get(indexId);
        if (registration.PartitionId != source.Identity.PartitionId)
            throw new InvalidDataException("domain.index-partition-mismatch");
        if (registration.Authority != IndexAuthorityV1.DerivedRebuildable)
            throw new InvalidDataException("domain.index-not-rebuildable");

        var result = new DerivedRecordIndexV1<TKey>(registration, keyComparer ?? Comparer<TKey>.Default);
        foreach (var record in source.RecordsCanonical)
        {
            var keys = selectKeys(record)
                ?? throw new InvalidDataException("domain.index-null-key-sequence");
            foreach (var key in keys)
            {
                if (!result._entries.TryGetValue(key, out var ids))
                {
                    ids = new SortedSet<OpaqueId128>();
                    result._entries.Add(key, ids);
                }
                ids.Add(record.RecordId);
            }
        }
        return result;
    }

    public IReadOnlyList<OpaqueId128> Lookup(TKey key)
        => _entries.TryGetValue(key, out var ids) ? ids.ToArray() : Array.Empty<OpaqueId128>();

    public IEnumerable<KeyValuePair<TKey, IReadOnlyList<OpaqueId128>>> CanonicalEntries
        => _entries.Select(static pair => new KeyValuePair<TKey, IReadOnlyList<OpaqueId128>>(pair.Key, pair.Value.ToArray()));
}
